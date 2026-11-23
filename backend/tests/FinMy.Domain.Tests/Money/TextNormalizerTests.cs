using FinMy.Domain.Monetary;

namespace FinMy.Domain.Tests.Money;

public class TextNormalizerTests
{
    private const string File = "text-normalize.json";

    public static TheoryData<string> Ids => TestVectors.Ids(File);

    [Theory]
    [MemberData(nameof(Ids))]
    public void Vetor_compartilhado(string id)
    {
        var vector = TestVectors.Get(File, id);

        TextNormalizer.Normalize(vector.Input.GetProperty("text").GetString()!)
            .ShouldBe(vector.Expected.GetProperty("text").GetString());
    }

    [Fact]
    public void Texto_nulo_lanca()
    {
        Should.Throw<ArgumentNullException>(() => TextNormalizer.Normalize(null!));
    }
}
