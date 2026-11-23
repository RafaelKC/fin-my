using FinMy.Application.Abstractions;
using FinMy.Domain.Common;

namespace FinMy.Application.Deletions;

/// <summary>Desfaz uma exclusão (RT-09, research R3): restaura o lote até 10 minutos depois de excluído.</summary>
public sealed class RestoreDeletionHandler(IDeletionStore store, IClock clock)
{
    /// <summary>Janela em que o servidor aceita restaurar (folga sobre os 8 s da interface).</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    public async Task HandleAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var outcome = await store.RestoreBatchAsync(batchId, clock.UtcNow - Window, cancellationToken);

        if (outcome == RestoreOutcome.NotFound)
        {
            throw new DomainException(DeletionErrors.NotFound);
        }

        if (outcome == RestoreOutcome.Conflict)
        {
            throw new DomainException(DeletionErrors.RestoreConflict);
        }
    }
}

/// <summary>Erros estáveis de <c>/deletions</c> (contracts/openapi.yaml).</summary>
public static class DeletionErrors
{
    public static readonly DomainError NotFound = new(
        "deletion.not_found", "Não é mais possível desfazer esta exclusão.", DomainErrorKind.NotFound);

    public static readonly DomainError RestoreConflict = new(
        "deletion.restore_conflict", "Não foi possível desfazer: já existe um item com os mesmos dados.", DomainErrorKind.Conflict);
}
