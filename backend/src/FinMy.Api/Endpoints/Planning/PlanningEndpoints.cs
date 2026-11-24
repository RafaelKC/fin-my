using FinMy.Api.Idempotency;
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
        r.InvoiceRows.Select(From).ToList(),
        TotalsOf(r.Totals));

    public static PlanningTotalsDto TotalsOf(IReadOnlyList<PlanningTotalsMonth> totals) =>
        new(totals.Select(t => new PlanningTotalsMonthDto(t.Month.ToString(), t.IncomeCents, t.ExpenseCents, t.ResultCents, t.FinalBalanceCents)).ToList());

    public static InvoiceRowDto From(InvoiceRow row) => new(
        row.CreditCardId,
        row.CreditCardName,
        row.Archived,
        row.Cells.Select(c => new InvoiceCellDto(c.Month.ToString(), c.PlannedCents, c.TotalCents, c.EstimateCents, true)).ToList());

    public static PlanningRowDto From(PlanningRow row) => new(
        row.Kind == EntryKind.Income ? "income" : "expense",
        row.Key,
        row.Description,
        row.LatestWalletId,
        row.LatestDay,
        row.LatestTagIds,
        row.Cells.Select(c => new PlanningCellDto(c.Month.ToString(), c.PlannedCents, c.ItemCount, c.AllSettled, c.Editable, c.EntryId)).ToList());
}

/// <summary>Corpo de <c>PUT /years/{yyyy}/planning/cells</c> (007 research R3).</summary>
public sealed record PlanningCellRequest(string? Kind, string? Description, Guid? CreditCardId, string? Month, string? Value, Guid? WalletId, int? Day);

/// <summary>Corpo de <c>POST /years/{yyyy}/planning/fill</c> (007 research R3).</summary>
public sealed record PlanningFillRequest(
    string? Kind, string? Description, Guid? CreditCardId, string? Amount, IReadOnlyList<string>? Months, Guid? WalletId, int? Day);

/// <summary>Resposta da edição de célula: a linha (<c>PlanningRow</c> ou <c>InvoiceRow</c>) e os totais; <c>deletionBatchId</c> só ao apagar.</summary>
public sealed record PlanningCellResponse(object Row, PlanningTotalsDto Totals, Guid? DeletionBatchId)
{
    public static PlanningCellResponse From(SetPlanningCellResult r) => new(RowOf(r.Outcome), PlanningGridDto.TotalsOf(r.Outcome.Totals), r.DeletionBatchId);

    public static object RowOf(PlanningRowOutcome o) =>
        o.Row is { } row ? PlanningGridDto.From(row) : PlanningGridDto.From(o.InvoiceRow!);
}

public sealed record PlanningFillResponse(int Created, int Skipped, object Row, PlanningTotalsDto Totals)
{
    public static PlanningFillResponse From(FillPlanningMonthsResult r) =>
        new(r.Created, r.Skipped, PlanningCellResponse.RowOf(r.Outcome), PlanningGridDto.TotalsOf(r.Outcome.Totals));
}

/// <summary>Grade do ano (007 US1): <c>/api/v1/years/{yyyy}/planning</c> (leitura, edição de célula e preenchimento).</summary>
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

        group.MapPut("/{year}/planning/cells", async (string year, PlanningCellRequest body, SetPlanningCellHandler handler, CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(
                new SetPlanningCellCommand(year, body.Kind, body.Description, body.CreditCardId, body.Month, body.Value, body.WalletId, body.Day),
                cancellationToken);
            return Results.Ok(PlanningCellResponse.From(result));
        });

        group.MapPost("/{year}/planning/fill", async (string year, PlanningFillRequest body, FillPlanningMonthsHandler handler, CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(
                new FillPlanningMonthsCommand(year, body.Kind, body.Description, body.CreditCardId, body.Amount, body.Months, body.WalletId, body.Day),
                cancellationToken);
            return Results.Ok(PlanningFillResponse.From(result));
        }).WithIdempotency();
    }
}
