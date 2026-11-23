using FinMy.Application.Abstractions;
using FinMy.Domain.Auth;
using FinMy.Domain.Identity;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Infrastructure.Auth;

/// <summary>Contador em <c>login_throttles</c>; a falha é aplicada sob <c>SELECT ... FOR UPDATE</c> (research R5).</summary>
public sealed class LoginThrottleStore(FinMyDbContext db) : ILoginThrottleStore
{
    public async Task<LoginThrottleState?> GetAsync(string emailNormalized, CancellationToken cancellationToken)
    {
        var row = await db.Set<LoginThrottle>().AsNoTracking()
            .Where(t => t.EmailNormalized == emailNormalized)
            .FirstOrDefaultAsync(cancellationToken);

        return row?.ToState();
    }

    public async Task<LoginThrottleState> RegisterFailureAsync(string emailNormalized, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            // Garante a linha (corrida entre duas primeiras falhas) e a trava para a atualização.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO login_throttles (email_normalized, failed_count, window_started_at, locked_until)
                VALUES ({emailNormalized}, 0, {now}, NULL)
                ON CONFLICT (email_normalized) DO NOTHING
                """,
                cancellationToken);

            var row = await db.Set<LoginThrottle>()
                .FromSqlInterpolated($"SELECT * FROM login_throttles WHERE email_normalized = {emailNormalized} FOR UPDATE")
                .SingleAsync(cancellationToken);

            var next = LoginThrottleRule.RegisterFailure(row.ToState(), now);
            row.Apply(next);

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return next;
        });
    }

    public async Task ResetAsync(string emailNormalized, CancellationToken cancellationToken)
        => await db.Set<LoginThrottle>()
            .Where(t => t.EmailNormalized == emailNormalized)
            .ExecuteDeleteAsync(cancellationToken);
}
