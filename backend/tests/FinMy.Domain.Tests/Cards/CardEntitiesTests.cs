using FinMy.Domain.Cards;
using FinMy.Domain.Common;
using FinMy.Domain.Monetary;
using Dinheiro = FinMy.Domain.Monetary.Money;

namespace FinMy.Domain.Tests.Cards;

public class CardEntitiesTests
{
    private static readonly DateOnly Hoje = new(2026, 3, 10);
    private static readonly Guid Carteira = Guid.CreateVersion7();

    private static CreditCard Azul(Dinheiro? limite = null) => CreditCard.Create("Azul", "#1E88E5", 5, 15, Carteira, limite);

    [Fact]
    public void Cartao_e_criado_com_nome_aparado_normalizado_e_sem_limite_por_padrao()
    {
        var card = CreditCard.Create("  Cartão Açaí ", "#43a047", 5, 15, Carteira);

        card.Name.ShouldBe("Cartão Açaí");
        card.NameNormalized.ShouldBe("cartao acai");
        card.ClosingDay.ShouldBe(5);
        card.DueDay.ShouldBe(15);
        card.PaymentWalletId.ShouldBe(Carteira);
        card.LimitCents.ShouldBeNull();
        card.IsArchived.ShouldBeFalse();
    }

    [Theory]
    [InlineData(0, 15, "credit_card.closing_day_invalid")]
    [InlineData(32, 15, "credit_card.closing_day_invalid")]
    [InlineData(5, 0, "credit_card.due_day_invalid")]
    [InlineData(5, 32, "credit_card.due_day_invalid")]
    public void Dias_fora_de_1_a_31_sao_recusados(int closing, int due, string code)
    {
        var ex = Should.Throw<DomainException>(() => CreditCard.Create("Azul", "#1E88E5", closing, due, Carteira));

        ex.Error.Code.ShouldBe(code);
    }

    [Theory]
    [InlineData(-1, "credit_card.limit_invalid")]
    [InlineData(1_000_000_000, "amount.too_large")]
    public void Limite_fora_de_0_a_9_999_999_99_e_recusado(long cents, string code)
    {
        var ex = Should.Throw<DomainException>(() => Azul(Dinheiro.FromCents(cents)));

        ex.Error.Code.ShouldBe(code);
    }

    [Fact]
    public void Limite_zero_e_aceito_e_pode_ser_removido()
    {
        var card = Azul(Dinheiro.Zero);
        card.LimitCents.ShouldBe(0);

        card.ChangeLimit(null);

        card.LimitCents.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("azul")]
    [InlineData("#12345")]
    public void Cor_invalida_e_recusada(string color)
    {
        Should.Throw<DomainException>(() => CreditCard.Create("Azul", color, 5, 15, Carteira))
            .Error.Code.ShouldBe("credit_card.color_invalid");
    }

    [Fact]
    public void Arquivar_e_desarquivar_mantem_a_primeira_data()
    {
        var card = Azul();
        var antes = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

        card.Archive(antes);
        card.Archive(antes.AddDays(5));
        card.ArchivedAt.ShouldBe(antes);
        card.IsArchived.ShouldBeTrue();

        card.Unarchive();
        card.IsArchived.ShouldBeFalse();
    }

    [Fact]
    public void Fatura_guarda_o_mes_de_vencimento_e_as_datas()
    {
        var invoice = Invoice.Create(Guid.CreateVersion7(), YearMonth.Of(2026, 3), new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 15));

