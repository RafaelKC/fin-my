using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;
using FinMy.Domain.Months;

namespace FinMy.Domain.Tests.Months;

/// <summary>Resumo do mês (002 FR-015, research R4): exemplo numérico da US3 e variações de carteira/transferência.</summary>
public class MonthSummaryCalculatorTests
{
    private static readonly YearMonth March = YearMonth.Of(2026, 3);
    private static readonly YearMonth April = YearMonth.Of(2026, 4);
    private static readonly YearMonth June = YearMonth.Of(2026, 6);

    private static readonly Guid Azul = Guid.NewGuid();
    private static readonly Guid Poupanca = Guid.NewGuid();

    private static readonly InvoiceAmount[] NoInvoices = [];

    // Exemplo da US3: carteira Azul com saldo inicial R$ 500,00 em 01/03/2026.
    private static readonly WalletOpening[] AzulComSaldoDeMarco = [new(Azul, 50000, new DateOnly(2026, 3, 1))];

    private static readonly EntryTotal[] ExemploUs3 =
    [
        new(March, EntryKind.Income, EntryStatus.Settled, 150000),
        new(March, EntryKind.Income, EntryStatus.Planned, 1827),
        new(March, EntryKind.Expense, EntryStatus.Settled, 4499),
        new(March, EntryKind.Expense, EntryStatus.Planned, 77003),
    ];

    private static MonthSummary Calculate(
        YearMonth month,
        IReadOnlyCollection<WalletOpening> wallets,
        IReadOnlyCollection<EntryTotal> entries,
        IReadOnlyCollection<TransferTotal>? transfers = null) =>
        MonthSummaryCalculator.Calculate(month, wallets, entries, transfers ?? [], NoInvoices, NoInvoices);

    [Fact]
    public void Marco_do_exemplo_da_US3_bate_todos_os_numeros_do_Previsto()
    {
        var planned = Calculate(March, AzulComSaldoDeMarco, ExemploUs3).Planned;

        planned.OpeningBalanceCents.ShouldBe(50000);
        planned.WalletsInitialBalanceCents.ShouldBe(50000);
        planned.IncomeCents.ShouldBe(151827);
        planned.ExpenseCents.ShouldBe(81502);
        planned.ResultCents.ShouldBe(70325);
        planned.FinalBalanceCents.ShouldBe(120325);
        planned.TransfersInCents.ShouldBe(0);
        planned.TransfersOutCents.ShouldBe(0);
    }

    [Fact]
    public void Marco_do_exemplo_da_US3_bate_todos_os_numeros_do_Realizado()
    {
        var actual = Calculate(March, AzulComSaldoDeMarco, ExemploUs3).Actual;

        actual.OpeningBalanceCents.ShouldBe(50000);
        actual.IncomeCents.ShouldBe(150000);
        actual.ExpenseCents.ShouldBe(4499);
        actual.ResultCents.ShouldBe(145501);
        actual.FinalBalanceCents.ShouldBe(195501);
    }

    [Fact]
    public void Abril_sem_lancamentos_herda_o_saldo_final_de_marco_e_nao_repete_o_saldo_inicial_da_carteira()
    {
        var summary = Calculate(April, AzulComSaldoDeMarco, ExemploUs3);

        summary.Planned.OpeningBalanceCents.ShouldBe(120325);
        summary.Planned.WalletsInitialBalanceCents.ShouldBe(0);
        summary.Planned.ResultCents.ShouldBe(0);
        summary.Planned.FinalBalanceCents.ShouldBe(120325);
        summary.Actual.OpeningBalanceCents.ShouldBe(195501);
        summary.Actual.FinalBalanceCents.ShouldBe(195501);
    }

    [Fact]
    public void US3_2_marcar_Duda_como_Recebida_muda_Realizado_para_o_esperado()
    {
        EntryTotal[] aposMarcar =
        [
            new(March, EntryKind.Income, EntryStatus.Settled, 150000 + 1827),
            new(March, EntryKind.Expense, EntryStatus.Settled, 4499),
            new(March, EntryKind.Expense, EntryStatus.Planned, 77003),
        ];

        var summary = Calculate(March, AzulComSaldoDeMarco, aposMarcar);

        summary.Actual.IncomeCents.ShouldBe(151827);
        summary.Actual.ResultCents.ShouldBe(147328);
        summary.Actual.FinalBalanceCents.ShouldBe(197328);
        summary.Planned.ResultCents.ShouldBe(70325);
    }

    [Fact]
    public void Lancamentos_de_meses_posteriores_ao_pedido_sao_ignorados()
    {
        var comAbril = ExemploUs3.Append(new EntryTotal(April, EntryKind.Expense, EntryStatus.Settled, 99999)).ToList();

        var summary = Calculate(March, AzulComSaldoDeMarco, comAbril);

        summary.Planned.FinalBalanceCents.ShouldBe(120325);
        summary.Actual.FinalBalanceCents.ShouldBe(195501);
    }

    [Fact]
    public void Mes_anterior_ao_saldo_inicial_da_carteira_nao_conta_o_saldo()
    {
        var summary = Calculate(YearMonth.Of(2026, 2), AzulComSaldoDeMarco, []);

        summary.Planned.OpeningBalanceCents.ShouldBe(0);
        summary.Planned.WalletsInitialBalanceCents.ShouldBe(0);
        summary.Planned.FinalBalanceCents.ShouldBe(0);
    }

