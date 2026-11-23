using System.Security.Cryptography;
using System.Text;
using FinMy.Application.Abstractions;
using FinMy.Domain.Identity;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Infrastructure.Auth;

/// <summary>
/// Refresh tokens opacos de 256 bits; o banco guarda só o SHA-256. Validade deslizante de 90 dias (001 FR-009),
/// rotação a cada uso e revogação da sessão inteira quando um token já trocado reaparece (docs/arquitetura.md §7).
/// </summary>
public sealed class RefreshTokenStore(FinMyDbContext db, IClock clock, ISessionRegistry sessions) : IRefreshTokenStore
{
    /// <summary>Validade desde o último uso; também o <c>Max-Age</c> do cookie.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(90);

    private const int TokenBytes = 32;

    private DbSet<RefreshToken> Tokens => db.Set<RefreshToken>();

    private const int DeviceLabelMaxLength = 120;

    /// <summary>Gera 256 bits aleatórios em base64url (valor que vai no cookie).</summary>
    public static string GenerateToken() => Base64Url(RandomNumberGenerator.GetBytes(TokenBytes));

    /// <summary>SHA-256 do token, como gravado em <c>refresh_tokens.token_hash</c>.</summary>
    public static byte[] Hash(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }

    public async Task<IssuedRefreshToken> IssueAsync(Guid userId, Guid? sessionId, string? deviceLabel, CancellationToken cancellationToken)
    {
        var (entity, token) = Create(userId, sessionId ?? Guid.CreateVersion7(), Truncate(deviceLabel));
        Tokens.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return new IssuedRefreshToken(token, entity.FamilyId, entity.ExpiresAt);
    }

    public async Task<RefreshRotation> RotateAsync(string token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        var now = clock.UtcNow;
        var hash = Hash(token);
        var current = await Tokens.AsNoTracking().SingleOrDefaultAsync(r => r.TokenHash == hash, cancellationToken);

        if (current is null)
        {
            return new RefreshRotation(RefreshRotationStatus.Invalid);
        }

        if (current.RevokedAt is not null)
        {
            return await RejectRevokedAsync(current, cancellationToken);
        }

        if (current.ExpiresAt <= now)
        {
            return new RefreshRotation(RefreshRotationStatus.Expired, current.UserId);
        }

        var (next, nextToken) = Create(current.UserId, current.FamilyId, current.DeviceLabel);

        // Troca atômica: só uma de duas requisições simultâneas com o mesmo token consegue revogá-lo.
        var strategy = db.Database.CreateExecutionStrategy();
        var rotated = await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var revoked = await Tokens
                .Where(r => r.Id == current.Id && r.RevokedAt == null)
                .ExecuteUpdateAsync(
                    set => set
                        .SetProperty(r => r.RevokedAt, now)
                        .SetProperty(r => r.RevokedReason, RefreshTokenRevocationReason.Rotated)
                        .SetProperty(r => r.ReplacedById, next.Id),
                    cancellationToken);

            if (revoked == 0)
            {
                return false;
            }

            Tokens.Add(next);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        });

        if (!rotated)
        {
            // Perdeu a corrida: o token acabou de ser usado por outra requisição, o que conta como reuso.
            await RevokeSessionAsync(current.FamilyId, RefreshTokenRevocationReason.ReuseDetected, cancellationToken);
            return new RefreshRotation(RefreshRotationStatus.ReuseDetected, current.UserId);
        }

        return new RefreshRotation(RefreshRotationStatus.Rotated, current.UserId, new IssuedRefreshToken(nextToken, next.FamilyId, next.ExpiresAt));
    }

    public async Task RevokeSessionAsync(Guid sessionId, RefreshTokenRevocationReason reason, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        await Tokens
            .Where(r => r.FamilyId == sessionId && r.RevokedAt == null)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(r => r.RevokedAt, now)
                    .SetProperty(r => r.RevokedReason, reason),
                cancellationToken);

        // Depois de gravar: a próxima requisição com esse `sid` já enxerga a sessão revogada.
        sessions.Forget(sessionId);
    }

    public async Task RevokeUserSessionsAsync(Guid userId, RefreshTokenRevocationReason reason, CancellationToken cancellationToken, Guid? exceptSessionId = null)
    {
        var sessionIds = await Tokens
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.RevokedAt == null && r.FamilyId != exceptSessionId)
            .Select(r => r.FamilyId)
            .Distinct()
            .ToListAsync(cancellationToken);

        // Pela revogação de sessão única, que também esquece o cache de cada `sid`.
        foreach (var sessionId in sessionIds)
        {
            await RevokeSessionAsync(sessionId, reason, cancellationToken);
        }
    }

    private async Task<RefreshRotation> RejectRevokedAsync(RefreshToken current, CancellationToken cancellationToken)
    {
        if (current.RevokedReason != RefreshTokenRevocationReason.Rotated)
        {
            return new RefreshRotation(RefreshRotationStatus.Revoked, current.UserId);
        }

        // Token já trocado reapresentado: alguém guardou uma cópia. Derruba a sessão inteira.
        await RevokeSessionAsync(current.FamilyId, RefreshTokenRevocationReason.ReuseDetected, cancellationToken);
        return new RefreshRotation(RefreshRotationStatus.ReuseDetected, current.UserId);
    }

    private (RefreshToken Entity, string Token) Create(Guid userId, Guid familyId, string? deviceLabel)
    {
        var now = clock.UtcNow;
        var token = GenerateToken();
        return (new RefreshToken(userId, familyId, Hash(token), deviceLabel, now, now.Add(Lifetime)), token);
    }

    private static string? Truncate(string? label)
        => string.IsNullOrWhiteSpace(label) ? null : label.Trim()[..Math.Min(label.Trim().Length, DeviceLabelMaxLength)];

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
