namespace FinMy.Domain.Common;

/// <summary>
/// Linha com <c>tenant_id</c> que ganha o filtro de Espaço sem ser <see cref="ITenantEntity"/> (sem auditoria,
/// exclusão lógica nem <c>xmin</c>), como <c>idempotency_keys</c>. A gravação informa o Espaço explicitamente.
/// </summary>
public interface ITenantScoped
{
    Guid TenantId { get; }
}
