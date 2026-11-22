namespace FinMy.Domain.Common;

/// <summary>Entidade que pertence a um Espaço (tenant).</summary>
public interface ITenantEntity : IEntity
{
    Guid TenantId { get; }
}
