using FinMy.Application.Abstractions;
using FinMy.Application.Months;
using FinMy.Domain.Common;
using FinMy.Domain.Monetary;
using FinMy.Domain.Months;

namespace FinMy.Application.Panel;

/// <summary>
/// <c>GET /years/{yyyy}/panel</c> (006 US1, research R1): os 12 meses do ano com Previsto e Realizado, compostos pelo mesmo
/// <see cref="MonthSummaryCalculator"/> da visão do mês a partir de uma única leitura agregada (nunca 12 leituras do mês).
/// </summary>
public sealed class GetYearPanelHandler(IMonthReader reader, IClock clock)
{
    private const int FirstYear = 2000;
    private const int LastYear = 2100;

    public async Task<YearPanelResult> HandleAsync(string? yearText, CancellationToken cancellationToken)
    {
        if (!int.TryParse(yearText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var year)
            || year is < FirstYear or > LastYear)
        {
            const string message = "Use um ano entre 2000 e 2100";
            throw new DomainException(new DomainError(
                "year.invalid", message, DomainErrorKind.Validation, new Dictionary<string, string[]> { ["year"] = [message] }));
        }

        var today = clock.Today;
        var data = await reader.ReadYearAsync(year, today, cancellationToken);

        var months = Enumerable.Range(1, 12)
            .Select(m => YearMonth.Of(year, m))
            .Select(month => new PanelMonth(
                month,
                MonthSummaryCalculator.Calculate(month, data.Wallets, data.EntryTotals, data.TransferTotals, data.InvoiceFigures)))
            .ToList();

        return new YearPanelResult(year, today, months, Totals(months, m => m.Summary.Planned), Totals(months, m => m.Summary.Actual));
    }

    private static YearTotals Totals(List<PanelMonth> months, Func<PanelMonth, SummaryColumn> column)
    {
        var columns = months.Select(column).ToList();
        return new YearTotals(
            columns.Sum(c => c.IncomeCents),
            columns.Sum(c => c.ExpenseCents),
            columns.Sum(c => c.ResultCents),
            columns[^1].FinalBalanceCents);
    }
}
