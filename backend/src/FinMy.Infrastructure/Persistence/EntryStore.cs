using FinMy.Application.Abstractions;
using FinMy.Application.Entries;
using FinMy.Domain.Cards;
using FinMy.Domain.Common;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;
using FinMy.Domain.Tags;
using FinMy.Domain.Wallets;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Infrastructure.Persistence;

/// <summary>Entradas e Saídas (<c>entries</c>); <c>version</c> é o <c>xmin</c> da linha (research R11).</summary>
public sealed class EntryStore(FinMyDbContext db) : IEntryStore
{
    private const string VersionColumn = "xmin";

    public async Task<WalletRef?> GetWalletAsync(Guid walletId, CancellationToken cancellationToken) =>
        await db.Set<Wallet>().AsNoTracking()
            .Where(w => w.Id == walletId)
            .Select(w => new WalletRef(w.Id, w.ArchivedAt != null))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<EntryView?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await db.Set<Entry>().AsNoTracking()
            .Where(e => e.Id == id)
            .Select(e => new { Entry = e, Version = EF.Property<uint>(e, VersionColumn) })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var links = await TagLinks.ForEntriesAsync(db, [id], cancellationToken);
        var counts = await SplitCounts.ForEntriesAsync(db, [row.Entry.SplitGroupId], cancellationToken);
        return ToView(row.Entry, row.Version, TagLinks.All(row.Entry.SettlementTagId, links, id), SplitCounts.Of(row.Entry.SplitGroupId, counts));
    }

    public async Task<EntryView> AddAsync(Entry entry, IReadOnlyCollection<Guid> commonTagIds, CancellationToken cancellationToken)
    {
        db.Set<Entry>().Add(entry);
        db.AddRange(commonTagIds.Select(t => new EntryTag(entry.Id, t)));
        await db.SaveChangesAsync(cancellationToken);
        return ToView(entry, VersionOf(entry), TagLinks.All(entry.SettlementTagId, commonTagIds), 0);
    }

    public async Task<IReadOnlyList<EntryView>> AddManyAsync(
        IReadOnlyList<(Entry Entry, IReadOnlyCollection<Guid> CommonTagIds)> items, CancellationToken cancellationToken)
    {
        foreach (var (entry, commonTagIds) in items)
        {
            db.Set<Entry>().Add(entry);
            db.AddRange(commonTagIds.Select(t => new EntryTag(entry.Id, t)));
        }

        await db.SaveChangesAsync(cancellationToken);
        return items.Select(i => ToView(i.Entry, VersionOf(i.Entry), TagLinks.All(i.Entry.SettlementTagId, i.CommonTagIds), items.Count)).ToList();
    }

    public async Task<EntryView?> UpdateAsync(
        Guid id, long? expectedVersion, Action<Entry> change, IReadOnlyCollection<Guid>? commonTagIds, CancellationToken cancellationToken)
    {
        var entry = await db.Set<Entry>().SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (entry is null)
        {
            return null;
        }

        if (expectedVersion is { } expected)
        {
            // Conferir já na leitura pega a versão velha mesmo quando nada mudou (o EF não emitiria UPDATE);
            // o valor original cobre a corrida entre a leitura e a gravação.
            if (expected != VersionOf(entry))
            {
                throw new DomainException(EntryErrors.ConcurrencyConflict);
            }

            db.Entry(entry).Property<uint>(VersionColumn).OriginalValue = (uint)expected;
        }

        change(entry);

        var current = (await TagLinks.ForEntriesAsync(db, [id], cancellationToken)).GetValueOrDefault(id) ?? [];
        if (commonTagIds is not null)
        {
            var wanted = commonTagIds.ToHashSet();
            db.RemoveRange(await db.Set<EntryTag>().Where(l => l.EntryId == id && !wanted.Contains(l.TagId)).ToListAsync(cancellationToken));
            db.AddRange(wanted.Except(current).Select(t => new EntryTag(id, t)));
            current = [.. wanted];
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException(EntryErrors.ConcurrencyConflict);
        }

        var counts = await SplitCounts.ForEntriesAsync(db, [entry.SplitGroupId], cancellationToken);
        return ToView(entry, VersionOf(entry), TagLinks.All(entry.SettlementTagId, current), SplitCounts.Of(entry.SplitGroupId, counts));
    }

    public async Task<IReadOnlyList<EntryView>> ListPendingAsync(YearMonth currentMonth, CancellationToken cancellationToken)
    {
        var firstDay = currentMonth.FirstDay;
        var rows = await db.Set<Entry>().AsNoTracking()
            .Where(e => e.Status == EntryStatus.Planned && e.ReferenceMonth < firstDay)
            .OrderBy(e => e.EntryDate).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .Select(e => new { Entry = e, Version = EF.Property<uint>(e, VersionColumn) })
            .ToListAsync(cancellationToken);

        var links = await TagLinks.ForEntriesAsync(db, rows.Select(r => r.Entry.Id).ToList(), cancellationToken);
        var counts = await SplitCounts.ForEntriesAsync(db, rows.Select(r => r.Entry.SplitGroupId), cancellationToken);
        return rows.ConvertAll(r => ToView(r.Entry, r.Version, TagLinks.All(r.Entry.SettlementTagId, links, r.Entry.Id), SplitCounts.Of(r.Entry.SplitGroupId, counts)));
    }

    public async Task<bool> UpdateManyAsync(IReadOnlyCollection<Guid> ids, Action<Entry> change, CancellationToken cancellationToken)
    {
        var entries = await LoadAllAsync(ids, cancellationToken);
        if (entries is null)
        {
            return false;
        }

        foreach (var entry in entries)
        {
            change(entry);
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var entries = await LoadAllAsync(ids, cancellationToken);
        if (entries is null)
        {
            return false;
        }

        db.RemoveRange(entries);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Carrega os lançamentos do Espaço com esses ids; <c>null</c> se algum não existir.</summary>
    private async Task<List<Entry>?> LoadAllAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var entries = await db.Set<Entry>().Where(e => ids.Contains(e.Id)).ToListAsync(cancellationToken);
        return entries.Count == ids.Count ? entries : null;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var entry = await db.Set<Entry>().SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (entry is null)
        {
            return false;
        }

        db.Remove(entry);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<EntrySuggestion>> SuggestAsync(
        string normalizedText, EntryKind? kind, DateOnly notBefore, int limit, CancellationToken cancellationToken)
    {
        var pattern = $"%{EscapeLike(normalizedText)}%";
        var entries = db.Set<Entry>().AsNoTracking()
            .Where(e => e.EntryDate >= notBefore && EF.Functions.Like(e.DescriptionNormalized, pattern, "\\"));
        var purchases = db.Set<CardPurchase>().AsNoTracking()
            .Where(p => p.PurchaseDate >= notBefore && EF.Functions.Like(p.DescriptionNormalized, pattern, "\\"));

        var entryGroups = await entries
            .GroupBy(e => e.DescriptionNormalized)
            .Select(g => new Usage(g.Key, g.Count(), g.Max(e => e.EntryDate), g.Max(e => e.CreatedAt)))
            .ToListAsync(cancellationToken);

        // Um parcelamento é um uso só, não um por parcela (research R9).
        var purchaseGroups = await purchases
            .GroupBy(p => p.DescriptionNormalized)
            .Select(g => new Usage(
                g.Key, g.Select(p => p.InstallmentPlanId ?? p.Id).Distinct().Count(), g.Max(p => p.PurchaseDate), g.Max(p => p.CreatedAt)))
            .ToListAsync(cancellationToken);

        var groups = entryGroups.Concat(purchaseGroups)
            .GroupBy(u => u.Key)
            .Select(g => new Usage(g.Key, g.Sum(u => u.Uses), g.Max(u => u.LastDate), g.Max(u => u.LastCreated)))
            .OrderByDescending(g => g.Uses)
            .ThenByDescending(g => g.LastDate)
            .ThenByDescending(g => g.LastCreated)
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Take(limit)
            .ToList();

        var result = new List<EntrySuggestion>(groups.Count);
        foreach (var group in groups)
        {
            var sameEntries = entries.Where(e => e.DescriptionNormalized == group.Key);
            var samePurchases = purchases.Where(p => p.DescriptionNormalized == group.Key);

            var latestEntry = await sameEntries
                .OrderByDescending(e => e.EntryDate).ThenByDescending(e => e.CreatedAt)
                .Select(e => new Latest(e.Description, e.EntryDate, e.CreatedAt))
                .FirstOrDefaultAsync(cancellationToken);
            var latestPurchase = await samePurchases
                .OrderByDescending(p => p.PurchaseDate).ThenByDescending(p => p.CreatedAt)
                .Select(p => new Latest(p.Description, p.PurchaseDate, p.CreatedAt))
                .FirstOrDefaultAsync(cancellationToken);

            var latest = (latestEntry, latestPurchase) switch
            {
                (null, var p) => p!,
                (var e, null) => e,
                var (e, p) => (e.Date, e.Created).CompareTo((p!.Date, p.Created)) >= 0 ? e : p,
            };

            Guid? walletId = null;
            Guid? creditCardId = null;
            IReadOnlyList<Guid> tagIds = [];
            if (kind is { } entryKind)
            {
                var latestOfKind = await sameEntries
                    .Where(e => e.Kind == entryKind)
                    .OrderByDescending(e => e.EntryDate).ThenByDescending(e => e.CreatedAt)
                    .Select(e => new { e.Id, e.WalletId, e.SettlementTagId })
                    .FirstOrDefaultAsync(cancellationToken);
                walletId = latestOfKind?.WalletId;

                if (walletId is { } id
                    && !await db.Set<Wallet>().AsNoTracking().AnyAsync(w => w.Id == id && w.ArchivedAt == null, cancellationToken))
                {
                    walletId = null;
                }

                if (latestOfKind is not null)
                {
                    var links = await TagLinks.ForEntriesAsync(db, [latestOfKind.Id], cancellationToken);
                    tagIds = await ActiveTagIdsAsync(TagLinks.All(latestOfKind.SettlementTagId, links, latestOfKind.Id), cancellationToken);
                }
            }
            else
            {
                var latestPurchaseRow = await samePurchases
                    .OrderByDescending(p => p.PurchaseDate).ThenByDescending(p => p.CreatedAt)
                    .Select(p => new { p.Id, p.CreditCardId, p.SettlementTagId })
                    .FirstOrDefaultAsync(cancellationToken);
                creditCardId = latestPurchaseRow?.CreditCardId;

                if (creditCardId is { } cardId
                    && !await db.Set<CreditCard>().AsNoTracking().AnyAsync(c => c.Id == cardId && c.ArchivedAt == null, cancellationToken))
                {
                    creditCardId = null;
                }

                if (latestPurchaseRow is not null)
                {
                    var links = await TagLinks.ForPurchasesAsync(db, [latestPurchaseRow.Id], cancellationToken);
                    tagIds = await ActiveTagIdsAsync(TagLinks.All(latestPurchaseRow.SettlementTagId, links, latestPurchaseRow.Id), cancellationToken);
                }
            }

            result.Add(new EntrySuggestion(latest.Description, group.Uses, walletId, creditCardId, tagIds));
        }

        return result;
    }

    /// <summary>Mantém só as tags ativas (tag arquivada não é sugerida), na ordem recebida.</summary>
    private async Task<IReadOnlyList<Guid>> ActiveTagIdsAsync(IReadOnlyList<Guid> tagIds, CancellationToken cancellationToken)
    {
        if (tagIds.Count == 0)
        {
            return tagIds;
        }

        var active = (await db.Set<Tag>().AsNoTracking().Where(t => tagIds.Contains(t.Id) && t.ArchivedAt == null).Select(t => t.Id).ToListAsync(cancellationToken)).ToHashSet();
        return tagIds.Where(active.Contains).ToList();
    }

    private sealed record Usage(string Key, int Uses, DateOnly LastDate, DateTimeOffset LastCreated);

    private sealed record Latest(string Description, DateOnly Date, DateTimeOffset Created);

    private uint VersionOf(Entry entry) => db.Entry(entry).Property<uint>(VersionColumn).CurrentValue;

    private static EntryView ToView(Entry e, uint version, IReadOnlyList<Guid> tagIds, int splitCount) => new(
        e.Id,
        e.Kind,
        e.Description,
        e.AmountCents,
        e.EntryDate,
        e.ReferenceYearMonth,
        e.Status,
        e.WalletId,
        e.Notes,
        e.CreatedAt,
        e.CreatedVia,
        version,
        tagIds,
        e.SettlementTagId,
        e.SplitGroupId,
        splitCount);

    internal static string EscapeLike(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}
