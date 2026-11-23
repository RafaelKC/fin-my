using System.Threading.RateLimiting;
using FinMy.Api.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace FinMy.Api.Composition;

/// <summary>Limites por IP (research R5): 20 req/min, janela fixa, política <c>auth</c> para <c>/api/v1/auth/*</c>.</summary>
public sealed class AuthRateLimitOptions
{
    public const string SectionName = "RateLimiting:Auth";

    public int PermitLimit { get; set; } = 20;

    public int WindowSeconds { get; set; } = 60;
}

public static class RateLimitPolicies
{
    /// <summary>Nome da política a aplicar nos endpoints de autenticação (<c>.RequireRateLimiting(RateLimitPolicies.Auth)</c>).</summary>
    public const string Auth = "auth";
}

public static class RateLimitingSetup
{
    public static IServiceCollection AddFinMyRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AuthRateLimitOptions>(configuration.GetSection(AuthRateLimitOptions.SectionName));

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = WriteRejectionAsync;
        });

        // As opções são lidas no momento do uso (a configuração dos testes já está completa).
        services.AddOptions<RateLimiterOptions>().Configure<Microsoft.Extensions.Options.IOptions<AuthRateLimitOptions>>((limiter, auth) =>
            limiter.AddPolicy(RateLimitPolicies.Auth, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = auth.Value.PermitLimit,
                    Window = TimeSpan.FromSeconds(auth.Value.WindowSeconds),
                    QueueLimit = 0,
                })));

        return services;
    }

    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        var problem = ProblemFactory.ForStatus(StatusCodes.Status429TooManyRequests);

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            var seconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
            http.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            problem.Extensions["retryAfterSeconds"] = seconds;
        }

        ProblemFactory.AddTraceId(problem, http);
        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await http.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken);
    }
}
