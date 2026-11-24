using FinMy.Application.Planning;

namespace FinMy.Application.Abstractions;

/// <summary>Leitura da grade do ano (007 research R2): lançamentos do ano, cartões e faturas do ano, sem filtro de Carteiras.</summary>
public interface IPlanningReader
{
    Task<PlanningData> ReadAsync(int year, DateOnly today, CancellationToken cancellationToken);
}
