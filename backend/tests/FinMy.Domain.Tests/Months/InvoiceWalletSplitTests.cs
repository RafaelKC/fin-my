using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;
using FinMy.Domain.Months;

namespace FinMy.Domain.Tests.Months;

/// <summary>
/// Faturas no resumo do mês (003 FR-022/FR-023, research R8). SC-004: para qualquer fatura, a soma das partes por Carteira
/// é igual ao valor da fatura nas Saídas de todas as Carteiras.
/// </summary>
public class InvoiceWalletSplitTests
{
    private static readonly YearMonth March = YearMonth.Of(2026, 3);
    private static readonly YearMonth April = YearMonth.Of(2026, 4);

    private static readonly Guid Azul = Guid.NewGuid();
    private static readonly Guid Verde = Guid.NewGuid();
    private static readonly Guid Poupanca = Guid.NewGuid();
    private static readonly Guid[] AllWallets = [Azul, Verde, Poupanca];

    private static InvoiceFigure Invoice(long total, params (Guid Wallet, long Cents)[] paid) =>
        new(March, total, Azul, paid.ToDictionary(p => p.Wallet, p => p.Cents));

    private static HashSet<Guid> Only(Guid wallet) => [wallet];

    public static TheoryData<long, long, long, long> Cases => new()
    {
        { 20000, 0, 0, 0 },           // fechada, nada pago
        { 20000, 20000, 0, 0 },       // paga inteira com a carteira de pagamento
        { 20000, 15000, 0, 0 },       // parcial (US5.2)
        { 20000, 5000, 10000, 2000 }, // pagamentos com carteiras diferentes e restante
        { 20000, 12000, 8000, 0 },    // paga repartida entre duas carteiras
        { 20000, 0, 20000, 5000 },    // pago a mais (total reduzido depois)
        { 0, 0, 3000, 0 },            // total zerado depois de pago
        { -5000, 0, 0, 0 },           // só créditos (estorno)
        { -5000, 0, 0, 1000 },        // créditos com pagamento
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void SC_004_soma_das_partes_por_carteira_e_igual_ao_valor_sem_filtro(long total, long paidAzul, long paidVerde, long paidPoupanca)
    {
        var invoice = Invoice(total, (Azul, paidAzul), (Verde, paidVerde), (Poupanca, paidPoupanca));
        var all = AllWallets.ToHashSet();

        var expected = Math.Max(Math.Max(total, paidAzul + paidVerde + paidPoupanca), 0);
        invoice.Planned(all).ShouldBe(expected);
        invoice.Actual(all).ShouldBe(paidAzul + paidVerde + paidPoupanca);
        AllWallets.Sum(w => invoice.Planned(Only(w))).ShouldBe(invoice.Planned(all));
        AllWallets.Sum(w => invoice.Actual(Only(w))).ShouldBe(invoice.Actual(all));
    }

    [Fact]
    public void FR_023_parte_prevista_e_pagos_com_a_carteira_mais_o_restante_na_carteira_de_pagamento()
    {
        var invoice = Invoice(20000, (Verde, 5000), (Azul, 10000));

        invoice.Planned(Only(Azul)).ShouldBe(10000 + 5000);
        invoice.Actual(Only(Azul)).ShouldBe(10000);
        invoice.Planned(Only(Verde)).ShouldBe(5000);
        invoice.Actual(Only(Verde)).ShouldBe(5000);
        invoice.Planned(Only(Poupanca)).ShouldBe(0);
        invoice.Actual(Only(Poupanca)).ShouldBe(0);
    }

    [Fact]
    public void Restante_negativo_nao_conta_como_previsto_extra()
    {
        var invoice = Invoice(5000, (Azul, 8000));

        invoice.Planned(Only(Azul)).ShouldBe(8000);
        invoice.Planned(new HashSet<Guid> { Azul, Verde }).ShouldBe(8000);
    }

    [Fact]
    public void Resumo_do_mes_soma_so_a_parte_das_carteiras_selecionadas_nas_saidas()
    {
        var wallets = AllWallets.Select(w => new WalletOpening(w, 0, new DateOnly(2026, 1, 1))).ToList();
        var invoices = new[] { Invoice(20000, (Verde, 5000)) };

        var azul = MonthSummaryCalculator.Calculate(March, wallets.Where(w => w.WalletId == Azul).ToList(), [], [], invoices);
        var verde = MonthSummaryCalculator.Calculate(March, wallets.Where(w => w.WalletId == Verde).ToList(), [], [], invoices);
        var todas = MonthSummaryCalculator.Calculate(March, wallets, [], [], invoices);

        azul.Planned.ExpenseCents.ShouldBe(15000);
        azul.Actual.ExpenseCents.ShouldBe(0);
        verde.Planned.ExpenseCents.ShouldBe(5000);
        verde.Actual.ExpenseCents.ShouldBe(5000);
        todas.Planned.ExpenseCents.ShouldBe(20000);
        todas.Actual.ExpenseCents.ShouldBe(5000);
        (azul.Planned.ExpenseCents + verde.Planned.ExpenseCents).ShouldBe(todas.Planned.ExpenseCents);
    }

    [Fact]
    public void Faturas_somam_nas_saidas_do_Previsto_e_do_Realizado_e_as_de_meses_futuros_sao_ignoradas()
    {
        var wallets = new[] { new WalletOpening(Azul, 50000, new DateOnly(2026, 3, 1)) };
        var entries = new[] { new EntryTotal(March, EntryKind.Expense, EntryStatus.Settled, 4499) };
        var invoices = new[]
        {
            Invoice(30000, (Azul, 10000)),
            new InvoiceFigure(April, 99999, Azul, new Dictionary<Guid, long>()),
        };

        var summary = MonthSummaryCalculator.Calculate(March, wallets, entries, [], invoices);

        summary.Planned.ExpenseCents.ShouldBe(4499 + 30000);
        summary.Actual.ExpenseCents.ShouldBe(4499 + 10000);
        summary.Planned.FinalBalanceCents.ShouldBe(50000 - 4499 - 30000);
        summary.Actual.FinalBalanceCents.ShouldBe(50000 - 4499 - 10000);
    }

    [Fact]
    public void Fatura_de_mes_anterior_com_restante_pesa_no_saldo_do_mes_seguinte_mas_nao_nas_saidas_dele()
    {
        var wallets = new[] { new WalletOpening(Azul, 50000, new DateOnly(2026, 1, 1)) };
        var invoices = new[] { Invoice(20000, (Azul, 15000)) };

        var april = MonthSummaryCalculator.Calculate(April, wallets, [], [], invoices);

        april.Planned.ExpenseCents.ShouldBe(0);
        april.Planned.OpeningBalanceCents.ShouldBe(30000);
        april.Actual.OpeningBalanceCents.ShouldBe(35000);
    }
}
