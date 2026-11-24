using FinMy.Domain.Common;
using FinMy.Domain.Monetary;
using FinMy.Domain.Recurrences;

namespace FinMy.Application.Recurrences;

/// <summary>Leitura dos textos da recorrência vindos da borda; erros de formato viram <see cref="DomainException"/> com código estável.</summary>
public static class RecurrenceInput
{
    public static RecurrenceKind ParseKind(string? text) => text switch
    {
        "income" => RecurrenceKind.Income,
        "expense" => RecurrenceKind.Expense,
        "card_purchase" => RecurrenceKind.CardPurchase,
        _ => throw new DomainException(RecurrenceErrors.Field("kind.invalid", "kind", "Escolha Entrada, Saída ou Compra")),
    };

    /// <summary>Ausente = sem fim.</summary>
    public static RecurrenceEndType ParseEndType(string? text) => text switch
    {
        null or "" or "none" => RecurrenceEndType.None,
        "until_month" => RecurrenceEndType.UntilMonth,
        "after_count" => RecurrenceEndType.AfterCount,
        _ => throw new DomainException(RecurrenceErrors.Field("endType.invalid", "endType", "Escolha sem fim, até um mês ou após N ocorrências")),
    };

    /// <summary>Ausente = <c>null</c> (quem chama aplica o padrão da regra).</summary>
    public static ReferenceMonthRule? ParseReferenceMonthRule(string? text) => text switch
    {
        null or "" => null,
        "same_month" => ReferenceMonthRule.SameMonth,
        "next_month" => ReferenceMonthRule.NextMonth,
        _ => throw new DomainException(RecurrenceErrors.Field("referenceMonthRule.invalid", "referenceMonthRule", "Escolha o mesmo mês ou o mês seguinte")),
    };

    public static YearMonth ParseMonth(string? text, string field) =>
        YearMonth.TryParse(text, out var month)
            ? month
            : throw new DomainException(RecurrenceErrors.Field($"{field}.invalid", field, "Use o formato AAAA-MM"));

    public static YearMonth? ParseOptionalMonth(string? text, string field) =>
        string.IsNullOrEmpty(text) ? null : ParseMonth(text, field);

    public static string ToText(RecurrenceKind kind) => kind switch
    {
        RecurrenceKind.Income => "income",
        RecurrenceKind.Expense => "expense",
        _ => "card_purchase",
    };

    public static string ToText(RecurrenceEndType endType) => endType switch
    {
        RecurrenceEndType.UntilMonth => "until_month",
        RecurrenceEndType.AfterCount => "after_count",
        _ => "none",
    };

    public static string ToText(ReferenceMonthRule rule) => rule == ReferenceMonthRule.NextMonth ? "next_month" : "same_month";
}
