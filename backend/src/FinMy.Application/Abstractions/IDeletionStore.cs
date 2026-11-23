namespace FinMy.Application.Abstractions;

/// <summary>Resultado de <see cref="IDeletionStore.RestoreBatchAsync"/>.</summary>
public enum RestoreOutcome
{
    /// <summary>Ao menos uma linha do lote foi restaurada.</summary>
    Restored,

    /// <summary>Nenhuma linha do Espaço no lote dentro do prazo (inexistente, de outro Espaço, já restaurado ou expirado).</summary>
    NotFound,

    /// <summary>A restauração violaria um índice único (ex.: carteira recriada com o mesmo nome); nada foi restaurado.</summary>
    Conflict,
}

/// <summary>Restauração de exclusões lógicas (research R3), sobre todas as entidades <c>ISoftDeletable</c> do Espaço do contexto.</summary>
public interface IDeletionStore
{
    /// <summary>
    /// Restaura, tudo ou nada, as linhas do Espaço do contexto excluídas no lote com <c>deleted_at</c> ≥ <paramref name="notBefore"/>.
    /// </summary>
    Task<RestoreOutcome> RestoreBatchAsync(Guid batchId, DateTimeOffset notBefore, CancellationToken cancellationToken);
}
