using FinMy.Application.Abstractions;
using FinMy.Application.Tags;
using FinMy.Domain.Cards;
using FinMy.Domain.Common;
using FinMy.Domain.Entries;
using FinMy.Domain.Tags;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FinMy.Infrastructure.Persistence;

/// <summary>
/// Tags do Espaço do contexto. "Em uso" e as movimentações de ligação consideram também lançamentos e compras
/// excluídos e ainda não expurgados (restaurar volta com as tags); <c>uses90d</c> só conta os vivos.
/// </summary>
public sealed class TagStore(FinMyDbContext db) : ITagStore
{
    private const string VersionColumn = "xmin";
    private const int RecentDays = 90;

    private IQueryable<Entry> AllEntries => db.Set<Entry>().IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter]);

    private IQueryable<CardPurchase> AllPurchases => db.Set<CardPurchase>().IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter]);

    public Task<Tag?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<Tag>().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<Guid?> FindIdByNormalizedNameAsync(string normalizedName, Guid? exceptId, CancellationToken cancellationToken) =>
        await db.Set<Tag>().AsNoTracking()
            .Where(t => t.NameNormalized == normalizedName && (exceptId == null || t.Id != exceptId))
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<int> CountAsync(CancellationToken cancellationToken) => db.Set<Tag>().CountAsync(cancellationToken);

    public async Task<TagView?> GetAsync(Guid id, DateOnly today, CancellationToken cancellationToken) =>
        (await ViewsAsync(db.Set<Tag>().Where(t => t.Id == id), today, cancellationToken)).SingleOrDefault();

    public Task<IReadOnlyList<TagView>> ListAsync(TagArchivedFilter filter, DateOnly today, CancellationToken cancellationToken)
    {
        var query = db.Set<Tag>().AsQueryable();
        query = filter switch
        {
            TagArchivedFilter.ActiveOnly => query.Where(t => t.ArchivedAt == null),
            TagArchivedFilter.ArchivedOnly => query.Where(t => t.ArchivedAt != null),
            _ => query,
        };

        return ViewsAsync(query, today, cancellationToken);
    }

    public async Task<bool> IsInUseAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Set<EntryTag>().AnyAsync(l => l.TagId == id, cancellationToken)
        || await db.Set<CardPurchaseTag>().AnyAsync(l => l.TagId == id, cancellationToken)
        || await AllEntries.AnyAsync(e => e.SettlementTagId == id, cancellationToken)
        || await AllPurchases.AnyAsync(p => p.SettlementTagId == id, cancellationToken);

    public async Task<IReadOnlyList<TagConflict>> ConflictsOnMarkAsync(Guid tagId, CancellationToken cancellationToken)
    {
        var entries = await AllEntries.AsNoTracking()
            .Where(e => e.SettlementTagId != null && db.Set<EntryTag>().Any(l => l.EntryId == e.Id && l.TagId == tagId))
            .Select(e => new TagConflict("entry", e.Id, e.Description, e.EntryDate))
            .ToListAsync(cancellationToken);
        var purchases = await AllPurchases.AsNoTracking()
            .Where(p => p.SettlementTagId != null && db.Set<CardPurchaseTag>().Any(l => l.CardPurchaseId == p.Id && l.TagId == tagId))
            .Select(p => new TagConflict("card_purchase", p.Id, p.Description, p.PurchaseDate))
            .ToListAsync(cancellationToken);
        return Ordered(entries, purchases);
    }

    public async Task<IReadOnlyList<TagConflict>> ConflictsOnMergeAsync(Guid sourceId, Guid targetId, CancellationToken cancellationToken)
    {
        // Só a origem comum (em ligação) pode colidir: se a origem é de acerto, ela é a única Tag de acerto do item.
        var entries = await AllEntries.AsNoTracking()
            .Where(e => e.SettlementTagId != null && e.SettlementTagId != targetId
                && db.Set<EntryTag>().Any(l => l.EntryId == e.Id && l.TagId == sourceId))
            .Select(e => new TagConflict("entry", e.Id, e.Description, e.EntryDate))
            .ToListAsync(cancellationToken);
        var purchases = await AllPurchases.AsNoTracking()
            .Where(p => p.SettlementTagId != null && p.SettlementTagId != targetId
                && db.Set<CardPurchaseTag>().Any(l => l.CardPurchaseId == p.Id && l.TagId == sourceId))
            .Select(p => new TagConflict("card_purchase", p.Id, p.Description, p.PurchaseDate))
            .ToListAsync(cancellationToken);
        return Ordered(entries, purchases);
    }

    public async Task MoveLinksToSettlementColumnAsync(Guid tagId, CancellationToken cancellationToken)
    {
        var entryLinks = await db.Set<EntryTag>().Where(l => l.TagId == tagId).ToListAsync(cancellationToken);
        var entryIds = entryLinks.Select(l => l.EntryId).ToList();
        foreach (var entry in await AllEntries.Where(e => entryIds.Contains(e.Id)).ToListAsync(cancellationToken))
        {
            entry.ChangeSettlementTag(tagId);
        }

        db.RemoveRange(entryLinks);

        var purchaseLinks = await db.Set<CardPurchaseTag>().Where(l => l.TagId == tagId).ToListAsync(cancellationToken);
        var purchaseIds = purchaseLinks.Select(l => l.CardPurchaseId).ToList();
        foreach (var purchase in await AllPurchases.Where(p => purchaseIds.Contains(p.Id)).ToListAsync(cancellationToken))
        {
            purchase.ChangeSettlementTag(tagId);
        }

        db.RemoveRange(purchaseLinks);
    }

    public async Task MoveSettlementColumnToLinksAsync(Guid tagId, CancellationToken cancellationToken)
    {
        foreach (var entry in await AllEntries.Where(e => e.SettlementTagId == tagId).ToListAsync(cancellationToken))
        {
            entry.ChangeSettlementTag(null);
            db.Add(new EntryTag(entry.Id, tagId));
        }

        foreach (var purchase in await AllPurchases.Where(p => p.SettlementTagId == tagId).ToListAsync(cancellationToken))
        {
            purchase.ChangeSettlementTag(null);
            db.Add(new CardPurchaseTag(purchase.Id, tagId));
        }
    }

    public async Task ReplaceTagAsync(Guid sourceId, Tag target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        // Entradas/saídas: itens que tinham a origem em ligação ou na coluna.
        var entryLinks = await db.Set<EntryTag>().Where(l => l.TagId == sourceId).ToListAsync(cancellationToken);
        var targetEntryLinks = (await db.Set<EntryTag>().Where(l => l.TagId == target.Id).Select(l => l.EntryId).ToListAsync(cancellationToken)).ToHashSet();
        var entryIds = entryLinks.Select(l => l.EntryId).ToList();
        var entries = await AllEntries.Where(e => entryIds.Contains(e.Id) || e.SettlementTagId == sourceId).ToListAsync(cancellationToken);
        foreach (var entry in entries)
        {
            if (entry.SettlementTagId == sourceId)
            {
                entry.ChangeSettlementTag(target.IsSettlement ? target.Id : null);
            }

            if (target.IsSettlement)
            {
                if (entryIds.Contains(entry.Id))
                {
                    entry.ChangeSettlementTag(target.Id);
                }
            }
            else if (targetEntryLinks.Add(entry.Id))
            {
                db.Add(new EntryTag(entry.Id, target.Id));
            }
        }

        db.RemoveRange(entryLinks);

        var purchaseLinks = await db.Set<CardPurchaseTag>().Where(l => l.TagId == sourceId).ToListAsync(cancellationToken);
        var targetPurchaseLinks = (await db.Set<CardPurchaseTag>().Where(l => l.TagId == target.Id).Select(l => l.CardPurchaseId).ToListAsync(cancellationToken)).ToHashSet();
        var purchaseIds = purchaseLinks.Select(l => l.CardPurchaseId).ToList();
        var purchases = await AllPurchases.Where(p => purchaseIds.Contains(p.Id) || p.SettlementTagId == sourceId).ToListAsync(cancellationToken);
        foreach (var purchase in purchases)
        {
            if (purchase.SettlementTagId == sourceId)
            {
                purchase.ChangeSettlementTag(target.IsSettlement ? target.Id : null);
            }

            if (target.IsSettlement)
            {
                if (purchaseIds.Contains(purchase.Id))
                {
                    purchase.ChangeSettlementTag(target.Id);
                }
            }
            else if (targetPurchaseLinks.Add(purchase.Id))
            {
                db.Add(new CardPurchaseTag(purchase.Id, target.Id));
            }
        }

        db.RemoveRange(purchaseLinks);
    }

    public long VersionOf(Tag tag) => db.Entry(tag).Property<uint>(VersionColumn).CurrentValue;

    public void ExpectVersion(Tag tag, long version) =>
        db.Entry(tag).Property<uint>(VersionColumn).OriginalValue = (uint)version;

    public void Add(Tag tag) => db.Set<Tag>().Add(tag);

    public void Remove(Tag tag) => db.Set<Tag>().Remove(tag);

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException(TagErrors.ConcurrencyConflict);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new DomainException(TagErrors.NameTaken(null));
        }
    }

    private static List<TagConflict> Ordered(List<TagConflict> entries, List<TagConflict> purchases) =>
        entries.Concat(purchases).OrderBy(c => c.Date).ThenBy(c => c.Description, StringComparer.Ordinal).ToList();

    private async Task<IReadOnlyList<TagView>> ViewsAsync(IQueryable<Tag> source, DateOnly today, CancellationToken cancellationToken)
    {
        var rows = await source.AsNoTracking()
            .OrderBy(t => t.NameNormalized)
            .Select(t => new { Tag = t, Version = EF.Property<uint>(t, VersionColumn) })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(r => r.Tag.Id).ToList();
        var since = today.AddDays(-RecentDays);

        // Pares (item, tag) para contar itens distintos por tag: coluna de acerto e ligação.
        var entryUses = await db.Set<Entry>().AsNoTracking()
            .Where(e => e.EntryDate >= since && e.SettlementTagId != null && ids.Contains(e.SettlementTagId.Value))
            .Select(e => new { TagId = e.SettlementTagId!.Value, ItemId = e.Id }).ToListAsync(cancellationToken);
        entryUses.AddRange(await db.Set<EntryTag>().AsNoTracking()
            .Where(l => ids.Contains(l.TagId) && db.Set<Entry>().Any(e => e.Id == l.EntryId && e.EntryDate >= since))
            .Select(l => new { l.TagId, ItemId = l.EntryId }).ToListAsync(cancellationToken));
        var purchaseUses = await db.Set<CardPurchase>().AsNoTracking()
            .Where(p => p.PurchaseDate >= since && p.SettlementTagId != null && ids.Contains(p.SettlementTagId.Value))
            .Select(p => new { TagId = p.SettlementTagId!.Value, ItemId = p.Id }).ToListAsync(cancellationToken);
        purchaseUses.AddRange(await db.Set<CardPurchaseTag>().AsNoTracking()
            .Where(l => ids.Contains(l.TagId) && db.Set<CardPurchase>().Any(p => p.Id == l.CardPurchaseId && p.PurchaseDate >= since))
            .Select(l => new { l.TagId, ItemId = l.CardPurchaseId }).ToListAsync(cancellationToken));

        var inUse = (await db.Set<EntryTag>().AsNoTracking().Where(l => ids.Contains(l.TagId)).Select(l => l.TagId).Distinct().ToListAsync(cancellationToken)).ToHashSet();
        inUse.UnionWith(await db.Set<CardPurchaseTag>().AsNoTracking().Where(l => ids.Contains(l.TagId)).Select(l => l.TagId).Distinct().ToListAsync(cancellationToken));
        inUse.UnionWith(await AllEntries.AsNoTracking().Where(e => e.SettlementTagId != null && ids.Contains(e.SettlementTagId.Value)).Select(e => e.SettlementTagId!.Value).Distinct().ToListAsync(cancellationToken));
        inUse.UnionWith(await AllPurchases.AsNoTracking().Where(p => p.SettlementTagId != null && ids.Contains(p.SettlementTagId.Value)).Select(p => p.SettlementTagId!.Value).Distinct().ToListAsync(cancellationToken));

        return rows.Select(r => new TagView(
            r.Tag.Id, r.Tag.Name, r.Tag.Color, r.Tag.IsSettlement, r.Tag.IsArchived,
            entryUses.Where(u => u.TagId == r.Tag.Id).Select(u => u.ItemId).Distinct().Count()
                + purchaseUses.Where(u => u.TagId == r.Tag.Id).Select(u => u.ItemId).Distinct().Count(),
            inUse.Contains(r.Tag.Id), r.Version)).ToList();
    }
}
