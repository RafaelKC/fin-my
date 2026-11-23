using FinMy.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FinMy.Api.Composition;

/// <summary>Verifica se o PostgreSQL responde. Falhas não expõem detalhes na resposta.</summary>
public sealed partial class DatabaseHealthCheck(FinMyDbContext db, ILogger<DatabaseHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogDatabaseFailure(logger, exception);
            return HealthCheckResult.Unhealthy();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falha ao verificar o banco de dados")]
    private static partial void LogDatabaseFailure(ILogger logger, Exception exception);
}
