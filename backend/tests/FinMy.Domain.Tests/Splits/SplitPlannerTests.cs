using FinMy.Domain.Common;
using FinMy.Domain.Monetary;
using FinMy.Domain.Splits;

namespace FinMy.Domain.Tests.Splits;

/// <summary>004 US4.1–US4.4 e research R5.</summary>
public class SplitPlannerTests
{
    private static readonly Guid Bruna = Guid.CreateVersion7();
    private static readonly Guid Carla = Guid.CreateVersion7();
    private static readonly Guid Duda = Guid.CreateVersion7();

    [Fact]
    public void US4_1_239_80_em_partes_iguais_entre_eu_e_Bruna()
    {
        var parts = SplitPlanner.Plan(23_980, SplitMode.Equal, [new(null), new(Bruna)]);

        parts.ShouldBe([new SplitPart(null, 11_990), new SplitPart(Bruna, 11_990)]);
    }

    [Fact]
    public void US4_2_valores_informados_somam_207_90_e_a_parcela_de_eu_fecha_em_4x()
    {
        var parts = SplitPlanner.Plan(
            20_790,
            SplitMode.Amounts,
            [new(null, 11_094), new(Carla, 1_199), new(Bruna, 8_497)]);

        parts.Select(p => p.AmountCents).ShouldBe([11_094L, 1_199, 8_497]);

        // Cada parte vira um parcelamento próprio de 4x (RT-02): a parte de "Eu" fica 27,75 + 27,73 + 27,73 + 27,73.
        MoneySplitter.Split(parts[0].AmountCents, 4).ShouldBe([2_775L, 2_773, 2_773, 2_773]);
    }

    [Fact]
    public void US4_3_soma_diferente_do_total_informa_a_diferenca()
    {
        var erro = Should.Throw<DomainException>(
            () => SplitPlanner.Plan(10_000, SplitMode.Amounts, [new(null, 6_000), new(Bruna, 3_500)]));

        erro.Error.Code.ShouldBe("split.sum_mismatch");
        erro.Error.Kind.ShouldBe(DomainErrorKind.Validation);
        erro.Error.Extensions!["differenceCents"].ShouldBe(500L);
    }

    [Fact]
    public void US4_3_soma_acima_do_total_tem_diferenca_negativa()
    {
        var erro = Should.Throw<DomainException>(
            () => SplitPlanner.Plan(10_000, SplitMode.Amounts, [new(null, 6_000), new(Bruna, 5_000)]));

        erro.Error.Extensions!["differenceCents"].ShouldBe(-1_000L);
    }

    [Fact]
    public void US4_4_100_em_3_partes_iguais_a_sobra_vai_para_a_primeira_na_ordem_pedida()
    {
        var parts = SplitPlanner.Plan(10_000, SplitMode.Equal, [new(Carla), new(null), new(Bruna)]);

        parts.ShouldBe([new SplitPart(Carla, 3_334), new SplitPart(null, 3_333), new SplitPart(Bruna, 3_333)]);
        parts.Sum(p => p.AmountCents).ShouldBe(10_000);
    }

    [Fact]
    public void Aceita_de_2_a_10_partes()
    {
        var dez = Enumerable.Range(0, 10).Select(i => new SplitPartRequest(i == 0 ? null : Guid.CreateVersion7())).ToList();

        SplitPlanner.Plan(10_000, SplitMode.Equal, dez).Count.ShouldBe(10);
        SplitPlanner.Plan(10_000, SplitMode.Equal, dez.Take(2).ToList()).Count.ShouldBe(2);
    }

    [Fact]
    public void Recusa_menos_de_2_e_mais_de_10_partes()
    {
        Should.Throw<DomainException>(() => SplitPlanner.Plan(10_000, SplitMode.Equal, [new(Bruna)])).Error.Code.ShouldBe("split.parts_count");

        var onze = Enumerable.Range(0, 11).Select(_ => new SplitPartRequest(Guid.CreateVersion7())).ToList();
        Should.Throw<DomainException>(() => SplitPlanner.Plan(10_000, SplitMode.Equal, onze)).Error.Code.ShouldBe("split.parts_count");
    }

    [Fact]
    public void Recusa_tag_repetida_e_mais_de_um_eu()
    {
        Should.Throw<DomainException>(() => SplitPlanner.Plan(10_000, SplitMode.Equal, [new(Bruna), new(Bruna)]))
            .Error.Code.ShouldBe("split.duplicate_part");
        Should.Throw<DomainException>(() => SplitPlanner.Plan(10_000, SplitMode.Equal, [new(null), new(null), new(Duda)]))
            .Error.Code.ShouldBe("split.duplicate_part");
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Valores_informados_precisam_ser_positivos_e_presentes(long cents)
    {
        Should.Throw<DomainException>(() => SplitPlanner.Plan(10_000, SplitMode.Amounts, [new(null, cents), new(Bruna, 10_000 - cents)]))
            .Error.Code.ShouldBe("amount.must_be_positive");
        Should.Throw<DomainException>(() => SplitPlanner.Plan(10_000, SplitMode.Amounts, [new(null, 10_000), new(Bruna)]))
            .Error.Code.ShouldBe("amount.must_be_positive");
    }

    [Theory]
    [InlineData(0L, "amount.must_be_positive")]
    [InlineData(1_000_000_000L, "amount.too_large")]
    public void Total_fora_do_limite_e_recusado(long total, string codigo)
    {
        Should.Throw<DomainException>(() => SplitPlanner.Plan(total, SplitMode.Equal, [new(null), new(Bruna)])).Error.Code.ShouldBe(codigo);
    }
}
