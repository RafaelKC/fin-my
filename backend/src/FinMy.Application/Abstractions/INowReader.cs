using FinMy.Application.Months;

namespace FinMy.Application.Abstractions;

/// <summary>Consultas do Resumo de agora (006 research R2), sempre no Espaço do contexto.</summary>
public interface INowReader
{
    /// <summary>
    /// Faturas com Restante &gt; 0 e vencimento entre <paramref name="firstDue"/> e <paramref name="lastDue"/> (inclusive), em ordem de
    /// vencimento, cartão, mês e id; o Restante e a situação são da fatura inteira, como na visão do mês.
    /// </summary>
    Task<IReadOnlyList<MonthInvoiceItem>> ReadInvoicesAsync(DateOnly firstDue, DateOnly lastDue, DateOnly today, CancellationToken cancellationToken);

    /// <summary>Pendências (002 FR-019): Previstas com Mês de referência anterior ao mês de <paramref name="today"/>.</summary>
    Task<PendingSummary> ReadPendingAsync(DateOnly today, CancellationToken cancellationToken);
}
