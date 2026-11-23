using FinMy.Application.Abstractions;
using FinMy.Application.Cards;

namespace FinMy.Api.Endpoints.Cards;

/// <summary>Corpo (opcional) de <c>POST /credit-cards/{id}/invoices/{month}/payments</c>; ausente ou vazio = padrões (FR-017).</summary>
public sealed record InvoicePaymentCreateRequest(string? Amount, DateOnly? Date, Guid? WalletId)
{
    internal RegisterInvoicePaymentCommand ToCommand(Guid creditCardId, string? month) => new(creditCardId, month, Amount, Date, WalletId);
}

/// <summary>Corpo de <c>PUT /invoice-payments/{id}</c>.</summary>
public sealed record InvoicePaymentUpdateRequest(string? Amount, DateOnly? Date, Guid? WalletId, long? Version)
{
    internal UpdateInvoicePaymentCommand ToCommand(Guid id) => new(id, Amount, Date, WalletId, Version);
}

/// <summary>Resposta <c>InvoicePayment</c> do contrato.</summary>
public sealed record InvoicePaymentResponse(
    Guid Id, Guid CreditCardId, string DueMonth, Guid WalletId, long AmountCents, DateOnly Date, long Version)
{
    public static InvoicePaymentResponse From(InvoicePaymentView p) =>
        new(p.Id, p.CreditCardId, p.DueMonth.ToString(), p.WalletId, p.AmountCents, p.Date, p.Version);
}

/// <summary>Subtotal por Tag de acerto da fatura (004 FR-011); <c>tagId</c> nulo = "Sem tag de acerto".</summary>
public sealed record SettlementSubtotalResponse(Guid? TagId, long Cents);

/// <summary>Resposta <c>Invoice</c> do contrato (<c>InvoiceSummary</c> mais conferido, compras, pagamentos e meses vizinhos).</summary>
public sealed record InvoiceResponse(
    Guid CreditCardId,
    string DueMonth,
    DateOnly ClosingDate,
    DateOnly DueDate,
    Guid? Id,
    string Status,
    long TotalCents,
    long PaidCents,
    long RemainingCents,
    long ReconciledCents,
    bool CanCarryOver,
    IReadOnlyList<CardPurchaseResponse> Purchases,
    IReadOnlyList<InvoicePaymentResponse> Payments,
    string PreviousMonth,
    string NextMonth,
    long? FilteredTotalCents,
    IReadOnlyList<SettlementSubtotalResponse> SettlementSubtotals)
{
    public static InvoiceResponse From(InvoiceDetailView v)
    {
        var s = InvoiceSummaryResponse.From(v.Summary);
        return new InvoiceResponse(
            s.CreditCardId, s.DueMonth, s.ClosingDate, s.DueDate, s.Id, s.Status, s.TotalCents, s.PaidCents, s.RemainingCents,
            v.ReconciledCents,
            v.CanCarryOver,
            v.Purchases.Select(CardPurchaseResponse.From).ToList(),
            v.Payments.Select(InvoicePaymentResponse.From).ToList(),
            v.PreviousMonth.ToString(),
            v.NextMonth.ToString(),
            v.FilteredTotalCents,
            v.SettlementSubtotals.Select(t => new SettlementSubtotalResponse(t.TagId, t.Cents)).ToList());
    }
}
