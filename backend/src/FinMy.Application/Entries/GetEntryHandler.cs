using FinMy.Application.Abstractions;
using FinMy.Domain.Common;

namespace FinMy.Application.Entries;

public sealed class GetEntryHandler(IEntryStore store)
{
    public async Task<EntryView> HandleAsync(Guid id, CancellationToken cancellationToken) =>
        await store.GetAsync(id, cancellationToken) ?? throw new DomainException(EntryErrors.NotFound);
}
