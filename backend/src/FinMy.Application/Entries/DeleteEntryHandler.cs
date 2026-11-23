using FinMy.Application.Abstractions;
using FinMy.Domain.Common;

namespace FinMy.Application.Entries;

/// <summary>Exclui (lógico) um lançamento num lote próprio, que a pessoa pode desfazer por <c>/deletions/{batchId}/restore</c> (RT-09).</summary>
public sealed class DeleteEntryHandler(IEntryStore store, ICurrentContext context)
{
    public async Task<Guid> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var batchId = context.BeginDeletionBatch();
        if (!await store.DeleteAsync(id, cancellationToken))
        {
            throw new DomainException(EntryErrors.NotFound);
        }

        return batchId;
    }
}
