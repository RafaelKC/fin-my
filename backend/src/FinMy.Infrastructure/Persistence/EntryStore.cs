using FinMy.Application.Abstractions;
using FinMy.Application.Entries;
using FinMy.Domain.Common;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;
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

        return row is null ? null : ToView(row.Entry, row.Version);
    }

    public async Task<EntryView> AddAsync(Entry entry, CancellationToken cancellationToken)
    {
        db.Set<Entry>().Add(entry);
        await db.SaveChangesAsync(cancellationToken);
        return ToView(entry, VersionOf(entry));
    }

    public async Task<EntryView?> UpdateAsync(Guid id, long? expectedVersion, Action<Entry> change, CancellationToken cancellationToken)
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

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException(EntryErrors.ConcurrencyConflict);
        }

        return ToView(entry, VersionOf(entry));
    }

    public async Task<IReadOnlyList<EntrySuggestion>> SuggestAsync(
        string normalizedText, EntryKind kind, DateOnly notBefore, int limit, CancellationToken cancellationToken)
    {
        var pattern = $"%{EscapeLike(normalizedText)}%";
        var universe = db.Set<Entry>().AsNoTracking()
            .Where(e => e.EntryDate >= notBefore && EF.Functions.Like(e.DescriptionNormalized, pattern, "\\"));

        var groups = await universe
            .GroupBy(e => e.DescriptionNormalized)
            .Select(g => new { Key = g.Key, Uses = g.Count(), LastDate = g.Max(e => e.EntryDate), LastCreated = g.Max(e => e.CreatedAt) })
            .OrderByDescending(g => g.Uses)
            .ThenByDescending(g => g.LastDate)
            .ThenByDescending(g => g.LastCreated)
            .ThenBy(g => g.Key)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var result = new List<EntrySuggestion>(groups.Count);
        foreach (var group in groups)
        {
            var same = universe.Where(e => e.DescriptionNormalized == group.Key);

            var latest = await same
                .OrderByDescending(e => e.EntryDate).ThenByDescending(e => e.CreatedAt)
                .Select(e => e.Description)
                .FirstAsync(cancellationToken);

            var walletId = await same
                .Where(e => e.Kind == kind)
                .OrderByDescending(e => e.EntryDate).ThenByDescending(e => e.CreatedAt)
                .Select(e => (Guid?)e.WalletId)
                .FirstOrDefaultAsync(cancellationToken);

            if (walletId is { } id
                && !await db.Set<Wallet>().AsNoTracking().AnyAsync(w => w.Id == id && w.ArchivedAt == null, cancellationToken))
            {
                walletId = null;
            }

            result.Add(new EntrySuggestion(latest, group.Uses, walletId));
        }

        return result;
    }

    private uint VersionOf(Entry entry) => db.Entry(entry).Property<uint>(VersionColumn).CurrentValue;

    private static EntryView ToView(Entry e, uint version) => new(
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
        version);

    private static string EscapeLike(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}
