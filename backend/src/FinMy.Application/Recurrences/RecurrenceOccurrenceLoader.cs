using FinMy.Application.Abstractions;
using FinMy.Domain.Cards;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Recurrences;

/// <summary>Ocorrências de uma recorrência separadas entre as elegíveis (005 FR-006) e as demais.</summary>
/// <param name="Invoices">Faturas das compras (elegíveis ou não), por id.</param>
public sealed record RecurrenceOccurrenceSet(
    IReadOnlyList<Entry> EligibleEntries,
    IReadOnlyList<Entry> OtherEntries,
    IReadOnlyList<CardPurchase> EligiblePurchases,
    IReadOnlyList<CardPurchase> OtherPurchases,
    IReadOnlyDictionary<Guid, Invoice> Invoices);

/// <summary>
/// Elegibilidade única de alterar, encerrar e excluir (005 research R4, FR-006): ocorrência do mês <c>M</c> em diante, não
/// alterada à mão e ainda mutável — Entrada/Saída Prevista, ou compra em fatura cuja data de fechamento é posterior a hoje.
/// Ocorrência Paga (ou em fatura fechada) nunca é alcançada.
/// </summary>
public sealed class RecurrenceOccurrenceLoader(IRecurrenceStore store, ICardPurchaseStore purchases, IClock clock)
{
    public async Task<RecurrenceOccurrenceSet> LoadAsync(Guid recurrenceId, YearMonth fromMonth, CancellationToken cancellationToken)
    {
        var from = fromMonth.FirstDay;
        var entries = await store.ListEntryOccurrencesAsync(recurrenceId, cancellationToken);
        var cardPurchases = await store.ListPurchaseOccurrencesAsync(recurrenceId, cancellationToken);
        var invoices = await purchases.GetInvoicesAsync(cardPurchases.Select(p => p.InvoiceId).Distinct().ToList(), cancellationToken);

        var today = clock.Today;
        bool EntryEligible(Entry e) => e.RecurrenceMonth >= from && !e.RecurrenceModified && e.Status == EntryStatus.Planned;
        bool PurchaseEligible(CardPurchase p) => p.RecurrenceMonth >= from && !p.RecurrenceModified && invoices[p.InvoiceId].ClosingDate > today;

        return new RecurrenceOccurrenceSet(
            entries.Where(EntryEligible).ToList(),
            entries.Where(e => !EntryEligible(e)).ToList(),
            cardPurchases.Where(PurchaseEligible).ToList(),
            cardPurchases.Where(p => !PurchaseEligible(p)).ToList(),
            invoices);
    }
}
