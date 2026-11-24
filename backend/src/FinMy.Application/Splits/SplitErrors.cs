using FinMy.Domain.Common;

namespace FinMy.Application.Splits;

/// <summary>Erros estáveis de <c>POST /entries/split</c> e <c>POST /card-purchases/split</c> (004 research R5).</summary>
public static class SplitErrors
{
    public static readonly DomainError Required = Invalid("split.required", "split", "Informe como dividir.");

    public static readonly DomainError InvalidMode = Invalid("split.mode_invalid", "split.mode", "Use partes iguais ou valores informados.");

    public static readonly DomainError ExpenseOnly = Invalid("split.expense_only", "kind", "Só uma Saída pode ser dividida.");

    public static readonly DomainError CreditNotAllowed = Invalid(
        "split.credit_not_allowed", "amount", "Um crédito no cartão não pode ser dividido.");

    public static readonly DomainError InProgressNotAllowed = Invalid(
        "split.in_progress_not_allowed", "installments.mode", "Um parcelamento em andamento não pode ser dividido.");

    public static readonly DomainError PartNotSettlement = Invalid(
        "split.part_not_settlement", "split.parts", "Cada parte precisa de uma tag de acerto ou de \"Eu\".");

    public static readonly DomainError SettlementTagInTagIds = Invalid(
        "split.settlement_tag_in_tag_ids", "tagIds", "Na divisão, a tag de acerto é escolhida em cada parte.");

    private static DomainError Invalid(string code, string field, string message) =>
        new(code, message, DomainErrorKind.Validation, new Dictionary<string, string[]> { [field] = [message] });
}
