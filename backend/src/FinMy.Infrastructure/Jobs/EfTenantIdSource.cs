using FinMy.Domain.Identity;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Infrastructure.Jobs;

/// <summary>Lê os Espaços da tabela global <c>tenants</c> (sem filtro de Espaço, por isso fica em <c>Jobs</c>).</summary>
public sealed class EfTenantIdSource(FinMyDbContext db) : ITenantIdSource
{
    public async Task<IReadOnlyList<Guid>> ListTenantIdsAsync(CancellationToken cancellationToken)
        => await db.Set<Tenant>().AsNoTracking().OrderBy(t => t.Id).Select(t => t.Id).ToListAsync(cancellationToken);
}
