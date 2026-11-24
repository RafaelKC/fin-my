using FinMy.Application.Abstractions;
using FinMy.Application.Cards;
using FinMy.Application.Entries;
using FinMy.Application.Tags;
using FinMy.Domain.Cards;
using FinMy.Domain.Common;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;
using FinMy.Domain.Recurrences;

namespace FinMy.Application.Recurrences;

/// <param name="Type"><c>entry</c> (Entrada/Saída) ou <c>card_purchase</c> (compra à vista).</param>
/// <param name="EndType"><c>none</c> (padrão), <c>until_month</c> ou <c>after_count</c> (o lançamento conta como a 1ª ocorrência).</param>
public sealed record CreateRecurrenceFromItemCommand(string? Type, Guid? Id, string? EndType, string? EndMonth, int? OccurrenceLimit);

/// <summary>
/// "Repetir todo mês" (005 US4, FR-010, research R5): cria a recorrência com os dados do lançamento (dia e mês inicial vêm da
/// data; a regra de mês, do Mês de referência), faz o próprio lançamento ser a ocorrência daquele mês e gera as seguintes.
/// Compra parcelada ou de crédito e lançamento que já é ocorrência não são elegíveis.
/// </summary>
public sealed class CreateRecurrenceFromItemHandler(
    IRecurrenceStore store,
    IEntryStore wallets,
    ICreditCardStore cards,
    ICardPurchaseStore purchases,
    ITagStore tags,
    RecurrenceGenerator generator,
    RecurrenceViewBuilder views)
{
    public async Task<RecurrenceView> HandleAsync(CreateRecurrenceFromItemCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var isEntry = command.Type switch
        {
            "entry" => true,
            "card_purchase" => false,
            _ => throw new DomainException(RecurrenceErrors.Field("type.invalid", "type", "Escolha Entrada/Saída ou Compra")),
        };
        var id = command.Id is { } given && given != Guid.Empty
            ? given
            : throw new DomainException(RecurrenceErrors.Field("id.required", "id", "Informe o lançamento"));
        var endType = RecurrenceInput.ParseEndType(command.EndType);
        var endMonth = RecurrenceInput.ParseOptionalMonth(command.EndMonth, "endMonth");

        var item = isEntry
            ? await LoadEntryAsync(id, cancellationToken)
            : await LoadPurchaseAsync(id, cancellationToken);

        // As tags do lançamento passam como estão (inclusive arquivadas: a recorrência fica "para revisar").
        var selection = await TagSelector.ResolveAsync(tags, item.TagIds, item.TagIds, cancellationToken);
        var startMonth = YearMonth.From(item.Date);
        var recurrence = Recurrence.Create(
            item.Kind, item.Description, item.Amount, item.Date.Day, item.WalletId, item.CreditCardId, selection.SettlementTagId,
            startMonth, endType, endMonth, command.OccurrenceLimit, item.Rule);

        if (item.WalletId is { } walletId)
        {
            var wallet = await wallets.GetWalletAsync(walletId, cancellationToken) ?? throw new DomainException(EntryErrors.WalletNotFound);
            if (wallet.IsArchived)
            {
                throw new DomainException(EntryErrors.WalletArchived);
            }
        }

        if (item.CreditCardId is { } cardId)
        {
            await CardPurchaseInput.RequireCardAsync(cards, cardId, mustBeActive: true, cancellationToken);
        }

        await using (var transaction = await store.BeginTransactionAsync(cancellationToken))
        {
            store.Add(recurrence, selection.CommonTagIds);
            item.Link(recurrence.Id, startMonth);

            // O lançamento já é a ocorrência do mês inicial: o gerador começa no mês seguinte.
            recurrence.AdvanceGeneratedUntil(startMonth);
            var allTagIds = selection.SettlementTagId is { } settlement ? [settlement, .. selection.CommonTagIds] : selection.CommonTagIds;
            await generator.StageAsync(recurrence, allTagIds, cancellationToken);
            await store.SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return (await views.BuildAsync([recurrence], cancellationToken))[0];
    }

    private async Task<Source> LoadEntryAsync(Guid id, CancellationToken cancellationToken)
    {
        var entry = await store.FindEntryAsync(id, cancellationToken) ?? throw new DomainException(EntryErrors.NotFound);
        if (entry.RecurrenceId is not null)
        {
            throw new DomainException(RecurrenceErrors.ItemNotEligible);
        }

        var view = await wallets.GetAsync(id, cancellationToken) ?? throw new DomainException(EntryErrors.NotFound);
        var dateMonth = YearMonth.From(entry.EntryDate);
        var rule = entry.ReferenceYearMonth == dateMonth.Next() ? ReferenceMonthRule.NextMonth : ReferenceMonthRule.SameMonth;
        return new Source(
            entry.Kind == EntryKind.Income ? RecurrenceKind.Income : RecurrenceKind.Expense,
            entry.Description, entry.Amount, entry.EntryDate, entry.WalletId, null, rule, view.TagIds,
            (recurrenceId, month) => entry.LinkToRecurrence(recurrenceId, month));
    }

    private async Task<Source> LoadPurchaseAsync(Guid id, CancellationToken cancellationToken)
    {
        var purchase = await purchases.FindAsync(id, cancellationToken) ?? throw new DomainException(CardPurchaseErrors.NotFound);
        if (!purchase.IsRecurrenceEligible)
        {
            throw new DomainException(RecurrenceErrors.ItemNotEligible);
        }

        var tagIds = await purchases.GetTagIdsAsync(purchase, cancellationToken);
        return new Source(
            RecurrenceKind.CardPurchase, purchase.Description, purchase.Amount, purchase.PurchaseDate, null, purchase.CreditCardId, null, tagIds,
            (recurrenceId, month) => purchase.LinkToRecurrence(recurrenceId, month));
    }

    /// <summary>O que a recorrência herda do lançamento e como ele se liga a ela.</summary>
    private sealed record Source(
        RecurrenceKind Kind,
        string Description,
        Money Amount,
        DateOnly Date,
        Guid? WalletId,
        Guid? CreditCardId,
        ReferenceMonthRule? Rule,
        IReadOnlyList<Guid> TagIds,
        Action<Guid, YearMonth> Attach)
    {
        public void Link(Guid recurrenceId, YearMonth month) => Attach(recurrenceId, month);
    }
}
