using FinMy.Application.Planning;
using FinMy.Domain.Entries;

namespace FinMy.Api.Endpoints.Planning;

public sealed record PlanningCellDto(string Month, long PlannedCents, int ItemCount, bool AllSettled, bool Editable, Guid? EntryId);

public sealed record PlanningRowDto(
    string Kind, string Key, string Description, Guid? LatestWalletId, int LatestDay, IReadOnlyList<Guid> LatestTagIds, IReadOnlyList<PlanningCellDto> Cells);

public sealed record InvoiceCellDto(string Month, long PlannedCents, long TotalCents, long? EstimateCents, bool Editable);

public sealed record InvoiceRowDto(Guid CreditCardId, string CreditCardName, bool Archived, IReadOnlyList<InvoiceCellDto> Cells);

public sealed record PlanningTotalsMonthDto(string Month, long IncomeCents, long ExpenseCents, long ResultCents, long FinalBalanceCents);

public sealed record PlanningTotalsDto(IReadOnlyList<PlanningTotalsMonthDto> Months);

/// <summary>Resposta de <c>GET /years/{yyyy}/planning</c> (007 contracts/api.md).</summary>
public sealed record PlanningGridDto(
    int Year,
    DateOnly Today,
    IReadOnlyList<PlanningRowDto> IncomeRows,
    IReadOnlyList<PlanningRowDto> ExpenseRows,
    IReadOnlyList<InvoiceRowDto> InvoiceRows,
    PlanningTotalsDto Totals)
{
    public static PlanningGridDto From(PlanningGridResult r) => new(
        r.Year,
        r.Today,
        r.IncomeRows.Select(From).ToList(),
        r.ExpenseRows.Select(From).ToList(),
        r.InvoiceRows.Select(row => new InvoiceRowDto(
            row.CreditCardId,
            row.CreditCardName,
            row.Archived,
            row.Cells.Select(c => new InvoiceCellDto(c.Month.ToString(), c.PlannedCents, c.TotalCents, c.EstimateCents, true)).ToList())).ToList(),
        new PlanningTotalsDto(r.Totals.Select(t => new PlanningTotalsMonthDto(t.Month.ToString(), t.IncomeCents, t.ExpenseCents, t.ResultCents, t.FinalBalanceCents)).ToList()));

    private static PlanningRowDto From(PlanningRow row) => new(
        row.Kind == EntryKind.Income ? "income" : "expense",
        row.Key,
        row.Description,
        row.LatestWalletId,
        row.LatestDay,
        row.LatestTagIds,
        row.Cells.Select(c => new PlanningCellDto(c.Month.ToString(), c.PlannedCents, c.ItemCount, c.AllSettled, c.Editable, c.EntryId)).ToList());
}

/// <summary>Grade do ano (007 US1): <c>GET /api/v1/years/{yyyy}/planning</c>.</summary>
public sealed class PlanningEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/v1/years").WithTags("planning").RequireAuthorization();

        group.MapGet("/{year}/planning", async (string year, GetPlanningGridHandler handler, HttpContext http, CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(year, cancellationToken);
            http.Response.Headers.CacheControl = "no-store";
            return Results.Ok(PlanningGridDto.From(result));
        });
    }
}
