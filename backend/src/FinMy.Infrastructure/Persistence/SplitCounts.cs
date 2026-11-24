using FinMy.Domain.Cards;
using FinMy.Domain.Entries;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Infrastructure.Persistence;

/// <summary>"Dividida em N" (004 FR-014): quantas partes ainda existem em cada grupo de divisão.</summary>
internal static class SplitCounts
{
    /// <summary>Partes de cada grupo de Saídas (excluídas ficam de fora pelo filtro padrão).</summary>
    public static async Task<Dictionary<Guid, int>> ForEntriesAsync(FinMyDbContext db, IEnumerable<Guid?> groupIds, CancellationToken cancellationToken)
    {
        var ids = Distinct(groupIds);
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.Set<Entry>().AsNoTracking()
            .Where(e => e.SplitGroupId != null && ids.Contains(e.SplitGroupId.Value))
            .GroupBy(e => e.SplitGroupId!.Value)
            .Select(g => new { Group = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Group, x => x.Count, cancellationToken);
    }

    /// <summary>Partes de cada grupo de compras; um parcelamento conta uma vez, não uma por parcela.</summary>
    public static async Task<Dictionary<Guid, int>> ForPurchasesAsync(FinMyDbContext db, IEnumerable<Guid?> groupIds, CancellationToken cancellationToken)
    {
        var ids = Distinct(groupIds);
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.Set<CardPurchase>().AsNoTracking()
            .Where(p => p.SplitGroupId != null && ids.Contains(p.SplitGroupId.Value))
            .GroupBy(p => p.SplitGroupId!.Value)
            .Select(g => new { Group = g.Key, Count = g.Select(p => p.InstallmentPlanId ?? p.Id).Distinct().Count() })
            .ToDictionaryAsync(x => x.Group, x => x.Count, cancellationToken);
    }

    /// <summary>Contagem do grupo do item; 0 quando o item não foi dividido.</summary>
    public static int Of(Guid? groupId, IReadOnlyDictionary<Guid, int> counts) =>
        groupId is { } id ? counts.GetValueOrDefault(id) : 0;

    private static List<Guid> Distinct(IEnumerable<Guid?> groupIds) =>
        groupIds.Where(g => g is not null).Select(g => g!.Value).Distinct().ToList();
}
