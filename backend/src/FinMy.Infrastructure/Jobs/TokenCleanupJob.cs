using FinMy.Application.Abstractions;
using FinMy.Domain.Identity;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinMy.Infrastructure.Jobs;

/// <summary>
/// Job diário (docs/arquitetura.md §8): remove refresh tokens e tokens de redefinição de senha vencidos há mais de
/// 24 h. Roda por Espaço via <see cref="ITenantJobRunner"/>; as tabelas de tokens são globais, então a limpeza é
/// idempotente (a primeira passada apaga, as seguintes não encontram mais nada).
/// </summary>
public sealed class TokenCleanupJob(ITenantJobRunner runner, IClock clock)
{
    public const string JobId = "token-cleanup";

    /// <summary>Tempo que um token vencido ainda é mantido antes de ser apagado.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    public Task RunAsync(CancellationToken cancellationToken = default)
        => runner.ForEachTenantAsync(CleanAsync, cancellationToken);

    /// <summary>Informa a recorrência (diária, 03:00 de Brasília) a quem agenda, sem depender do Hangfire.</summary>
    public static void Describe(Action<string, string, TimeZoneInfo?> register)
        => register(JobId, "0 3 * * *", TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));

    private async Task CleanAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<FinMyDbContext>();
        var limit = clock.UtcNow - Retention;

        await db.Set<RefreshToken>().Where(t => t.ExpiresAt < limit).ExecuteDeleteAsync(cancellationToken);
        await db.Set<PasswordResetToken>().Where(t => t.ExpiresAt < limit).ExecuteDeleteAsync(cancellationToken);
    }
}
