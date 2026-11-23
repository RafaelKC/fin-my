using System.Reflection;
using FinMy.Application.Abstractions;
using FinMy.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FinMy.Infrastructure.Persistence;

/// <summary>
/// Restaura um lote de exclusão (research R3) varrendo o modelo do EF: toda entidade <see cref="ISoftDeletable"/>
/// de Espaço entra sozinha, sem lista manual. A leitura mantém o filtro de Espaço (só o de exclusão é ignorado) e a
/// gravação passa pelos interceptors (auditoria); um único <c>SaveChanges</c> torna a restauração tudo ou nada.
/// </summary>
public sealed class DeletionStore(FinMyDbContext db) : IDeletionStore
{
    private static readonly MethodInfo RestoreMethod = typeof(DeletionStore)
        .GetMethod(nameof(RestoreTypeAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    public async Task<RestoreOutcome> RestoreBatchAsync(Guid batchId, DateTimeOffset notBefore, CancellationToken cancellationToken)
    {
        var restored = 0;
        foreach (var clrType in SoftDeletableTenantTypes())
        {
            var task = (Task<int>)RestoreMethod.MakeGenericMethod(clrType).Invoke(null, [db, batchId, notBefore, cancellationToken])!;
            restored += await task;
        }

        if (restored == 0)
        {
            return RestoreOutcome.NotFound;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return RestoreOutcome.Restored;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return RestoreOutcome.Conflict;
        }
    }

    private List<Type> SoftDeletableTenantTypes() => db.Model.GetEntityTypes()
        .Where(t => !t.IsOwned() && t.BaseType is null
            && typeof(ISoftDeletable).IsAssignableFrom(t.ClrType) && typeof(ITenantEntity).IsAssignableFrom(t.ClrType))
        .Select(t => t.ClrType)
        .ToList();

    private static async Task<int> RestoreTypeAsync<T>(FinMyDbContext db, Guid batchId, DateTimeOffset notBefore, CancellationToken cancellationToken)
        where T : class, ISoftDeletable
    {
        var rows = await db.Set<T>()
            .IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])
            .Where(e => EF.Property<Guid?>(e, nameof(ISoftDeletable.DeletionBatchId)) == batchId
                && EF.Property<DateTimeOffset?>(e, nameof(ISoftDeletable.DeletedAt)) >= notBefore)
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            var entry = db.Entry(row);
            entry.Property(nameof(ISoftDeletable.DeletedAt)).CurrentValue = null;
            entry.Property(nameof(ISoftDeletable.DeletedBy)).CurrentValue = null;
            entry.Property(nameof(ISoftDeletable.DeletionBatchId)).CurrentValue = null;
        }

        return rows.Count;
    }
}
