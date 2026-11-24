namespace FinMy.Domain.Cards;

/// <summary>
/// Previsto da fatura com Estimativa (007 FR-010, research R1). Regra pura, com vetor em
/// <c>shared/test-vectors/invoice-forecast.json</c> (e porta no frontend).
/// </summary>
public static class InvoiceForecast
{
    /// <summary>A estimativa só vale enquanto a fatura não fechou: hoje &lt; fechamento (hoje = fechamento já não vale).</summary>
    public static bool EstimateActive(long? estimateCents, DateOnly today, DateOnly closingDate) =>
        estimateCents is not null && today < closingDate;

    /// <summary>
    /// Com estimativa válida: <c>max(total, pago, estimativa, 0)</c>; sem ela, ou depois do fechamento: <c>max(total, pago, 0)</c>
    /// (003 FR-022). O Realizado não muda.
    /// </summary>
    public static long Planned(long totalCents, long paidCents, long? estimateCents, DateOnly today, DateOnly closingDate)
    {
        var withoutEstimate = Math.Max(Math.Max(totalCents, paidCents), 0);
        return EstimateActive(estimateCents, today, closingDate)
            ? Math.Max(withoutEstimate, estimateCents!.Value)
            : withoutEstimate;
    }
}
