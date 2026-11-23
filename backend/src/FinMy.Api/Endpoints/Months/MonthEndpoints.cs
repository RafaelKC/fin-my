using FinMy.Application.Months;

namespace FinMy.Api.Endpoints.Months;

/// <summary>Visão do mês (US3): <c>GET /api/v1/months/{month}</c> (contracts/openapi.yaml).</summary>
public sealed class MonthEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/v1/months").WithTags("months").RequireAuthorization();

        group.MapGet("/{month}", async (string month, string? walletIds, GetMonthHandler handler, HttpContext http, CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(month, walletIds, cancellationToken);
            http.Response.Headers.CacheControl = "no-store";
            return Results.Ok(MonthViewDto.From(result));
        });
    }
}
