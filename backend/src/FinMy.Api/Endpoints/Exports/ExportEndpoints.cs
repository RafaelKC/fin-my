using FinMy.Application.Exports;

namespace FinMy.Api.Endpoints.Exports;

/// <summary>Exportação de lançamentos (006 US4): <c>GET /api/v1/exports/entries.csv</c>.</summary>
public sealed class ExportEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/v1/exports/entries.csv", async (string? from, string? to, ExportEntriesCsvHandler handler, HttpContext http, CancellationToken cancellationToken) =>
        {
            var file = await handler.HandleAsync(from, to, cancellationToken);
            http.Response.Headers.CacheControl = "no-store";
            return Results.File(file.Content, "text/csv; charset=utf-8", file.FileName);
        }).WithTags("exports").RequireAuthorization();
    }
}
