namespace FinMy.Domain.Cards;

/// <summary>Situação da fatura (FR-006).</summary>
public enum InvoiceStatus
{
    NoAmountDue,
    Paid,
    Open,
    Closed,
    Overdue,
}

/// <summary>Regra da situação da fatura, avaliada em ordem (FR-006).</summary>
public static class InvoiceStatusRule
{
    /// <summary>
    /// (1) Total ≤ 0 → sem valor a pagar; (2) Restante (total − pago) ≤ 0 → paga; (3) hoje &lt; fechamento → aberta;
    /// (4) hoje ≤ vencimento → fechada; (5) senão → vencida.
    /// </summary>
    public static InvoiceStatus Evaluate(long totalCents, long paidCents, DateOnly today, DateOnly closingDate, DateOnly dueDate)
    {
        if (totalCents <= 0)
        {
            return InvoiceStatus.NoAmountDue;
        }

        if (totalCents - paidCents <= 0)
        {
            return InvoiceStatus.Paid;
        }

        if (today < closingDate)
        {
            return InvoiceStatus.Open;
        }

        return today <= dueDate ? InvoiceStatus.Closed : InvoiceStatus.Overdue;
    }

    /// <summary>Código estável usado na API e nos vetores.</summary>
    public static string ToCode(this InvoiceStatus status) => status switch
    {
        InvoiceStatus.NoAmountDue => "no_amount_due",
        InvoiceStatus.Paid => "paid",
        InvoiceStatus.Open => "open",
        InvoiceStatus.Closed => "closed",
        InvoiceStatus.Overdue => "overdue",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
}
