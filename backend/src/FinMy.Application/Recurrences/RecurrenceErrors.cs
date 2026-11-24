using FinMy.Domain.Common;

namespace FinMy.Application.Recurrences;

/// <summary>Erros estáveis de <c>/recurrences</c> (specs/005 contracts/api.md).</summary>
public static class RecurrenceErrors
{
    public static readonly DomainError NotFound = new("not_found", "Recorrência não encontrada.", DomainErrorKind.NotFound);

    public static readonly DomainError ConcurrencyConflict = new(
        "concurrency_conflict", "Esta recorrência foi alterada em outro aparelho. Recarregue.", DomainErrorKind.Conflict);

    public static readonly DomainError KindImmutable = Field("recurrence.kind_immutable", "kind", "O tipo da recorrência não pode mudar");

    public static readonly DomainError FromMonthBeforeStart = Field(
        "recurrence.from_month_before_start", "fromMonth", "O mês não pode ser anterior ao mês inicial da recorrência");

    public static readonly DomainError AlreadyEnded = Field("recurrence.already_ended", "fromMonth", "Esta recorrência já está encerrada");

    public static readonly DomainError ItemNotEligible = Field(
        "recurrence.item_not_eligible", "id", "Só Entrada, Saída ou Compra à vista que ainda não é recorrência pode se repetir todo mês");

    public static DomainError Field(string code, string field, string message) =>
        new(code, message, DomainErrorKind.Validation, new Dictionary<string, string[]> { [field] = [message] });
}
