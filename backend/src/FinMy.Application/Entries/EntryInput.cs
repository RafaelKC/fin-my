using FinMy.Domain.Common;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Entries;

/// <summary>
/// Leitura dos campos de texto de Entrada/Saída vindos da borda (tipo, situação, mês e valor). Erros de formato viram
/// <see cref="DomainException"/> de validação com código estável; as regras de negócio ficam no <c>Entry</c>.
/// </summary>
public static class EntryInput
{
    public static EntryKind ParseKind(string? text) => text switch
    {
        "income" => EntryKind.Income,
        "expense" => EntryKind.Expense,
        _ => throw new DomainException(EntryErrors.Field("kind.invalid", "kind", "Escolha Entrada ou Saída")),
    };

    public static EntryStatus ParseStatus(string? text) => text switch
    {
        "planned" => EntryStatus.Planned,
        "settled" => EntryStatus.Settled,
        _ => throw new DomainException(EntryErrors.Field("status.invalid", "status", "Escolha Prevista ou Recebida/Paga")),
    };

    public static YearMonth ParseReferenceMonth(string text) =>
        YearMonth.TryParse(text, out var month)
            ? month
            : throw new DomainException(EntryErrors.Field("referenceMonth.invalid", "referenceMonth", "Use o formato AAAA-MM"));

    /// <summary>Avalia a expressão de valor (RT-05) com o mesmo parser da pré-visualização.</summary>
    public static Money ParseAmount(string? text)
    {
        var result = ValueExpressionParser.Parse(text);
        return result.IsSuccess
            ? result.Money
            : throw new DomainException(ValueExpressionErrors.ToDomainError(result.Error!, "amount"));
    }

    public static string ToText(EntryKind kind) => kind == EntryKind.Income ? "income" : "expense";

    public static string ToText(EntryStatus status) => status == EntryStatus.Planned ? "planned" : "settled";
}
