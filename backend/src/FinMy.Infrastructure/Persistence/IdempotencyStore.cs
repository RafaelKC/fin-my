using System.Security.Cryptography;
using FinMy.Application.Abstractions;
using FinMy.Domain.Idempotency;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Infrastructure.Persistence;

/// <summary>
/// Implementa <see cref="IIdempotencyStore"/> sem tocar no rastreamento do contexto da requisição: a reserva é um
/// <c>INSERT ... ON CONFLICT DO NOTHING</c> (a PK <c>(tenant_id, key)</c> decide a corrida) e as demais gravações são
/// <c>ExecuteUpdate</c>/<c>ExecuteDelete</c>, que respeitam o filtro de Espaço.
/// </summary>
public sealed class IdempotencyStore(FinMyDbContext db, IClock clock) : IIdempotencyStore
{
    public async Task<IdempotencyBegin> BeginAsync(Guid key, byte[] requestHash, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requestHash);

        var tenantId = db.CurrentContext.TenantId ?? throw new InvalidOperationException("Idempotência exige um Espaço no contexto.");
        var userId = db.CurrentContext.UserId;
        var now = clock.UtcNow;

        var inserted = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO idempotency_keys (tenant_id, key, user_id, request_hash, status_code, created_at)
            VALUES ({tenantId}, {key}, {userId}, {requestHash}, {IdempotencyKey.InProgress}, {now})
            ON CONFLICT (tenant_id, key) DO NOTHING
            """,
            cancellationToken);

        if (inserted == 1)
        {
            return new IdempotencyBegin(IdempotencyOutcome.Started);
        }

        var existing = await db.Set<IdempotencyKey>().AsNoTracking()
            .Where(k => k.Key == key)
            .Select(k => new { k.RequestHash, k.StatusCode, k.ResponseBody })
            .SingleOrDefaultAsync(cancellationToken);

        if (existing is null)
        {
            // A linha foi liberada entre o INSERT e a leitura: o cliente simplesmente repete.
            return new IdempotencyBegin(IdempotencyOutcome.InProgress);
        }

        if (!CryptographicOperations.FixedTimeEquals(existing.RequestHash, requestHash))
        {
            return new IdempotencyBegin(IdempotencyOutcome.Reused);
        }

        return existing.StatusCode == IdempotencyKey.InProgress
            ? new IdempotencyBegin(IdempotencyOutcome.InProgress)
            : new IdempotencyBegin(IdempotencyOutcome.Replay, existing.StatusCode, existing.ResponseBody);
    }

    public Task CompleteAsync(Guid key, int statusCode, string? responseBody, CancellationToken cancellationToken)
        => db.Set<IdempotencyKey>().Where(k => k.Key == key)
            .ExecuteUpdateAsync(
                s => s.SetProperty(k => k.StatusCode, statusCode).SetProperty(k => k.ResponseBody, responseBody),
                cancellationToken);

    public Task AbandonAsync(Guid key, CancellationToken cancellationToken)
        => db.Set<IdempotencyKey>().Where(k => k.Key == key).ExecuteDeleteAsync(cancellationToken);
}
