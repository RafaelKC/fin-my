namespace FinMy.Domain.Monetary;

/// <summary>Valor monetário em BRL, em centavos inteiros e com sinal (RT-02). Nunca usa <c>decimal</c>/<c>double</c>.</summary>
public readonly record struct Money(long Cents) : IComparable<Money>
{
    /// <summary>Valor máximo por lançamento: R$ 9.999.999,99 (RT-02).</summary>
    public const long MaxCents = 999_999_999;

    public static Money Zero => new(0);

    public static Money FromCents(long cents) => new(cents);

    public bool IsPositive => Cents > 0;

    public bool IsNegative => Cents < 0;

    /// <summary>Positivo e dentro do máximo por lançamento (RT-02).</summary>
    public bool IsValidEntryAmount => Cents is > 0 and <= MaxCents;

    public int CompareTo(Money other) => Cents.CompareTo(other.Cents);

    public static Money operator +(Money left, Money right) => new(checked(left.Cents + right.Cents));

    public static Money operator -(Money left, Money right) => new(checked(left.Cents - right.Cents));

    public static Money operator -(Money value) => new(checked(-value.Cents));

    public static bool operator <(Money left, Money right) => left.Cents < right.Cents;

    public static bool operator >(Money left, Money right) => left.Cents > right.Cents;

    public static bool operator <=(Money left, Money right) => left.Cents <= right.Cents;

    public static bool operator >=(Money left, Money right) => left.Cents >= right.Cents;

    public override string ToString() => Cents.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
