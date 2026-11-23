namespace FinMy.Domain.Common;

/// <summary>Categoria de um erro de domínio; a Api a traduz para o status HTTP.</summary>
public enum DomainErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Unauthorized,
    Forbidden,
    Gone,
    TooManyRequests,
}

/// <summary>Erro esperado de regra de negócio, com <paramref name="Code"/> estável e <paramref name="Message"/> em pt-BR.</summary>
/// <param name="Code">Código estável (ex.: <c>email_already_registered</c>).</param>
/// <param name="Message">Mensagem pronta para exibir, em pt-BR.</param>
/// <param name="Kind">Categoria do erro.</param>
/// <param name="FieldErrors">Mensagens por campo (somente <see cref="DomainErrorKind.Validation"/>).</param>
/// <param name="RetryAfterSeconds">Espera sugerida (somente <see cref="DomainErrorKind.TooManyRequests"/>).</param>
public sealed record DomainError(
    string Code,
    string Message,
    DomainErrorKind Kind,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null,
    int? RetryAfterSeconds = null);

/// <summary>Transporta um <see cref="DomainError"/> até a borda da Api, que o converte em ProblemDetails.</summary>
public sealed class DomainException(DomainError error) : Exception(error?.Message)
{
    public DomainError Error { get; } = error ?? throw new ArgumentNullException(nameof(error));
}
