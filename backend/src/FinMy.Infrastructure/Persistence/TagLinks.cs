using FinMy.Domain.Tags;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Infrastructure.Persistence;

/// <summary>Leitura das tags de lançamentos e compras (coluna de acerto + ligações) para as respostas (004 research R1).</summary>
internal static class TagLinks
{
    /// <summary>Tags comuns de cada lançamento (ligações), na ordem dos ids de tag.</summary>
    public static async Task<Dictionary<Guid, List<Guid>>> ForEntriesAsync(FinMyDbContext db, IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken)
    {
        if (entryIds.Count == 0)
        {
            return [];
        }

        var rows = await db.Set<EntryTag>().AsNoTracking()
            .Where(l => entryIds.Contains(l.EntryId))
            .Select(l => new { Owner = l.EntryId, l.TagId })
            .ToListAsync(cancellationToken);
        return Group(rows.Select(r => (r.Owner, r.TagId)));
    }

    /// <summary>Tags comuns de cada compra (ligações), na ordem dos ids de tag.</summary>
    public static async Task<Dictionary<Guid, List<Guid>>> ForPurchasesAsync(FinMyDbContext db, IReadOnlyCollection<Guid> purchaseIds, CancellationToken cancellationToken)
    {
        if (purchaseIds.Count == 0)
        {
            return [];
        }

        var rows = await db.Set<CardPurchaseTag>().AsNoTracking()
            .Where(l => purchaseIds.Contains(l.CardPurchaseId))
            .Select(l => new { Owner = l.CardPurchaseId, l.TagId })
            .ToListAsync(cancellationToken);
        return Group(rows.Select(r => (r.Owner, r.TagId)));
    }

    /// <summary>Todas as tags do item: a de acerto primeiro, depois as comuns.</summary>
    public static IReadOnlyList<Guid> All(Guid? settlementTagId, IReadOnlyDictionary<Guid, List<Guid>> links, Guid ownerId)
    {
        var result = new List<Guid>();
        if (settlementTagId is { } settlement)
        {
            result.Add(settlement);
        }

        if (links.TryGetValue(ownerId, out var common))
        {
            result.AddRange(common);
        }

        return result;
    }

    public static IReadOnlyList<Guid> All(Guid? settlementTagId, IEnumerable<Guid> common) =>
        settlementTagId is { } settlement ? [settlement, .. common.Order()] : [.. common.Order()];

    private static Dictionary<Guid, List<Guid>> Group(IEnumerable<(Guid Owner, Guid TagId)> rows) =>
        rows.GroupBy(r => r.Owner).ToDictionary(g => g.Key, g => g.Select(r => r.TagId).Order().ToList());
}
