using FinMy.Application.Abstractions;
using FinMy.Domain.Common;

namespace FinMy.Application.Entries;

/// <summary>Troca a situação com um toque (FR-006); sem versão: a última escrita vence (research R11).</summary>
public sealed class ChangeEntryStatusHandler(IEntryStore store)
{
    public async Task<EntryView> HandleAsync(Guid id, string? status, CancellationToken cancellationToken)
    {
        var parsed = EntryInput.ParseStatus(status);
        return await store.UpdateAsync(id, null, entry => entry.ChangeStatus(parsed), null, cancellationToken)
            ?? throw new DomainException(EntryErrors.NotFound);
    }
}
