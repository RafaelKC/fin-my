using System.Globalization;
using FinMy.Application.Abstractions;
using FinMy.Domain.Common;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace FinMy.Infrastructure.Jobs;

/// <summary>
/// Job diário (docs/arquitetura.md §8): remove definitivamente as linhas com <c>deleted_at</c> há mais de 30 dias em
/// todas as tabelas <see cref="ISoftDeletable"/> do modelo (varredura do EF, sem lista manual), uma passada por Espaço.
/// Uma linha ainda referenciada por outra (a exclusão lógica não cascateia) fica para a próxima passada, depois que
/// quem a referencia for expurgado; a execução é idempotente.
/// </summary>
public sealed class DeletionPurgeJob(ITenantJobRunner runner, IClock clock)
{
    public const string JobId = "deletion-purge";

    /// <summary>Tempo que uma linha excluída ainda é mantida (e pode ser restaurada) antes do expurgo.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    // Cada passada só apaga o que já não é referenciado; a profundidade da cadeia de FKs é pequena.
    private const int MaxPasses = 10;

    public Task RunAsync(CancellationToken cancellationToken = default)
        => runner.ForEachTenantAsync(PurgeAsync, cancellationToken);

    /// <summary>Informa a recorrência (diária, 03:30 de Brasília) a quem agenda, sem depender do Hangfire.</summary>
    public static void Describe(Action<string, string, TimeZoneInfo?> register)
        => register(JobId, "30 3 * * *", TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));

    private async Task PurgeAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<FinMyDbContext>();
        var limit = clock.UtcNow - Retention;
        var tenantId = db.TenantIdOrEmpty;
        var statements = db.Model.GetEntityTypes()
            .Where(t => !t.IsOwned() && t.BaseType is null && typeof(ISoftDeletable).IsAssignableFrom(t.ClrType))
            .Select(BuildDelete)
            .OfType<string>()
            .ToList();

        for (var pass = 0; pass < MaxPasses; pass++)
        {
            var purged = 0;
            foreach (var sql in statements)
            {
#pragma warning disable EF1002 // Só identificadores vindos do modelo do EF; os valores seguem como parâmetros.
                purged += await db.Database.ExecuteSqlRawAsync(sql, [tenantId, limit], cancellationToken);
#pragma warning restore EF1002
            }

            if (purged == 0)
            {
                return;
            }
        }
    }

    // DELETE ... WHERE [tenant_id = {0} AND] deleted_at < {1} AND NOT EXISTS (linha que ainda a referencia)
    private static string? BuildDelete(IEntityType entityType)
    {
        if (StoreObjectIdentifier.Create(entityType, StoreObjectType.Table) is not { } table)
        {
            return null;
        }

        var deletedAt = Column(entityType.FindProperty(nameof(ISoftDeletable.DeletedAt))!, table);
        var where = new List<string>();
        if (typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType))
        {
            where.Add($"t.{Column(entityType.FindProperty(nameof(ITenantEntity.TenantId))!, table)} = {{0}}");
        }

        where.Add($"t.{deletedAt} < {{1}}");

        foreach (var fk in entityType.GetReferencingForeignKeys())
        {
            var dependent = StoreObjectIdentifier.Create(fk.DeclaringEntityType, StoreObjectType.Table);
            if (dependent is not { } dep)
            {
                continue;
            }

            var join = string.Join(" AND ", fk.Properties.Zip(
                fk.PrincipalKey.Properties,
                (child, parent) => $"c.{Column(child, dep)} = t.{Column(parent, table)}"));
            where.Add($"NOT EXISTS (SELECT 1 FROM {Quote(dep)} c WHERE {join})");
        }

        return string.Create(CultureInfo.InvariantCulture, $"DELETE FROM {Quote(table)} t WHERE {string.Join(" AND ", where)}");
    }

    private static string Column(IProperty property, StoreObjectIdentifier table) => $"\"{property.GetColumnName(table)}\"";

    private static string Quote(StoreObjectIdentifier table)
        => table.Schema is null ? $"\"{table.Name}\"" : $"\"{table.Schema}\".\"{table.Name}\"";
}
