namespace FinMy.Application.Abstractions;

/// <summary>Resultado de <see cref="IIdempotencyStore.BeginAsync"/>.</summary>
public enum IdempotencyOutcome
{
    /// <summary>Chave nova: a requisição pode executar (a linha "em andamento" já foi gravada).</summary>
    Started,

    /// <summary>Mesma chave e mesma requisição já concluída: devolver a resposta guardada.</summary>
    Replay,

    /// <summary>Mesma chave com requisição diferente.</summary>
    Reused,

    /// <summary>Mesma chave e mesma requisição ainda em execução (corrida).</summary>
    InProgress,
}

/// <summary>Resposta guardada, preenchida só em <see cref="IdempotencyOutcome.Replay"/>.</summary>
public sealed record IdempotencyBegin(IdempotencyOutcome Outcome, int StatusCode = 0, string? ResponseBody = null);

/// <summary>Guarda de <c>Idempotency-Key</c> por Espaço (research R2, tabela <c>idempotency_keys</c>).</summary>
public interface IIdempotencyStore
{
    /// <summary>Tenta reservar a chave; se ela já existe, diz o que fazer com a requisição repetida.</summary>
    Task<IdempotencyBegin> BeginAsync(Guid key, byte[] requestHash, CancellationToken cancellationToken);

    /// <summary>Grava a resposta 2xx da requisição que reservou a chave.</summary>
    Task CompleteAsync(Guid key, int statusCode, string? responseBody, CancellationToken cancellationToken);

    /// <summary>Libera a chave (resposta que não é 2xx ou falha), para que o cliente possa tentar de novo.</summary>
    Task AbandonAsync(Guid key, CancellationToken cancellationToken);
}
