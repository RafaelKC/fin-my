using FinMy.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FinMy.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Isola a escrita por Espaço (docs/arquitetura.md §3.4): atribui o <c>TenantId</c> do contexto às
/// entidades novas e recusa gravar sem Espaço, trocar o Espaço ou alterar/excluir dado de outro Espaço.
/// </summary>
public sealed class TenantInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Apply(DbContext? dbContext)
    {
        if (dbContext is null)
        {
            return;
        }

        var current = InterceptorContext.Current(dbContext);
        dbContext.ChangeTracker.DetectChanges();

        foreach (var entry in dbContext.ChangeTracker.Entries<ITenantEntity>().ToList())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    ApplyToAdded(entry, current.TenantId);
                    break;
                case EntityState.Modified:
                case EntityState.Deleted:
                    EnsureSameTenant(entry, current.TenantId);
                    break;
            }
        }
    }

    private static void ApplyToAdded(EntityEntry<ITenantEntity> entry, Guid? contextTenant)
    {
        var tenantId = contextTenant ?? throw new MissingTenantContextException();
        var property = entry.Property(nameof(ITenantEntity.TenantId));
        var informed = (Guid)property.CurrentValue!;

        if (informed != Guid.Empty && informed != tenantId)
        {
            throw new CrossTenantWriteException(
                $"{entry.Metadata.ClrType.Name} informa o Espaço {informed}, diferente do Espaço do contexto.");
        }

        property.CurrentValue = tenantId;
    }

    private static void EnsureSameTenant(EntityEntry<ITenantEntity> entry, Guid? contextTenant)
    {
        var tenantId = contextTenant ?? throw new MissingTenantContextException();
        var property = entry.Property(nameof(ITenantEntity.TenantId));

        if ((Guid)property.OriginalValue! != (Guid)property.CurrentValue!)
        {
            throw new CrossTenantWriteException($"{entry.Metadata.ClrType.Name}: o Espaço de uma entidade não pode ser alterado.");
        }

        if ((Guid)property.CurrentValue! != tenantId)
        {
            throw new CrossTenantWriteException($"{entry.Metadata.ClrType.Name} pertence a outro Espaço.");
        }
    }
}
