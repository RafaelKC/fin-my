namespace FinMy.Domain.Common;

/// <summary>
/// Tabela de ligação (N:N) de um Espaço, como <c>entry_tags</c>: tem <c>tenant_id</c> e chave composta, mas não
/// tem <c>Id</c>, auditoria, exclusão lógica nem <c>xmin</c> (docs/arquitetura.md §3.2). Ganha o filtro de Espaço.
/// </summary>
public interface ITenantLink : ITenantScoped
{
}
