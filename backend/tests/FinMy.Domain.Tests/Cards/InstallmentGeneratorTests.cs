using FinMy.Domain.Cards;
using FinMy.Domain.Monetary;
using FinMy.Domain.Tests.Money;

namespace FinMy.Domain.Tests.Cards;

public class InstallmentGeneratorTests
{
    private const string File = "installments.json";

    public static TheoryData<string> Ids => TestVectors.Ids(File);

    [Theory]
    [MemberData(nameof(Ids))]
    public void Vetor_compartilhado(string id)
    {
        var vector = TestVectors.Get(File, id);
        var input = vector.Input;
        var mode = input.GetProperty("mode").GetString() switch
        {
            "total_amount" => InstallmentMode.TotalAmount,
            "installment_amount" => InstallmentMode.InstallmentAmount,
            "in_progress" => InstallmentMode.InProgress,
            var other => throw new InvalidOperationException($"modo desconhecido: {other}"),
        };

        var result = InstallmentGenerator.Generate(
            mode,
            input.GetProperty("installmentCount").GetInt32(),
            input.GetProperty("firstNumber").GetInt32(),
            input.GetProperty("amountCents").GetInt64(),
            YearMonth.Parse(input.GetProperty("firstInvoiceMonth").GetString()!));

        var expected = vector.Expected.GetProperty("installments").EnumerateArray()
            .Select(e => new GeneratedInstallment(
                e.GetProperty("number").GetInt32(),
                e.GetProperty("amountCents").GetInt64(),
                YearMonth.Parse(e.GetProperty("dueMonth").GetString()!)))
            .ToList();
        result.ShouldBe(expected);
    }

    [Fact]
    public void Total_parcelado_soma_o_total()
    {
        InstallmentGenerator.Generate(InstallmentMode.TotalAmount, 7, 1, 99_999, YearMonth.Of(2026, 1))
            .Sum(p => p.AmountCents).ShouldBe(99_999);
    }

    [Theory]
    [InlineData(0, 1, 100)]
    [InlineData(8, 9, 100)]
    [InlineData(8, 0, 100)]
    [InlineData(8, 5, 0)]
    public void Entrada_invalida_em_andamento_lanca(int n, int firstNumber, long amount)
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            InstallmentGenerator.Generate(InstallmentMode.InProgress, n, firstNumber, amount, YearMonth.Of(2026, 1)));
    }
}