        invoice.DueMonth.ShouldBe(new DateOnly(2026, 3, 1));
        invoice.ClosingDate.ShouldBe(new DateOnly(2026, 3, 5));
        invoice.DueDate.ShouldBe(new DateOnly(2026, 3, 15));
    }

    [Fact]
    public void Compra_a_vista_tem_origem_manual_e_descricao_sem_parcela()
    {
        var purchase = CardPurchase.CreateManual(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "  Mercado ", Dinheiro.FromCents(4_500), PurchaseNature.Debit, Hoje, null);

        purchase.Origin.ShouldBe(PurchaseOrigin.Manual);
        purchase.Description.ShouldBe("Mercado");
        purchase.DescriptionNormalized.ShouldBe("mercado");
        purchase.DisplayDescription.ShouldBe("Mercado");
        purchase.Reconciled.ShouldBeFalse();
        purchase.ManuallyMoved.ShouldBeFalse();
        purchase.InstallmentPlanId.ShouldBeNull();
    }

    [Fact]
    public void Parcela_acrescenta_k_de_N_na_descricao_de_exibicao()
    {
        var plan = InstallmentPlan.Create(
            Guid.CreateVersion7(), "Geladeira", InstallmentMode.TotalAmount, 10, 1, Dinheiro.FromCents(100_000), Hoje, YearMonth.Of(2026, 4));

        var parcela = CardPurchase.CreateInstallment(plan.CreditCardId, Guid.CreateVersion7(), plan, 3, Dinheiro.FromCents(10_000), null);

        parcela.Origin.ShouldBe(PurchaseOrigin.Installment);
        parcela.Nature.ShouldBe(PurchaseNature.Debit);
        parcela.InstallmentPlanId.ShouldBe(plan.Id);
        parcela.DisplayDescription.ShouldBe("Geladeira 3/10");
        parcela.PurchaseDate.ShouldBe(Hoje);
    }

    [Fact]
    public void Plano_guarda_o_valor_conforme_o_modo_e_ignora_primeira_parcela_fora_do_andamento()
    {
        var total = InstallmentPlan.Create(Guid.CreateVersion7(), "TV", InstallmentMode.TotalAmount, 3, 2, Dinheiro.FromCents(30_000), Hoje, YearMonth.Of(2026, 4));
        var andamento = InstallmentPlan.Create(Guid.CreateVersion7(), "TV", InstallmentMode.InProgress, 10, 4, Dinheiro.FromCents(9_000), Hoje, YearMonth.Of(2026, 4));

        total.TotalAmountCents.ShouldBe(30_000);
        total.InstallmentAmountCents.ShouldBeNull();
        total.FirstNumber.ShouldBe(1);
        andamento.TotalAmountCents.ShouldBeNull();
        andamento.InstallmentAmountCents.ShouldBe(9_000);
        andamento.FirstNumber.ShouldBe(4);
    }

    [Theory]
    [InlineData(0, 1, "installment.total_invalid")]
    [InlineData(49, 1, "installment.total_invalid")]
    [InlineData(10, 0, "installment.first_number_invalid")]
    [InlineData(10, 11, "installment.first_number_invalid")]
    public void Plano_recusa_total_e_proxima_parcela_fora_do_intervalo(int total, int first, string code)
    {
        Should.Throw<DomainException>(() => InstallmentPlan.Create(
                Guid.CreateVersion7(), "TV", InstallmentMode.InProgress, total, first, Dinheiro.FromCents(9_000), Hoje, YearMonth.Of(2026, 4)))
            .Error.Code.ShouldBe(code);
    }

    [Fact]
    public void Mover_para_outra_fatura_marca_como_movida_a_mao_mas_redistribuir_nao()
    {
        var purchase = CardPurchase.CreateManual(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "Mercado", Dinheiro.FromCents(100), PurchaseNature.Debit, Hoje, null);

        purchase.Redistribute(Guid.CreateVersion7());
        purchase.ManuallyMoved.ShouldBeFalse();

        purchase.MoveToInvoice(Guid.CreateVersion7());
        purchase.ManuallyMoved.ShouldBeTrue();
    }

    [Fact]
    public void Par_de_saldo_levado_compartilha_o_identificador()
    {
        var par = Guid.CreateVersion7();

        var credito = CardPurchase.CreateCarryOver(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "Saldo levado", Dinheiro.FromCents(500), PurchaseNature.Credit, Hoje, par);

        credito.Origin.ShouldBe(PurchaseOrigin.CarryOver);
        credito.CarryOverPairId.ShouldBe(par);
    }

    [Fact]
    public void Pagamento_exige_valor_positivo_e_data_no_intervalo()
    {
        var pagamento = InvoicePayment.Create(Guid.CreateVersion7(), Carteira, Dinheiro.FromCents(1_000), Hoje);
        pagamento.AmountCents.ShouldBe(1_000);

        Should.Throw<DomainException>(() => InvoicePayment.Create(Guid.CreateVersion7(), Carteira, Dinheiro.Zero, Hoje));
        Should.Throw<DomainException>(() => InvoicePayment.Create(Guid.CreateVersion7(), Carteira, Dinheiro.FromCents(1), new DateOnly(1999, 12, 31)))
            .Error.Code.ShouldBe("date.out_of_range");
    }
}
