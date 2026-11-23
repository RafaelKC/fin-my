using FinMy.Application.Months;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Abstractions;

/// <summary>Consultas agregadas da visão do mês (research R4), sempre no Espaço do contexto.</summary>
public interface IMonthReader
{
    /// <summary>
    /// Lê os agregados e as listas do mês <paramref name="month"/> para as Carteiras de <paramref name="walletIds"/>
    /// (<c>null</c> = todas, ativas e arquivadas; ids que não são de Carteiras do Espaço são ignorados).
    /// </summary>
    Task<MonthData> ReadAsync(YearMonth month, IReadOnlyCollection<Guid>? walletIds, DateOnly today, CancellationToken cancellationToken);
}
