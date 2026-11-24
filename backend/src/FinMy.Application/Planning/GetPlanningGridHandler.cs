using FinMy.Application.Abstractions;
using FinMy.Application.Panel;
using FinMy.Domain.Entries;

namespace FinMy.Application.Planning;

/// <summary>
/// <c>GET /years/{yyyy}/planning</c> (007 US1, research R2): linhas calculadas dos lançamentos e das faturas do ano, e totais
/// Previstos tirados do mesmo painel do ano (006) — por isso idênticos a ele (SC-002). O ano é validado pelo painel.
/// </summary>
public sealed class GetPlanningGridHandler(GetYearPanelHandler panel, IPlanningReader reader)
{
    public async Task<PlanningGridResult> HandleAsync(string? yearText, CancellationToken cancellationToken)
    {
        var year = await panel.HandleAsync(yearText, cancellationToken);
        var data = await reader.ReadAsync(year.Year, year.Today, cancellationToken);

        var totals = year.Months
            .Select(m => new PlanningTotalsMonth(
                m.Month, m.Summary.Planned.IncomeCents, m.Summary.Planned.ExpenseCents, m.Summary.Planned.ResultCents, m.Summary.Planned.FinalBalanceCents))
            .ToList();

        return new PlanningGridResult(
            year.Year,
            year.Today,
            PlanningGridBuilder.Rows(EntryKind.Income, year.Year, data),
            PlanningGridBuilder.Rows(EntryKind.Expense, year.Year, data),
            PlanningGridBuilder.InvoiceRows(year.Year, data),
            totals);
    }
}
