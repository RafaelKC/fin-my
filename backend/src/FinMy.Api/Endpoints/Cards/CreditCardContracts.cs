using FinMy.Application.Abstractions;
using FinMy.Application.Cards;
using FinMy.Domain.Cards;

namespace FinMy.Api.Endpoints.Cards;

/// <summary>Corpo de <c>POST /credit-cards</c> (<c>CreditCardInput</c> do contrato). <c>limit</c> é texto avaliado pelo servidor.</summary>
public sealed record CreditCardRequest(string? Name, int? ClosingDay, int? DueDay, Guid? PaymentWalletId, string? Color, string? Limit)
{
    public CreditCardInput ToInput() => new(Name, ClosingDay, DueDay, PaymentWalletId, Color, Limit);
}

/// <summary>Corpo de <c>PUT /credit-cards/{id}</c>: os campos de <see cref="CreditCardRequest"/> mais <c>version</c>.</summary>
public sealed record UpdateCreditCardRequest(
    string? Name, int? ClosingDay, int? DueDay, Guid? PaymentWalletId, string? Color, string? Limit, long? Version)
{
    public CreditCardInput ToInput() => new(Name, ClosingDay, DueDay, PaymentWalletId, Color, Limit);
}

/// <summary>Resposta <c>CreditCard</c> do contrato.</summary>
public record CreditCardResponse(
    Guid Id,
    string Name,
    int ClosingDay,
    int DueDay,
    Guid PaymentWalletId,
    string Color,
    long? LimitCents,
    bool Archived,
    bool HasPurchases,
    long Version)
{
    public static CreditCardResponse From(CreditCardView c) => new(
        c.Id, c.Name, c.ClosingDay, c.DueDay, c.PaymentWalletId, c.Color, c.LimitCents, c.Archived, c.HasPurchases, c.Version);
}

/// <summary>Resposta <c>InvoiceSummary</c> do contrato.</summary>
public sealed record InvoiceSummaryResponse(
    Guid CreditCardId,
    string DueMonth,
    DateOnly ClosingDate,
    DateOnly DueDate,
    Guid? Id,
    string Status,
    long TotalCents,
    long PaidCents,
    long RemainingCents)
{
    public static InvoiceSummaryResponse From(InvoiceSummaryView i) => new(
        i.CreditCardId, i.DueMonth.ToString(), i.ClosingDate, i.DueDate, i.Id, i.Status.ToCode(), i.TotalCents, i.PaidCents, i.RemainingCents);
}

/// <summary>Resposta <c>CreditCardOverview</c> do contrato (<see cref="CreditCardResponse"/> mais o resumo das faturas e o limite).</summary>
public sealed record CreditCardOverviewResponse(
    Guid Id,
    string Name,
    int ClosingDay,
    int DueDay,
    Guid PaymentWalletId,
    string Color,
    long? LimitCents,
    bool Archived,
    bool HasPurchases,
    long Version,
    InvoiceSummaryResponse CurrentInvoice,
    InvoiceSummaryResponse NextInvoice,
    IReadOnlyList<InvoiceSummaryResponse> OpenPastInvoices,
    long? AvailableLimitCents)
    : CreditCardResponse(Id, Name, ClosingDay, DueDay, PaymentWalletId, Color, LimitCents, Archived, HasPurchases, Version)
{
    public static CreditCardOverviewResponse From(CreditCardOverviewView o) => new(
        o.Card.Id, o.Card.Name, o.Card.ClosingDay, o.Card.DueDay, o.Card.PaymentWalletId, o.Card.Color, o.Card.LimitCents,
        o.Card.Archived, o.Card.HasPurchases, o.Card.Version,
        InvoiceSummaryResponse.From(o.CurrentInvoice),
        InvoiceSummaryResponse.From(o.NextInvoice),
        o.OpenPastInvoices.Select(InvoiceSummaryResponse.From).ToList(),
        o.AvailableLimitCents);
}

/// <summary>Resposta <c>InvoiceRef</c> do contrato.</summary>
public sealed record InvoiceRefResponse(Guid CreditCardId, string DueMonth, DateOnly ClosingDate, DateOnly DueDate)
{
    public static InvoiceRefResponse From(InvoiceRefView r) => new(r.CreditCardId, r.DueMonth.ToString(), r.ClosingDate, r.DueDate);
}
