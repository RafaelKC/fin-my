using FinMy.Domain.Common;

namespace FinMy.Domain.Identity;

/// <summary>Refresh token opaco de uma sessão (<c>refresh_tokens</c>); só o hash SHA-256 é guardado.</summary>
public sealed class RefreshToken : IEntity
{
    // Construtor para o EF Core.
    private RefreshToken()
    {
    }

    public RefreshToken(
        Guid userId,
        Guid familyId,
        byte[] tokenHash,
        string? deviceLabel,
        DateTimeOffset now,
        DateTimeOffset expiresAt)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        FamilyId = familyId;
        TokenHash = tokenHash;
        DeviceLabel = deviceLabel;
        CreatedAt = now;
        LastUsedAt = now;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>Identifica a sessão (aparelho); vai no access token como <c>sid</c>.</summary>
    public Guid FamilyId { get; private set; }

    public byte[] TokenHash { get; private set; } = [];

    public string? DeviceLabel { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset LastUsedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public RefreshTokenRevocationReason? RevokedReason { get; private set; }

    public Guid? ReplacedById { get; private set; }

    /// <summary>Ativo = não revogado e ainda dentro da validade.</summary>
    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    /// <summary>Marca este token como trocado por <paramref name="replacedById"/> (rotação).</summary>
    public void MarkRotated(Guid replacedById, DateTimeOffset now)
    {
        Revoke(RefreshTokenRevocationReason.Rotated, now);
        ReplacedById = replacedById;
    }

    /// <summary>Encerra o token; não altera um token que já foi revogado antes.</summary>
    public void Revoke(RefreshTokenRevocationReason reason, DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        RevokedReason = reason;
    }
}
