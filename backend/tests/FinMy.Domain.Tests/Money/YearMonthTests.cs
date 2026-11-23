using FinMy.Domain.Monetary;

namespace FinMy.Domain.Tests.Money;

public class YearMonthTests
{
    [Fact]
    public void Of_guarda_o_dia_1()
    {
        var month = YearMonth.Of(2026, 3);

        month.FirstDay.ShouldBe(new DateOnly(2026, 3, 1));
        month.Year.ShouldBe(2026);
        month.Month.ShouldBe(3);
    }

    [Fact]
    public void From_data_usa_o_mes_da_data()
    {
        YearMonth.From(new DateOnly(2026, 3, 31)).ShouldBe(YearMonth.Of(2026, 3));
    }

    [Theory]
    [InlineData(2026, 2, 28)]
    [InlineData(2028, 2, 29)]
    [InlineData(2026, 4, 30)]
    [InlineData(2026, 12, 31)]
    public void Ultimo_dia_do_mes(int year, int month, int day)
    {
        YearMonth.Of(year, month).LastDay.ShouldBe(new DateOnly(year, month, day));
    }

    [Fact]
    public void Anterior_e_seguinte_atravessam_o_ano()
    {
        YearMonth.Of(2026, 1).Previous().ShouldBe(YearMonth.Of(2025, 12));
        YearMonth.Of(2026, 12).Next().ShouldBe(YearMonth.Of(2027, 1));
        YearMonth.Of(2026, 3).AddMonths(-14).ShouldBe(YearMonth.Of(2025, 1));
    }

    [Fact]
    public void Contem_apenas_datas_do_proprio_mes()
    {
        var march = YearMonth.Of(2026, 3);

        march.Contains(new DateOnly(2026, 3, 1)).ShouldBeTrue();
        march.Contains(new DateOnly(2026, 3, 31)).ShouldBeTrue();
        march.Contains(new DateOnly(2026, 4, 1)).ShouldBeFalse();
        march.Contains(new DateOnly(2025, 3, 15)).ShouldBeFalse();
    }

    [Theory]
    [InlineData("2026-03", 2026, 3)]
    [InlineData("2026-12", 2026, 12)]
    [InlineData("0001-01", 1, 1)]
    public void Le_AAAA_MM(string text, int year, int month)
    {
        YearMonth.TryParse(text, out var value).ShouldBeTrue();
        value.ShouldBe(YearMonth.Of(year, month));
        value.ToString().ShouldBe(text);
        YearMonth.Parse(text).ShouldBe(value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-3")]
    [InlineData("2026-13")]
    [InlineData("2026-00")]
    [InlineData("0000-01")]
    [InlineData("2026/03")]
    [InlineData("2026-03-01")]
    [InlineData("26-03")]
    [InlineData("abcd-ef")]
    [InlineData("2026-0a")]
    public void Rejeita_formatos_invalidos(string? text)
    {
        YearMonth.TryParse(text, out _).ShouldBeFalse();
    }

    [Fact]
    public void Parse_invalido_lanca()
    {
        Should.Throw<FormatException>(() => YearMonth.Parse("2026-13"));
    }

    [Fact]
    public void Compara_cronologicamente()
    {
        var a = YearMonth.Of(2025, 12);
        var b = YearMonth.Of(2026, 1);

        (a < b).ShouldBeTrue();
        (b > a).ShouldBeTrue();
        (a <= YearMonth.Of(2025, 12)).ShouldBeTrue();
        (b >= a).ShouldBeTrue();
        a.CompareTo(b).ShouldBeLessThan(0);
        new[] { b, a }.Order().ShouldBe([a, b]);
    }

    [Fact]
    public void Formata_com_zero_a_esquerda()
    {
        YearMonth.Of(2026, 3).ToString().ShouldBe("2026-03");
    }
}
