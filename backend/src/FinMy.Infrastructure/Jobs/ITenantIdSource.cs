namespace FinMy.Infrastructure.Jobs;

/// <summary>Lista os Espaços existentes para os jobs (a tabela <c>tenants</c> é global, sem filtro de Espaço).</summary>
public interface ITenantIdSource
{
    Task<IReadOnlyList<Guid>> ListTenantIdsAsync(CancellationToken cancellationToken);
}
