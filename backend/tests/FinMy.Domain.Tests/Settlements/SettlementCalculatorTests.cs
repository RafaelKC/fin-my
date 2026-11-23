using FinMy.Domain.Monetary;
using FinMy.Domain.Settlements;

namespace FinMy.Domain.Tests.Settlements;

/// <summary>004 US2 / SC-002: exemplo numérico da Tag de acerto "Bruna" em 2026-03.</summary>
public class SettlementCalculatorTests
{
    private static readonly YearMonth Marco = YearMonth.Of(2026, 3);

    // Compras Ifood 156,81 e Uber 12,72 (débito), Estorno 9,53 (crédito), Saída "Presente Bruna" 40,00.
    private static SettlementMonthInput MarcoDaBruna(long cobrado = 15_000 + 3_000, long recebido = 3_000) =>
        new(ExpenseCents: 4_000, CardDebitCents: 15_681 + 1_272, CardCreditCents: 953, ChargedCents: cobrado, ReceivedCents: recebido);

    [Fact]
    public void Exemplo_da_US2_marco_de_Bruna()
    {
        var r = SettlementCalculator.Calculate(Marco, new Dictionary<YearMonth, SettlementMonthInput> { [Marco] = MarcoDaBruna() });

        r.SpentCents.ShouldBe(20_000);
        r.ChargedCents.ShouldBe(18_000);
        r.ReceivedCents.ShouldBe(3_000);
        r.ToChargeCents.ShouldBe(2_000);
        r.ToReceiveCents.ShouldBe(17_000);
        r.AccumulatedToReceiveCents.ShouldBe(17_000);
    }

    [Fact]
    public void US2_2_entrada_de_150_marcada_como_recebida()
    {
        var r = SettlementCalculator.Calculate(
            Marco,
            new Dictionary<YearMonth, SettlementMonthInput> { [Marco] = MarcoDaBruna(recebido: 15_000 + 3_000) });

        r.ReceivedCents.ShouldBe(18_000);
        r.ToReceiveCents.ShouldBe(2_000);
        r.AccumulatedToReceiveCents.ShouldBe(2_000);
    }

    [Fact]
    public void US2_3_saldo_acumulado_negativo_e_credito_da_pessoa()
    {
        // Bruna gastou 50,00 e pagou 80,00: A receber = −30,00.
        var inputs = new Dictionary<YearMonth, SettlementMonthInput>
        {
            [Marco] = new(5_000, 0, 0, 8_000, 8_000),
        };

        var r = SettlementCalculator.Calculate(Marco, inputs);

        r.ToReceiveCents.ShouldBe(-3_000);
        r.ToChargeCents.ShouldBe(-3_000);
        r.AccumulatedToReceiveCents.ShouldBe(-3_000);
    }

    [Fact]
    public void Saldo_acumulado_soma_os_meses_ate_o_escolhido_e_ignora_os_posteriores()
    {
        var janeiro = YearMonth.Of(2026, 1);
        var fevereiro = YearMonth.Of(2026, 2);
        var abril = YearMonth.Of(2026, 4);
        var inputs = new Dictionary<YearMonth, SettlementMonthInput>
        {
            [janeiro] = new(10_000, 0, 0, 0, 0),
            [fevereiro] = new(0, 6_000, 1_000, 5_000, 2_000),
            [Marco] = MarcoDaBruna(),
            [abril] = new(99_999, 0, 0, 0, 0),
        };

        // A receber: jan 100,00; fev (60,00 − 10,00) − 20,00 = 30,00; mar 170,00.
        SettlementCalculator.Calculate(janeiro, inputs).AccumulatedToReceiveCents.ShouldBe(10_000);
        SettlementCalculator.Calculate(fevereiro, inputs).AccumulatedToReceiveCents.ShouldBe(13_000);

        var r = SettlementCalculator.Calculate(Marco, inputs);
        r.ToReceiveCents.ShouldBe(17_000);
        r.AccumulatedToReceiveCents.ShouldBe(30_000);
    }

    [Fact]
    public void Mes_sem_movimento_mostra_zeros_mas_carrega_o_acumulado()
    {
        var inputs = new Dictionary<YearMonth, SettlementMonthInput> { [Marco] = MarcoDaBruna() };

        var r = SettlementCalculator.Calculate(YearMonth.Of(2026, 5), inputs);

        r.SpentCents.ShouldBe(0);
        r.ToReceiveCents.ShouldBe(0);
        r.AccumulatedToReceiveCents.ShouldBe(17_000);
    }

    [Fact]
    public void Sem_nenhum_movimento_tudo_zero()
    {
        SettlementCalculator.Calculate(Marco, new Dictionary<YearMonth, SettlementMonthInput>()).ShouldBe(default);
    }
}
