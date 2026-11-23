using FinMy.Domain.Common;

namespace FinMy.Api.Idempotency;

/// <summary>Erros do cabeçalho <c>Idempotency-Key</c> (contracts/openapi.yaml, research R2).</summary>
public static class IdempotencyErrors
{
    public static readonly DomainError InvalidKey = new(
        "idempotency.invalid_key",
        "O cabeçalho Idempotency-Key deve ser um UUID válido.",
        DomainErrorKind.Validation);

    public static readonly DomainError KeyReused = new(
        "idempotency.key_reused",
        "Esta chave de idempotência já foi usada em outra requisição. Gere uma chave nova para enviar de novo.",
        DomainErrorKind.Validation);

    public static readonly DomainError InProgress = new(
        "idempotency.in_progress",
        "Esta requisição ainda está sendo processada. Aguarde um instante e tente de novo.",
        DomainErrorKind.Conflict);
}
