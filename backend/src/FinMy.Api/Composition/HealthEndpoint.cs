using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FinMy.Api.Composition;

public static class HealthEndpoint
{
    public const string Path = "/health";

    /// <summary><c>GET /health</c>: 200 com servidor e banco saudáveis, 503 caso o banco não responda.</summary>
    public static IEndpointRouteBuilder MapFinMyHealth(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks(Path, new HealthCheckOptions { ResponseWriter = WriteAsync, AllowCachingResponses = false });
        return app;
    }

    private static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var body = new
        {
            status = Name(report.Status),
            server = "healthy",
            database = Name(report.Entries.TryGetValue("database", out var entry) ? entry.Status : HealthStatus.Unhealthy),
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonSerializerOptions.Web), context.RequestAborted);
    }

    private static string Name(HealthStatus status) => status == HealthStatus.Healthy ? "healthy" : "unhealthy";
}
