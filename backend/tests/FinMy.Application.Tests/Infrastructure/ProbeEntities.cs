using FinMy.Domain.Common;

namespace FinMy.Application.Tests.Infrastructure;

/// <summary>Sonda "pai" (research R1): entidade de Espaço sem significado de negócio.</summary>
public sealed class ProbeParent : TenantEntity
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>Sonda "filha": referencia <see cref="ProbeParent"/> por FK composta (tenant_id, parent_id).</summary>
public sealed class ProbeItem : TenantEntity
{
    public Guid ParentId { get; set; }

    public string Label { get; set; } = string.Empty;
}
