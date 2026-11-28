using FinMy.Application.Abstractions;
using FinMy.Domain.Suggestions;
using FinMy.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Infrastructure.Persistence;

public sealed class SuggestionStore(FinMyDbContext db) : ISuggestionStore
{
    public Task<Suggestion?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<Suggestion>().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<Suggestion?> FindByDedupeKeyAsync(string dedupeKey, CancellationToken cancellationToken) =>
        db.Set<Suggestion>().SingleOrDefaultAsync(s => s.DedupeKey == dedupeKey, cancellationToken);

    public async Task<IReadOnlyList<Suggestion>> ListPendingAsync(CancellationToken cancellationToken) =>
        await db.Set<Suggestion>().AsNoTracking()
            .Where(s => s.Status == SuggestionStatus.Pending)
            .OrderByDescending(s => s.OccurredAt)
            .Take(200)
            .ToListAsync(cancellationToken);

    public void Add(Suggestion suggestion) => db.Set<Suggestion>().Add(suggestion);

    public Task SaveAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
