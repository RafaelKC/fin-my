using FinMy.Application.Abstractions;
using FinMy.Domain.Idempotency;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinMy.Infrastructure.Jobs;

/// <summary>
/// Job diário (docs/arquitetura.md §8, research R2): apaga as linhas de <c>idempotency_keys</c> criadas há mais de
/// 24 h, uma passada por Espaço (o filtro de Espaço vale dentro do escopo de sistema). Idempotente.
/// </summary>
public sealed class IdempotencyPurgeJob(ITenantJobRunner runner, IClock clock)
{
    public const string JobId = "idempotency-purge";

    /// <summary>Janela em que a mesma chave ainda devolve a resposta guardada.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    public Task RunAsync(CancellationToken cancellationToken = default)
        => runner.ForEachTenantAsync(PurgeAsync, cancellationToken);

    /// <summary>Informa a recorrência (diária, 03:15 de Brasília) a quem agenda, sem depender do Hangfire.</summary>
    public static void Describe(Action<string, string, TimeZoneInfo?> register)
        => register(JobId, "15 3 * * *", TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));

    private async Task PurgeAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<FinMyDbContext>();
        var limit = clock.UtcNow - Retention;

        await db.Set<IdempotencyKey>().Where(k => k.CreatedAt < limit).ExecuteDeleteAsync(cancellationToken);
    }
}
