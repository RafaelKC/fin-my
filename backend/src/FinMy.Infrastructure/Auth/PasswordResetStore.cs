using FinMy.Application.Abstractions;
using FinMy.Domain.Identity;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Infrastructure.Auth;

/// <summary>Grava e consome os tokens de redefinição de senha (<c>password_reset_tokens</c>).</summary>
public sealed class PasswordResetStore(FinMyDbContext db) : IPasswordResetStore
{
    public async Task<PasswordResetRecipient?> CreateAsync(
        string emailNormalized, byte[] tokenHash, DateTimeOffset now, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        var user = await db.Set<User>()
            .AsNoTracking()
            .Where(u => u.EmailNormalized == emailNormalized)
            .Select(u => new PasswordResetRecipient(u.Id, u.Name, u.Email))
            .SingleOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            return null;
        }

        db.Add(new PasswordResetToken(user.UserId, tokenHash, now, expiresAt));
        await db.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task<Guid?> RedeemAsync(byte[] tokenHash, string newPasswordHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var token = await db.Set<PasswordResetToken>()
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        if (token is null)
        {
            return null;
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            // Uso único mesmo com duas requisições simultâneas: só uma consegue marcar o token.
            var claimed = await db.Set<PasswordResetToken>()
                .Where(t => t.Id == token.Id && t.UsedAt == null && t.ExpiresAt > now)
                .ExecuteUpdateAsync(set => set.SetProperty(t => t.UsedAt, now), cancellationToken);

            if (claimed == 0)
            {
                return (Guid?)null;
            }

            var user = await db.Set<User>().SingleAsync(u => u.Id == token.UserId, cancellationToken);
            user.ChangePassword(newPasswordHash, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return user.Id;
        });
    }
}
