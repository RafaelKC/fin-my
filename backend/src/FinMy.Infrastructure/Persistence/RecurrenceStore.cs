using FinMy.Application.Abstractions;
using FinMy.Domain.Cards;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;
using FinMy.Domain.Recurrences;
using FinMy.Domain.Tags;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FinMy.Infrastructure.Persistence;

/// <summary>Recorrências do Espaço do contexto; <c>version</c> é o <c>xmin</c> da linha.</summary>
public sealed class RecurrenceStore(FinMyDbContext db) : IRecurrenceStore
{
    private const string VersionColumn = "xmin";

    public Task<Recurrence?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<Recurrence>().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Recurrence>> ListAsync(CancellationToken cancellationToken) =>
        await db.Set<Recurrence>().OrderBy(r => r.DescriptionNormalized).ThenBy(r => r.CreatedAt).ThenBy(r => r.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Recurrence>> ListPendingGenerationAsync(YearMonth horizon, CancellationToken cancellationToken)
    {
        var last = horizon.FirstDay;
        return await db.Set<Recurrence>()
            .Where(r => r.GeneratedUntilMonth == null
                || (r.GeneratedUntilMonth < last && (r.EndMonth == null || r.GeneratedUntilMonth < r.EndMonth)))
            .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetTagIdsAsync(
        IReadOnlyCollection<Recurrence> recurrences, CancellationToken cancellationToken)
    {
        var ids = recurrences.Select(r => r.Id).ToList();
        var links = ids.Count == 0
            ? []
            : await db.Set<RecurrenceTag>().AsNoTracking()
                .Where(l => ids.Contains(l.RecurrenceId))
                .Select(l => new { l.RecurrenceId, l.TagId })
                .ToListAsync(cancellationToken);

        return recurrences.ToDictionary(
            r => r.Id,
            r => (IReadOnlyList<Guid>)TagLinks.All(r.SettlementTagId, links.Where(l => l.RecurrenceId == r.Id).Select(l => l.TagId)));
    }

    public async Task<IReadOnlyDictionary<Guid, DateOnly>> GetNextOccurrenceDatesAsync(
        IReadOnlyCollection<Guid> ids, DateOnly today, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, DateOnly>();
        }

        var entries = await db.Set<Entry>().AsNoTracking()
            .Where(e => e.RecurrenceId != null && ids.Contains(e.RecurrenceId.Value) && e.EntryDate >= today)
            .GroupBy(e => e.RecurrenceId!.Value)
            .Select(g => new { Id = g.Key, Date = g.Min(e => e.EntryDate) })
            .ToListAsync(cancellationToken);
        var purchases = await db.Set<CardPurchase>().AsNoTracking()
            .Where(p => p.RecurrenceId != null && ids.Contains(p.RecurrenceId.Value) && p.PurchaseDate >= today)
            .GroupBy(p => p.RecurrenceId!.Value)
            .Select(g => new { Id = g.Key, Date = g.Min(p => p.PurchaseDate) })
            .ToListAsync(cancellationToken);

        return entries.Concat(purchases)
            .GroupBy(r => r.Id)
            .ToDictionary(g => g.Key, g => g.Min(r => r.Date));
    }

    public long VersionOf(Recurrence recurrence) => db.Entry(recurrence).Property<uint>(VersionColumn).CurrentValue;

    public void Add(Recurrence recurrence, IReadOnlyCollection<Guid> commonTagIds)
    {
        db.Set<Recurrence>().Add(recurrence);
        db.AddRange(commonTagIds.Select(t => new RecurrenceTag(recurrence.Id, t)));
    }

    public void AddOccurrence(Entry entry, IReadOnlyCollection<Guid> commonTagIds)
    {
        db.Set<Entry>().Add(entry);
        db.AddRange(commonTagIds.Select(t => new EntryTag(entry.Id, t)));
    }

    public async Task SaveAsync(CancellationToken cancellationToken) => await db.SaveChangesAsync(cancellationToken);

    public async Task<ICardTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        new RecurrenceTransaction(await db.Database.BeginTransactionAsync(cancellationToken));

    private sealed class RecurrenceTransaction(IDbContextTransaction transaction) : ICardTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
