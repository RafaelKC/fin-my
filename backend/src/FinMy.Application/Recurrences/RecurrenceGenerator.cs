using FinMy.Application.Abstractions;
using FinMy.Domain.Cards;
using FinMy.Domain.Entries;
using FinMy.Domain.Recurrences;

namespace FinMy.Application.Recurrences;

/// <summary>
/// Cria as ocorrências que faltam de cada recorrência e avança <c>generated_until_month</c> (005 research R2, FR-003, FR-008).
/// Como só cria meses posteriores ao último gerado, uma ocorrência excluída não volta. Entrada/Saída nascem Previstas;
/// compra no cartão nasce à vista, débito, com origem <c>recurrence</c> e fatura pelo ciclo do cartão.
/// </summary>
public sealed class RecurrenceGenerator(
    IRecurrenceStore store,
    ICreditCardStore cards,
    ICardPurchaseStore purchases,
    IInvoiceRepository invoices,
    RecurrenceReviewer reviewer,
    IClock clock)
{
    /// <summary>
    /// Acrescenta ao contexto as ocorrências que faltam de <paramref name="recurrence"/> (e avança o último mês gerado);
    /// quem chama abre a transação e grava. Recorrência com carteira, cartão ou tag arquivados não gera nada.
    /// Devolve quantas ocorrências foram criadas.
    /// </summary>
    public async Task<int> StageAsync(Recurrence recurrence, IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recurrence);
        ArgumentNullException.ThrowIfNull(tagIds);

        if (await reviewer.ReasonAsync(recurrence, tagIds, cancellationToken) is not null)
        {
            return 0;
        }

        CreditCard? card = null;
        if (recurrence.CreditCardId is { } cardId)
        {
            card = await cards.FindAsync(cardId, cancellationToken)
                ?? throw new InvalidOperationException($"O cartão {cardId} da recorrência {recurrence.Id} não existe.");
        }

        var occurrences = RecurrenceScheduler.Occurrences(
            recurrence, recurrence.GeneratedUntil, clock.Today, card is null ? null : (card.ClosingDay, card.DueDay));
        if (occurrences.Count == 0)
        {
            return 0;
        }

        var commonTagIds = tagIds.Where(t => t != recurrence.SettlementTagId).ToList();
        foreach (var occurrence in occurrences)
        {
            if (card is null)
            {
                var entry = Entry.Create(
                    recurrence.Kind == RecurrenceKind.Income ? EntryKind.Income : EntryKind.Expense,
                    recurrence.Description,
                    recurrence.Amount,
                    occurrence.Date,
                    recurrence.WalletId!.Value,
                    EntryStatus.Planned,
                    occurrence.ReferenceMonth,
                    null,
                    clock.Today);
                entry.LinkToRecurrence(recurrence.Id, occurrence.Month);
                entry.ChangeSettlementTag(recurrence.SettlementTagId);
                store.AddOccurrence(entry, commonTagIds);
            }
            else
            {
                var invoice = await invoices.GetOrCreateAsync(card.Id, occurrence.InvoiceDueMonth!.Value, cancellationToken);
                var purchase = CardPurchase.CreateFromRecurrence(
                    card.Id, invoice.Id, recurrence.Description, recurrence.Amount, occurrence.Date, recurrence.Id, occurrence.Month);
                purchase.ChangeSettlementTag(recurrence.SettlementTagId);
                purchases.Add(purchase);
                purchases.AddTags(purchase, commonTagIds);
            }
        }

        recurrence.AdvanceGeneratedUntil(occurrences[^1].Month);
        return occurrences.Count;
    }

    /// <summary>
    /// Estende o horizonte de todas as recorrências do Espaço do contexto (job diário). Cada recorrência numa transação;
    /// idempotente: sem meses faltando, não grava nada. Devolve quantas ocorrências foram criadas.
    /// </summary>
    public async Task<int> GenerateAllAsync(CancellationToken cancellationToken)
    {
        var pending = await store.ListPendingGenerationAsync(RecurrenceScheduler.Horizon(clock.Today), cancellationToken);
        var tags = await store.GetTagIdsAsync(pending, cancellationToken);

        var total = 0;
        foreach (var recurrence in pending)
        {
            await using var transaction = await store.BeginTransactionAsync(cancellationToken);
            var created = await StageAsync(recurrence, tags.GetValueOrDefault(recurrence.Id) ?? [], cancellationToken);
            if (created == 0)
            {
                continue;
            }

            await store.SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            total += created;
        }

        return total;
    }
}
