using FinMy.Domain.Monetary;

namespace FinMy.Domain.Common;

/// <summary>Validações compartilhadas das entidades financeiras (RT-02, RT-03, RT-05a). Lançam <see cref="DomainException"/>.</summary>
internal static class Rules
{
    /// <summary>Menor data aceita (RT-03).</summary>
    public static readonly DateOnly MinDate = new(2000, 1, 1);

    /// <summary>Maior data aceita (RT-03).</summary>
    public static readonly DateOnly MaxDate = new(2100, 12, 31);

    public const int DescriptionMaxLength = 80;
    public const int NotesMaxLength = 500;

    public static DomainException Invalid(string code, string field, string message) =>
        new(new DomainError(
            code,
            message,
            DomainErrorKind.Validation,
            new Dictionary<string, string[]> { [field] = [message] }));

    /// <summary>Texto obrigatório com 1..<paramref name="max"/> caracteres após trim.</summary>
    public static string RequiredText(string? text, int max, string field, string code, string label)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw Invalid($"{code}.required", field, $"Informe {label}");
        }

        if (trimmed.Length > max)
        {
            throw Invalid($"{code}.too_long", field, $"{char.ToUpperInvariant(label[0])}{label[1..]} pode ter no máximo {max} caracteres");
        }

        return trimmed;
    }

    /// <summary>Texto opcional com no máximo <paramref name="max"/> caracteres; vazio vira <c>null</c>.</summary>
    public static string? OptionalText(string? text, int max, string field, string code, string label)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > max)
        {
            throw Invalid($"{code}.too_long", field, $"{char.ToUpperInvariant(label[0])}{label[1..]} pode ter no máximo {max} caracteres");
        }

        return trimmed;
    }

    /// <summary>Data entre 01/01/2000 e 31/12/2100 (RT-03).</summary>
    public static DateOnly Date(DateOnly date, string field)
    {
        if (date < MinDate || date > MaxDate)
        {
            throw Invalid("date.out_of_range", field, "A data deve estar entre 01/01/2000 e 31/12/2100");
        }

        return date;
    }

    /// <summary>Valor de lançamento: maior que zero e até R$ 9.999.999,99 (RT-02).</summary>
    public static long EntryAmount(Money amount, string field = "amount")
    {
        if (amount.Cents <= 0)
        {
            throw AmountError(ValueExpressionErrors.MustBePositive, field);
        }

        if (amount.Cents > Money.MaxCents)
        {
            throw AmountError(ValueExpressionErrors.TooLarge, field);
        }

        return amount.Cents;
    }

    /// <summary>Erro de valor com os códigos e mensagens compartilhados com o campo de valor (RT-05).</summary>
    public static DomainException AmountError(string code, string field) =>
        new(ValueExpressionErrors.ToDomainError(code, field));
}
