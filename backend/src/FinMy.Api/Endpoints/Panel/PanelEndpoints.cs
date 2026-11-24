using FinMy.Api.Endpoints.Months;
using FinMy.Application.Months;
using FinMy.Application.Panel;
using FinMy.Domain.Months;

namespace FinMy.Api.Endpoints.Panel;

/// <summary>Totais do ano numa leitura (<c>totals.planned</c> / <c>totals.actual</c>).</summary>
public sealed record YearTotalsDto(long IncomeCents, long ExpenseCents, long ResultCents, long FinalBalanceCents);

public sealed record PanelTotalsDto(YearTotalsDto Planned, YearTotalsDto Actual);

public sealed record PanelMonthDto(string Month, SummaryColumnDto Planned, SummaryColumnDto Actual);

/// <summary>Resposta de <c>GET /years/{yyyy}/panel</c> (006 contracts/api.md).</summary>
public sealed record YearPanelDto(int Year, DateOnly Today, IReadOnlyList<PanelMonthDto> Months, PanelTotalsDto Totals)
{
    public static YearPanelDto From(YearPanelResult r) => new(
        r.Year,
        r.Today,
        r.Months.Select(m => new PanelMonthDto(m.Month.ToString(), From(m.Summary.Planned), From(m.Summary.Actual))).ToList(),
        new PanelTotalsDto(From(r.Planned), From(r.Actual)));

    private static YearTotalsDto From(YearTotals t) => new(t.IncomeCents, t.ExpenseCents, t.ResultCents, t.FinalBalanceCents);

    private static SummaryColumnDto From(SummaryColumn c) => new(
        c.OpeningBalanceCents,
        c.WalletsInitialBalanceCents,
        c.IncomeCents,
        c.ExpenseCents,
        c.ResultCents,
        c.TransfersInCents,
        c.TransfersOutCents,
        c.FinalBalanceCents);
}

/// <summary>Painel do ano (006 US1): <c>GET /api/v1/years/{yyyy}/panel</c>.</summary>
public sealed class PanelEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/v1/years").WithTags("panel").RequireAuthorization();

        group.MapGet("/{year}/panel", async (string year, GetYearPanelHandler handler, HttpContext http, CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(year, cancellationToken);
            http.Response.Headers.CacheControl = "no-store";
            return Results.Ok(YearPanelDto.From(result));
        });
    }
}
