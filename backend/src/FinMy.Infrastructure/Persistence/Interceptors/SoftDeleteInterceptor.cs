using FinMy.Application.Abstractions;
using FinMy.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FinMy.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Converte a exclusão de <see cref="ISoftDeletable"/> em atualização (docs/arquitetura.md §3.4):
/// grava <c>DeletedAt</c>, <c>DeletedBy</c> e o lote de exclusão do contexto. Dependentes não são apagados.
/// </summary>
public sealed class SoftDeleteInterceptor(IClock clock) : SaveChangesInterceptor
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

    private void Apply(DbContext? dbContext)
    {
        if (dbContext is null)
        {
            return;
        }

        var current = InterceptorContext.Current(dbContext);

        foreach (var entry in dbContext.ChangeTracker.Entries<ISoftDeletable>().Where(e => e.State == EntityState.Deleted).ToList())
        {
            var by = current.UserId ?? throw new MissingUserContextException();

            // Volta para Unchanged e altera só as colunas de exclusão, para o UPDATE não reescrever o resto.
            entry.State = EntityState.Unchanged;
            entry.Property(nameof(ISoftDeletable.DeletedAt)).CurrentValue = clock.UtcNow;
            entry.Property(nameof(ISoftDeletable.DeletedBy)).CurrentValue = by;
            entry.Property(nameof(ISoftDeletable.DeletionBatchId)).CurrentValue = current.DeletionBatchId;
        }
    }
}
