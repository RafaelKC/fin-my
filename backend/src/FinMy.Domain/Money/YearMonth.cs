using System.Globalization;

namespace FinMy.Domain.Monetary;

/// <summary>Mês de calendário (<c>AAAA-MM</c>), guardado como <see cref="DateOnly"/> no dia 1.</summary>
public readonly record struct YearMonth : IComparable<YearMonth>
{
    private YearMonth(DateOnly firstDay) => FirstDay = firstDay;

    /// <summary>Dia 1 do mês (valor gravado nas colunas de mês).</summary>
    public DateOnly FirstDay { get; }

    public int Year => FirstDay.Year;

    public int Month => FirstDay.Month;

    /// <summary>Último dia do mês.</summary>
    public DateOnly LastDay => FirstDay.AddMonths(1).AddDays(-1);

    public static YearMonth Of(int year, int month) => new(new DateOnly(year, month, 1));

    /// <summary>Mês de uma data.</summary>
    public static YearMonth From(DateOnly date) => new(new DateOnly(date.Year, date.Month, 1));

    /// <summary>Lê <c>AAAA-MM</c> (4 dígitos, hífen, 2 dígitos). Qualquer outra forma é rejeitada.</summary>
    public static bool TryParse(string? text, out YearMonth value)
    {
        value = default;
        if (text is not { Length: 7 } || text[4] != '-')
        {
            return false;
        }

        if (!IsDigits(text.AsSpan(0, 4)) || !IsDigits(text.AsSpan(5, 2)))
        {
            return false;
        }

        var year = int.Parse(text.AsSpan(0, 4), CultureInfo.InvariantCulture);
        var month = int.Parse(text.AsSpan(5, 2), CultureInfo.InvariantCulture);
        if (year < 1 || month is < 1 or > 12)
        {
            return false;
        }

        value = Of(year, month);
        return true;
    }

    public static YearMonth Parse(string text) =>
        TryParse(text, out var value)
            ? value
            : throw new FormatException($"Mês inválido: '{text}'. Use AAAA-MM.");

    public YearMonth AddMonths(int months) => new(FirstDay.AddMonths(months));

    public YearMonth Previous() => AddMonths(-1);

    public YearMonth Next() => AddMonths(1);

    public bool Contains(DateOnly date) => date.Year == Year && date.Month == Month;

    public int CompareTo(YearMonth other) => FirstDay.CompareTo(other.FirstDay);

    public static bool operator <(YearMonth left, YearMonth right) => left.CompareTo(right) < 0;

    public static bool operator >(YearMonth left, YearMonth right) => left.CompareTo(right) > 0;

    public static bool operator <=(YearMonth left, YearMonth right) => left.CompareTo(right) <= 0;

    public static bool operator >=(YearMonth left, YearMonth right) => left.CompareTo(right) >= 0;

    /// <summary>Formato <c>AAAA-MM</c>.</summary>
    public override string ToString() => FirstDay.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static bool IsDigits(ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
