using FinMy.Application.Abstractions;
using FinMy.Domain.Common;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Entries;

/// <summary>Pendências (FR-019, research R9): Previstas com Mês de referência anterior ao mês de hoje, do Espaço todo, na ordem RT-12.</summary>
public sealed class ListPendingEntriesHandler(IEntryStore store, IClock clock)
{
    public async Task<IReadOnlyList<EntryView>> HandleAsync(CancellationToken cancellationToken) =>
        await store.ListPendingAsync(YearMonth.From(clock.Today), cancellationToken);
}

/// <summary>Resultado de <see cref="BulkEntriesHandler"/>; <see cref="DeletionBatchId"/> só existe na exclusão.</summary>
public sealed record BulkEntriesResult(int Affected, Guid? DeletionBatchId);

/// <summary>
/// Ação em várias entradas/saídas de uma vez (FR-020, research R9), tudo ou nada: <c>settle</c>, <c>move_to_current_month</c>
/// ou <c>delete</c> (um único lote de exclusão, desfazível). Id inexistente → 404 e nada muda. Sem exigir versão (R11).
/// </summary>
public sealed class BulkEntriesHandler(IEntryStore store, IClock clock, ICurrentContext context)
{
    public const int MaxIds = 200;

    public async Task<BulkEntriesResult> HandleAsync(IReadOnlyList<Guid>? ids, string? action, CancellationToken cancellationToken)
    {
        var distinct = ValidateIds(ids);
        var parsed = action switch
        {
            "settle" or "move_to_current_month" or "delete" => action,
            _ => throw new DomainException(EntryErrors.Field("action.invalid", "action", "Escolha uma ação válida")),
        };

        Guid? batchId = null;
        bool found;
        switch (parsed)
        {
            case "settle":
                found = await store.UpdateManyAsync(distinct, e => e.ChangeStatus(EntryStatus.Settled), cancellationToken);
                break;
            case "move_to_current_month":
                var current = YearMonth.From(clock.Today);
                found = await store.UpdateManyAsync(distinct, e => e.MoveToMonth(current), cancellationToken);
                break;
            default:
                batchId = context.BeginDeletionBatch();
                found = await store.DeleteManyAsync(distinct, cancellationToken);
                break;
        }

        return found ? new BulkEntriesResult(distinct.Count, batchId) : throw new DomainException(EntryErrors.NotFound);
    }

    private static List<Guid> ValidateIds(IReadOnlyList<Guid>? ids)
    {
        if (ids is null || ids.Count == 0)
        {
            throw new DomainException(EntryErrors.Field("ids.required", "ids", "Escolha ao menos um lançamento"));
        }

        if (ids.Count > MaxIds)
        {
            throw new DomainException(EntryErrors.Field("ids.too_many", "ids", $"Escolha no máximo {MaxIds} lançamentos por vez"));
        }

        return [.. ids.Distinct()];
    }
}
