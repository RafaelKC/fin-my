using FinMy.Application.Abstractions;
using FinMy.Domain.Common;
using FinMy.Domain.Identity;
using FinMy.Domain.Wallets;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Infrastructure.Persistence;

/// <summary>Estado do guia nas colunas <c>onboarding_*</c> de <c>tenants</c> (tabela global, lida só pelo Espaço do contexto).</summary>
public sealed class OnboardingStore(FinMyDbContext db, ICurrentContext context) : IOnboardingStore
{
    public async Task<Tenant?> FindTenantAsync(CancellationToken cancellationToken)
    {
        var tenantId = context.TenantId ?? throw new MissingTenantContextException();
        return await db.Set<Tenant>().SingleOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
    }

    public Task<bool> HasWalletsAsync(CancellationToken cancellationToken) =>
        db.Set<Wallet>().AsNoTracking().AnyAsync(cancellationToken);

    public Task SaveAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
