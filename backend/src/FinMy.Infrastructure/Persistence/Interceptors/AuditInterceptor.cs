using FinMy.Application.Abstractions;
using FinMy.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FinMy.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Preenche a auditoria (docs/arquitetura.md §3.4): <c>Created*</c> e <c>Updated*</c> nas novas,
/// <c>Updated*</c> nas alteradas, sem nunca tocar em <c>Created*</c> de linha existente.
/// O horário vem de <see cref="IClock"/>; o autor, do contexto (ou o usuário de sistema).
/// </summary>
public sealed class AuditInterceptor(IClock clock) : SaveChangesInterceptor
{
    private static readonly string[] CreatedProperties =
    [
        nameof(IAudited.CreatedAt), nameof(IAudited.CreatedBy), nameof(IAudited.CreatedVia),
    ];

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

    private static void Set(EntityEntry entry, string name, object value) => entry.Property(name).CurrentValue = value;

    private void Apply(DbContext? dbContext)
    {
        if (dbContext is null)
        {
            return;
        }

        var current = InterceptorContext.Current(dbContext);
        dbContext.ChangeTracker.DetectChanges();

        foreach (var entry in dbContext.ChangeTracker.Entries<IAudited>().ToList())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            var now = clock.UtcNow;
            var by = current.UserId ?? (current.IsSystem ? SystemUser.Id : throw new MissingUserContextException());

            if (entry.State == EntityState.Added)
            {
                Set(entry, nameof(IAudited.CreatedAt), now);
                Set(entry, nameof(IAudited.CreatedBy), by);
                Set(entry, nameof(IAudited.CreatedVia), current.Channel);
            }
            else
            {
                foreach (var name in CreatedProperties)
                {
                    entry.Property(name).IsModified = false;
                }
            }

            Set(entry, nameof(IAudited.UpdatedAt), now);
            Set(entry, nameof(IAudited.UpdatedBy), by);
            Set(entry, nameof(IAudited.UpdatedVia), current.Channel);
        }
    }
}
