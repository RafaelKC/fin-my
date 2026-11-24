using FinMy.Domain.Common;

namespace FinMy.Application.Recurrences;

/// <summary>Erros estáveis de <c>/recurrences</c> (specs/005 contracts/api.md).</summary>
public static class RecurrenceErrors
{
    public static readonly DomainError NotFound = new("not_found", "Recorrência não encontrada.", DomainErrorKind.NotFound);

    public static DomainError Field(string code, string field, string message) =>
        new(code, message, DomainErrorKind.Validation, new Dictionary<string, string[]> { [field] = [message] });
}
