using FinMy.Domain.Monetary;

namespace FinMy.Domain.Tests.Money;

public class MoneySplitterTests
{
    private const string File = "money-split.json";

    public static TheoryData<string> Ids => TestVectors.Ids(File);

    [Theory]
    [MemberData(nameof(Ids))]
    public void Vetor_compartilhado(string id)
    {
        var vector = TestVectors.Get(File, id);
        var expected = vector.Expected.GetProperty("partsCents").EnumerateArray().Select(e => e.GetInt64()).ToArray();

        var parts = MoneySplitter.Split(
            vector.Input.GetProperty("totalCents").GetInt64(),
            vector.Input.GetProperty("parts").GetInt32());

        parts.ShouldBe(expected);
    }

    [Theory]
    [InlineData(10000, 3)]
    [InlineData(1, 48)]
    [InlineData(999_999_999, 48)]
    public void A_soma_das_partes_e_o_total(long total, int parts)
    {
        MoneySplitter.Split(total, parts).Sum().ShouldBe(total);
    }

    [Fact]
    public void Quantidade_de_partes_invalida_lanca()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => MoneySplitter.Split(100, 0));
    }

    [Fact]
    public void Total_negativo_lanca()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => MoneySplitter.Split(-1, 2));
    }
}