    [Fact]
    public void Carteira_com_saldo_inicial_no_meio_do_periodo_entra_no_saldo_do_mes_da_data_e_nos_seguintes()
    {
        // Azul desde janeiro (R$ 100,00); Poupança só em março (R$ 300,00).
        WalletOpening[] wallets =
        [
            new(Azul, 10000, new DateOnly(2026, 1, 1)),
            new(Poupanca, 30000, new DateOnly(2026, 3, 15)),
        ];
        EntryTotal[] entries =
        [
            new(YearMonth.Of(2026, 1), EntryKind.Income, EntryStatus.Settled, 5000),
            new(March, EntryKind.Expense, EntryStatus.Settled, 2000),
        ];

        var february = Calculate(YearMonth.Of(2026, 2), wallets, entries);
        var march = Calculate(March, wallets, entries);
        var april = Calculate(April, wallets, entries);

        february.Actual.OpeningBalanceCents.ShouldBe(15000);
        february.Actual.WalletsInitialBalanceCents.ShouldBe(0);
        february.Actual.FinalBalanceCents.ShouldBe(15000);

        march.Actual.OpeningBalanceCents.ShouldBe(45000);
        march.Actual.WalletsInitialBalanceCents.ShouldBe(30000);
        march.Actual.ResultCents.ShouldBe(-2000);
        march.Actual.FinalBalanceCents.ShouldBe(43000);

        april.Actual.OpeningBalanceCents.ShouldBe(43000);
        april.Actual.WalletsInitialBalanceCents.ShouldBe(0);
    }

    [Fact]
    public void Saldo_inicial_negativo_de_carteira_reduz_o_saldo()
    {
        var summary = Calculate(March, [new WalletOpening(Azul, -15000, new DateOnly(2026, 3, 1))], []);

        summary.Planned.OpeningBalanceCents.ShouldBe(-15000);
        summary.Planned.FinalBalanceCents.ShouldBe(-15000);
    }

    // US5.1: Azul com 1.000,00 e Poupança com 0,00; transferência de 20,00 de Azul para Poupança em 29/06/2026.
    private static readonly WalletOpening[] AzulEPoupanca =
    [
        new(Azul, 100000, new DateOnly(2026, 6, 1)),
        new(Poupanca, 0, new DateOnly(2026, 6, 1)),
    ];

    private static readonly TransferTotal[] VinteDeAzulParaPoupanca = [new(June, Azul, Poupanca, 2000)];

    [Fact]
    public void US5_1_com_todas_as_carteiras_a_transferencia_nao_muda_nenhum_total()
    {
        var withTransfer = Calculate(June, AzulEPoupanca, [], VinteDeAzulParaPoupanca);
        var without = Calculate(June, AzulEPoupanca, []);

        withTransfer.ShouldBe(without);
        withTransfer.Planned.TransfersInCents.ShouldBe(0);
        withTransfer.Planned.TransfersOutCents.ShouldBe(0);
        withTransfer.Planned.FinalBalanceCents.ShouldBe(100000);
    }

    [Fact]
    public void US5_2_filtrando_pela_origem_a_transferencia_e_enviada_e_reduz_o_saldo_final()
    {
        var summary = Calculate(June, [AzulEPoupanca[0]], [], VinteDeAzulParaPoupanca);

        foreach (var column in new[] { summary.Planned, summary.Actual })
        {
            column.TransfersOutCents.ShouldBe(2000);
            column.TransfersInCents.ShouldBe(0);
            column.ResultCents.ShouldBe(0);
            column.OpeningBalanceCents.ShouldBe(100000);
            column.FinalBalanceCents.ShouldBe(98000);
        }
    }

    [Fact]
    public void US5_1_filtrando_pelo_destino_a_transferencia_e_recebida()
    {
        var summary = Calculate(June, [AzulEPoupanca[1]], [], VinteDeAzulParaPoupanca);

        summary.Actual.TransfersInCents.ShouldBe(2000);
        summary.Actual.TransfersOutCents.ShouldBe(0);
        summary.Actual.FinalBalanceCents.ShouldBe(2000);
        summary.Actual.OpeningBalanceCents.ShouldBe(0);
    }

    [Fact]
    public void Transferencia_de_mes_anterior_entra_no_saldo_inicial_do_mes_seguinte_sem_aparecer_como_recebida()
    {
        var summary = Calculate(YearMonth.Of(2026, 7), [AzulEPoupanca[0]], [], VinteDeAzulParaPoupanca);

        summary.Actual.OpeningBalanceCents.ShouldBe(98000);
        summary.Actual.TransfersOutCents.ShouldBe(0);
        summary.Actual.FinalBalanceCents.ShouldBe(98000);
    }

    [Fact]
    public void Transferencia_entre_duas_carteiras_selecionadas_se_anula_mesmo_com_filtro_explicito()
    {
        var summary = Calculate(June, AzulEPoupanca, [], VinteDeAzulParaPoupanca);

        summary.Actual.TransfersInCents.ShouldBe(0);
        summary.Actual.TransfersOutCents.ShouldBe(0);
    }

    [Fact]
    public void Pontos_de_extensao_de_faturas_somam_nas_saidas_do_Previsto_e_do_Realizado_respectivamente()
    {
        var summary = MonthSummaryCalculator.Calculate(
            March,
            AzulComSaldoDeMarco,
            ExemploUs3,
            [],
            [new InvoiceAmount(March, 30000)],
            [new InvoiceAmount(March, 10000)]);

        summary.Planned.ExpenseCents.ShouldBe(81502 + 30000);
        summary.Actual.ExpenseCents.ShouldBe(4499 + 10000);
        summary.Planned.FinalBalanceCents.ShouldBe(120325 - 30000);
        summary.Actual.FinalBalanceCents.ShouldBe(195501 - 10000);
    }
}
