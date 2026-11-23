using FinMy.Domain.Monetary;

namespace FinMy.Domain.Tests.Money;

public class MoneyTests
{
    [Fact]
    public void Soma_subtrai_e_nega_em_centavos()
    {
        var a = Monetary.Money.FromCents(10000);
        var b = Monetary.Money.FromCents(2550);

        (a + b).Cents.ShouldBe(12550);
        (a - b).Cents.ShouldBe(7450);
        (-b).Cents.ShouldBe(-2550);
    }

    [Fact]
    public void Compara_por_centavos()
    {
        var small = Monetary.Money.FromCents(1);
        var big = Monetary.Money.FromCents(2);

        (small < big).ShouldBeTrue();
        (big > small).ShouldBeTrue();
        (small <= Monetary.Money.FromCents(1)).ShouldBeTrue();
        (big >= small).ShouldBeTrue();
        small.CompareTo(big).ShouldBeLessThan(0);
        Monetary.Money.FromCents(5).ShouldBe(new Monetary.Money(5));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(999_999_999, true)]
    [InlineData(1_000_000_000, false)]
    [InlineData(-1, false)]
    public void Valor_de_lancamento_e_positivo_e_ate_o_maximo(long cents, bool valid)
    {
        Monetary.Money.FromCents(cents).IsValidEntryAmount.ShouldBe(valid);
    }

    [Fact]
    public void Sinal_e_zero()
    {
        Monetary.Money.Zero.IsPositive.ShouldBeFalse();
        Monetary.Money.Zero.IsNegative.ShouldBeFalse();
        Monetary.Money.FromCents(-1).IsNegative.ShouldBeTrue();
        Monetary.Money.FromCents(1).IsPositive.ShouldBeTrue();
        Monetary.Money.FromCents(-5).ToString().ShouldBe("-5");
    }

    [Fact]
    public void Estouro_de_long_lanca()
    {
        Should.Throw<OverflowException>(() => Monetary.Money.FromCents(long.MaxValue) + Monetary.Money.FromCents(1));
    }
}
