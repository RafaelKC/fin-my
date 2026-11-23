namespace FinMy.Domain.Identity;

/// <summary>
/// Participação de um usuário num Espaço (<c>tenant_members</c>), chave composta (tenant, usuário).
/// Na v1, <c>UNIQUE (user_id)</c> e <c>UNIQUE (tenant_id)</c> limitam a um Espaço por usuário e um participante por Espaço.
/// </summary>
public sealed class TenantMember
{
    // Construtor para o EF Core.
    private TenantMember()
    {
    }

    public TenantMember(Guid tenantId, Guid userId, DateTimeOffset joinedAt)
    {
        TenantId = tenantId;
        UserId = userId;
        Role = TenantMemberRole.Owner;
        JoinedAt = joinedAt;
    }

    public Guid TenantId { get; private set; }

    public Guid UserId { get; private set; }

    public TenantMemberRole Role { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }
}
