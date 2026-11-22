using FinMy.Domain.Common;

namespace FinMy.Domain.Identity;

/// <summary>Token de redefinição de senha (<c>password_reset_tokens</c>): só o hash é guardado; uso único.</summary>
public sealed class PasswordResetToken : IEntity
{
    // Construtor para o EF Core.
    private PasswordResetToken()
    {
    }

    public PasswordResetToken(Guid userId, byte[] tokenHash, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAt = now;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public byte[] TokenHash { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }
}
