using FinMy.Api.Endpoints.Cards;
using FinMy.Api.Endpoints.Entries;
using FinMy.Application.Entries;
using FinMy.Application.Splits;

namespace FinMy.Api.Endpoints.Splits;

/// <summary>Parte de <c>split.parts</c>: <c>settlementTagId</c> nulo é "Eu"; <c>amount</c> só em <c>mode = amounts</c>.</summary>
public sealed record SplitPartRequest(Guid? SettlementTagId, string? Amount);

/// <summary>Objeto <c>split</c> (<c>Split</c>): <c>mode</c> é <c>equal|amounts</c>, com 2 a 10 partes.</summary>
public sealed record SplitRequest(string? Mode, IReadOnlyList<SplitPartRequest>? Parts)
{
    internal SplitCommand ToCommand() => new(Mode, Parts?.Select(p => new SplitPartCommand(p.SettlementTagId, p.Amount)).ToList());
}

/// <summary>Corpo de <c>POST /entries/split</c>: os campos de <c>EntryCreate</c> (<c>kind = expense</c>) mais <c>split</c>.</summary>
public sealed record EntrySplitRequest(
    string? Kind,
    string? Description,
    string? Amount,
    DateOnly? Date,
    Guid? WalletId,
    string? Status,
    string? ReferenceMonth,
    string? Notes,
    IReadOnlyList<Guid>? TagIds,
    SplitRequest? Split)
{
    internal CreateEntryCommand ToCommand() => new(Kind, Description, Amount, Date, WalletId, Status, ReferenceMonth, Notes, TagIds);
}

/// <summary>Corpo de <c>POST /card-purchases/split</c>: os campos de <c>CardPurchaseCreate</c> mais <c>split</c>.</summary>
public sealed record CardPurchaseSplitRequest(
    string? Description,
    string? Amount,
    string? Nature,
    DateOnly? Date,
    Guid? CreditCardId,
    string? Notes,
    InstallmentsRequest? Installments,
    bool? ConfirmPaidInvoice,
    IReadOnlyList<Guid>? TagIds,
    SplitRequest? Split)
{
    internal Application.Cards.CreateCardPurchaseCommand ToCommand() =>
        new CardPurchaseCreateRequest(Description, Amount, Nature, Date, CreditCardId, Notes, Installments, ConfirmPaidInvoice, TagIds).ToCommand();
}
