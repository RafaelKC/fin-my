using FinMy.Application.Abstractions;
using FinMy.Application.Cards;
using FinMy.Application.Entries;
using FinMy.Application.Tags;
using FinMy.Domain.Common;
using FinMy.Domain.Recurrences;

namespace FinMy.Application.Recurrences;

/// <param name="Kind"><c>income</c>, <c>expense</c> ou <c>card_purchase</c>.</param>
/// <param name="Amount">Expressão de valor (RT-05).</param>
/// <param name="StartMonth"><c>AAAA-MM</c>.</param>
/// <param name="EndType"><c>none</c> (padrão), <c>until_month</c> ou <c>after_count</c>.</param>
/// <param name="ReferenceMonthRule"><c>same_month</c> (padrão) ou <c>next_month</c>; proibido em compra.</param>
public sealed record CreateRecurrenceCommand(
    string? Kind,
    string? Description,
    string? Amount,
    int? DayOfMonth,
    Guid? WalletId,
    Guid? CreditCardId,
    IReadOnlyList<Guid>? TagIds,
    string? StartMonth,
    string? EndType,
    string? EndMonth,
    int? OccurrenceLimit,
    string? ReferenceMonthRule);

/// <summary>Cria a recorrência e já gera as ocorrências até o horizonte, na mesma transação (US1, FR-003).</summary>
public sealed class CreateRecurrenceHandler(
    IRecurrenceStore store,
    IEntryStore wallets,
    ICreditCardStore cards,
    ITagStore tags,
    RecurrenceGenerator generator,
    RecurrenceViewBuilder views)
{
    public async Task<RecurrenceView> HandleAsync(CreateRecurrenceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var kind = RecurrenceInput.ParseKind(command.Kind);
        var amount = EntryInput.ParseAmount(command.Amount);
        var startMonth = RecurrenceInput.ParseMonth(command.StartMonth, "startMonth");
        var endType = RecurrenceInput.ParseEndType(command.EndType);
        var endMonth = RecurrenceInput.ParseOptionalMonth(command.EndMonth, "endMonth");
        var rule = RecurrenceInput.ParseReferenceMonthRule(command.ReferenceMonthRule);
        if (kind != RecurrenceKind.CardPurchase)
        {
            rule ??= ReferenceMonthRule.SameMonth;
        }

        var walletId = command.WalletId is { } w && w != Guid.Empty ? w : (Guid?)null;
        var cardId = command.CreditCardId is { } c && c != Guid.Empty ? c : (Guid?)null;

        // Valida as regras da recorrência (tipo × carteira/cartão, dia, término) antes de consultar o banco.
        var selection = await TagSelector.ResolveAsync(tags, command.TagIds, [], cancellationToken);
        var recurrence = Recurrence.Create(
            kind, command.Description!, amount, command.DayOfMonth ?? 0, walletId, cardId, selection.SettlementTagId,
            startMonth, endType, endMonth, command.OccurrenceLimit, rule);

        if (walletId is { } id)
        {
            var wallet = await wallets.GetWalletAsync(id, cancellationToken) ?? throw new DomainException(EntryErrors.WalletNotFound);
            if (wallet.IsArchived)
            {
                throw new DomainException(EntryErrors.WalletArchived);
            }
        }

        if (cardId is { } card)
        {
            await CardPurchaseInput.RequireCardAsync(cards, card, mustBeActive: true, cancellationToken);
        }

        var allTagIds = selection.SettlementTagId is { } settlement ? [settlement, .. selection.CommonTagIds] : selection.CommonTagIds;
        await using (var transaction = await store.BeginTransactionAsync(cancellationToken))
        {
            store.Add(recurrence, selection.CommonTagIds);
            await generator.StageAsync(recurrence, allTagIds, cancellationToken);
            await store.SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return (await views.BuildAsync([recurrence], cancellationToken))[0];
    }
}

/// <summary>Obtém uma recorrência.</summary>
public sealed class GetRecurrenceHandler(IRecurrenceStore store, RecurrenceViewBuilder views)
{
    public async Task<RecurrenceView> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var recurrence = await store.FindAsync(id, cancellationToken) ?? throw new DomainException(RecurrenceErrors.NotFound);
        return (await views.BuildAsync([recurrence], cancellationToken))[0];
    }
}

/// <summary>Lista as recorrências ativas, encerradas ou todas e soma o total mensal das ativas (FR-009, research R6).</summary>
public sealed class ListRecurrencesHandler(IRecurrenceStore store, RecurrenceViewBuilder views)
{
    public async Task<RecurrenceListView> HandleAsync(string? status, CancellationToken cancellationToken)
    {
        var wanted = status switch
        {
            null or "" or RecurrenceViewBuilder.Active => RecurrenceViewBuilder.Active,
            RecurrenceViewBuilder.Ended => RecurrenceViewBuilder.Ended,
            "all" => null,
            _ => throw new DomainException(RecurrenceErrors.Field("status.invalid", "status", "Use active, ended ou all")),
        };

        var all = await views.BuildAsync(await store.ListAsync(cancellationToken), cancellationToken);
        var active = all.Where(v => v.Status == RecurrenceViewBuilder.Active).ToList();
        var items = wanted is null ? all : all.Where(v => v.Status == wanted).ToList();

        return new RecurrenceListView(
            items,
            active.Where(v => v.Kind == RecurrenceKind.Income).Sum(v => v.AmountCents),
            active.Where(v => v.Kind != RecurrenceKind.Income).Sum(v => v.AmountCents));
    }
}
