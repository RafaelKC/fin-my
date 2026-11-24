using FinMy.Application.Months;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Abstractions;

/// <summary>Consultas agregadas da visão do mês (research R4), sempre no Espaço do contexto.</summary>
public interface IMonthReader
{
    /// <summary>
    /// Lê os agregados e as listas do mês <paramref name="month"/> para as Carteiras de <paramref name="walletIds"/>
    /// (<c>null</c> = todas, ativas e arquivadas; ids que não são de Carteiras do Espaço são ignorados).
    /// <paramref name="tagIds"/> não vazio filtra só as listas (Entradas e Saídas com qualquer das tags; as transferências saem; entram as
    /// compras das faturas do mês com qualquer das tags): resumo e pendências não mudam (004 research R6).
    /// </summary>
    Task<MonthData> ReadAsync(
        YearMonth month, IReadOnlyCollection<Guid>? walletIds, IReadOnlyCollection<Guid> tagIds, DateOnly today, CancellationToken cancellationToken);

    /// <summary>
    /// Agregados do painel do ano (006 research R1): carteiras, lançamentos, transferências e faturas até dezembro de
    /// <paramref name="year"/>, sem filtro de Carteiras, no formato que o <c>MonthSummaryCalculator</c> consome.
    /// </summary>
    Task<YearData> ReadYearAsync(int year, DateOnly today, CancellationToken cancellationToken);
}
