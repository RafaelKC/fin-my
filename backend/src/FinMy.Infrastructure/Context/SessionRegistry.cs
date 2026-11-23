using FinMy.Application.Abstractions;
using FinMy.Domain.Identity;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FinMy.Infrastructure.Context;

/// <summary>
/// <see cref="ISessionRegistry"/> sobre <c>refresh_tokens</c> com <see cref="IMemoryCache"/>. Só sessões ativas
/// ficam em cache (por 5 min); sessão revogada nunca volta a valer, então consulta o banco sempre.
/// </summary>
public sealed class SessionRegistry(FinMyDbContext db, IClock clock, IMemoryCache cache) : ISessionRegistry
{
    /// <summary>Tempo máximo que uma sessão ativa fica em cache sem nova consulta ao banco.</summary>
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public async Task<bool> IsActiveAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(Key(sessionId), out _))
        {
            return true;
        }

        var now = clock.UtcNow;
        var active = await db.Set<RefreshToken>()
            .AsNoTracking()
            .AnyAsync(r => r.FamilyId == sessionId && r.RevokedAt == null && r.ExpiresAt > now, cancellationToken);

        if (active)
        {
            cache.Set(Key(sessionId), true, CacheDuration);
        }

        return active;
    }

    public void Forget(Guid sessionId) => cache.Remove(Key(sessionId));

    private static string Key(Guid sessionId) => $"session-active:{sessionId:N}";
}
