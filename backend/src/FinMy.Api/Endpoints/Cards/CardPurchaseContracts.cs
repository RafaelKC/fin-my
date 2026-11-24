using FinMy.Application.Abstractions;
using FinMy.Application.Cards;

namespace FinMy.Api.Endpoints.Cards;

/// <summary>Parte <c>installments</c> de <c>POST /card-purchases</c>; ausente = à vista.</summary>
public sealed record InstallmentsRequest(int? Total, string? Mode, int? NextNumber, string? FirstInvoiceMonth);

/// <summary>Corpo de <c>POST /card-purchases</c> (<c>CardPurchaseCreate</c>). <c>amount</c> é texto avaliado pelo servidor; negativo vira crédito.</summary>
public sealed record CardPurchaseCreateRequest(
    string? Description,
    string? Amount,
    string? Nature,
    DateOnly? Date,
    Guid? CreditCardId,
    string? Notes,
    InstallmentsRequest? Installments,
    bool? ConfirmPaidInvoice,
    IReadOnlyList<Guid>? TagIds = null)
{
    internal CreateCardPurchaseCommand ToCommand() => new(
        Description,
        Amount,
        Nature,
        Date,
        CreditCardId,
        Notes,
        Installments is null ? null : new InstallmentsCommand(Installments.Total, Installments.Mode, Installments.NextNumber, Installments.FirstInvoiceMonth),
        ConfirmPaidInvoice ?? false,
        TagIds);
}

/// <summary>Corpo de <c>PUT /card-purchases/{id}</c>.</summary>
public sealed record CardPurchaseUpdateRequest(
    string? Description,
    string? Amount,
    string? Nature,
    DateOnly? Date,
    Guid? CreditCardId,
    string? Notes,
    string? Scope,
    bool? ConfirmPaidInvoice,
    long? Version,
    IReadOnlyList<Guid>? TagIds = null)
{
    internal UpdateCardPurchaseCommand ToCommand(Guid id) =>
        new(id, Description, Amount, Nature, Date, CreditCardId, Notes, Scope, ConfirmPaidInvoice ?? false, Version, TagIds);
}

/// <summary>Corpo de <c>PATCH /card-purchases/{id}/reconciled</c>.</summary>
public sealed record CardPurchaseReconciledRequest(bool? Reconciled);

/// <summary>Corpo de <c>POST /card-purchases/{id}/move</c>.</summary>
public sealed record CardPurchaseMoveRequest(string? Direction, string? Scope);

/// <summary>Corpo de <c>PUT /installment-plans/{id}</c>.</summary>
public sealed record InstallmentPlanUpdateRequest(int? InstallmentTotal, string? Amount, string? AmountMode);

/// <summary>Resposta <c>CardPurchase</c> do contrato; dinheiro em centavos.</summary>
public sealed record CardPurchaseResponse(
    Guid Id,
    Guid CreditCardId,
    string DueMonth,
    string Description,
    string DisplayDescription,
    long AmountCents,
    string Nature,
    DateOnly Date,
    string Origin,
    bool Reconciled,
    bool ManuallyMoved,
    string? Notes,
    Guid? InstallmentPlanId,
    int? InstallmentNumber,
    int? InstallmentTotal,
    DateTimeOffset CreatedAt,
    string CreatedVia,
    long Version,
    IReadOnlyList<Guid> TagIds,
    Guid? SettlementTagId,
    Guid? SplitGroupId,
    int SplitCount,
    Guid? RecurrenceId,
    string? RecurrenceMonth,
    bool RecurrenceModified)
{
    public static CardPurchaseResponse From(CardPurchaseView p) => new(
        p.Id,
        p.CreditCardId,
        p.DueMonth.ToString(),
        p.Description,
        p.DisplayDescription,
        p.AmountCents,
        ToText(p.Nature),
        p.Date,
        ToText(p.Origin),
        p.Reconciled,
        p.ManuallyMoved,
        p.Notes,
        p.InstallmentPlanId,
        p.InstallmentNumber,
        p.InstallmentTotal,
        p.CreatedAt,
        ChannelText.Of(p.CreatedVia),
        p.Version,
        p.TagIds,
        p.SettlementTagId,
        p.SplitGroupId,
        p.SplitCount,
        p.RecurrenceId,
        p.RecurrenceMonth?.ToString(),
        p.RecurrenceModified);

    private static string ToText(Domain.Cards.PurchaseNature nature) => nature == Domain.Cards.PurchaseNature.Credit ? "credit" : "debit";

    private static string ToText(Domain.Cards.PurchaseOrigin origin) => origin switch
    {
        Domain.Cards.PurchaseOrigin.Installment => "installment",
        Domain.Cards.PurchaseOrigin.CarryOver => "carry_over",
        Domain.Cards.PurchaseOrigin.Recurrence => "recurrence",
        _ => "manual",
    };
}

/// <summary>Resposta de <c>POST /card-purchases</c>: as compras criadas e os avisos (<c>recorded_as_credit</c>).</summary>
public sealed record CardPurchaseCreateResponse(IReadOnlyList<CardPurchaseResponse> Purchases, IReadOnlyList<string> Warnings);
