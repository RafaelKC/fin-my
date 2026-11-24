using FinMy.Domain.Common;
using FinMy.Domain.Monetary;
using FinMy.Domain.Recurrences;
using Dinheiro = FinMy.Domain.Monetary.Money;

namespace FinMy.Domain.Tests.Recurrences;

public class RecurrenceSchedulerTests
{
    private static readonly Guid Carteira = Guid.CreateVersion7();
    private static readonly Guid Cartao = Guid.CreateVersion7();

    private static Recurrence Aluguel(
        YearMonth start,
        int dia = 5,
        RecurrenceEndType fim = RecurrenceEndType.None,
        YearMonth? fimMes = null,
        int? limite = null,
        ReferenceMonthRule regra = ReferenceMonthRule.SameMonth) =>
        Recurrence.Create(
            RecurrenceKind.Expense, "Aluguel", new Dinheiro(150_000), dia, Carteira, null, null, start, fim, fimMes, limite, regra);

    [Fact]
    public void Sem_fim_gera_ate_dezembro_do_ano_seguinte_US1_1()
    {
        var rec = Aluguel(YearMonth.Of(2026, 3));

        var ocorrencias = RecurrenceScheduler.Occurrences(rec, null, new DateOnly(2026, 3, 10));

        ocorrencias.Count.ShouldBe(22);
        ocorrencias[0].Month.ShouldBe(YearMonth.Of(2026, 3));
        ocorrencias[^1].Month.ShouldBe(YearMonth.Of(2027, 12));
        ocorrencias[0].Date.ShouldBe(new DateOnly(2026, 3, 5));
    }

    [Fact]
    public void Continua_depois_do_ultimo_mes_gerado_e_na_virada_do_ano_estende()
    {
        var rec = Aluguel(YearMonth.Of(2026, 3));

        var ocorrencias = RecurrenceScheduler.Occurrences(rec, YearMonth.Of(2027, 12), new DateOnly(2026, 12, 31));
        ocorrencias.ShouldBeEmpty();

        var virada = RecurrenceScheduler.Occurrences(rec, YearMonth.Of(2027, 12), new DateOnly(2028, 1, 1));
        virada.Count.ShouldBe(24);
        virada[0].Month.ShouldBe(YearMonth.Of(2028, 1));
        virada[^1].Month.ShouldBe(YearMonth.Of(2029, 12));
    }

    [Fact]
    public void Mes_ja_gerado_antes_do_inicio_nao_antecipa_o_inicio()
    {
        var rec = Aluguel(YearMonth.Of(2026, 3));

        var ocorrencias = RecurrenceScheduler.Occurrences(rec, YearMonth.Of(2025, 1), new DateOnly(2026, 3, 1));

        ocorrencias[0].Month.ShouldBe(YearMonth.Of(2026, 3));
    }

    [Fact]
    public void Inicio_no_passado_gera_desde_o_inicio_US1_5()
    {
        var rec = Aluguel(YearMonth.Of(2026, 1));

        var ocorrencias = RecurrenceScheduler.Occurrences(rec, null, new DateOnly(2026, 4, 20));

        ocorrencias[0].Month.ShouldBe(YearMonth.Of(2026, 1));
        ocorrencias.Count.ShouldBe(24);
    }

    [Fact]
    public void Dia_31_vira_o_ultimo_dia_do_mes_curto_US1_4()
    {
        var rec = Aluguel(YearMonth.Of(2027, 1), dia: 31, fim: RecurrenceEndType.UntilMonth, fimMes: YearMonth.Of(2028, 2));

        var datas = RecurrenceScheduler.Occurrences(rec, null, new DateOnly(2027, 1, 1)).Select(o => o.Date).ToList();

        datas[0].ShouldBe(new DateOnly(2027, 1, 31));
        datas[1].ShouldBe(new DateOnly(2027, 2, 28));
        datas[3].ShouldBe(new DateOnly(2027, 4, 30));
        datas[^1].ShouldBe(new DateOnly(2028, 2, 29));
    }

    [Fact]
    public void Ate_um_mes_para_no_mes_final()
    {
        var rec = Aluguel(YearMonth.Of(2026, 3), fim: RecurrenceEndType.UntilMonth, fimMes: YearMonth.Of(2026, 8));

        var ocorrencias = RecurrenceScheduler.Occurrences(rec, null, new DateOnly(2026, 3, 1));

        ocorrencias.Count.ShouldBe(6);
        ocorrencias[^1].Month.ShouldBe(YearMonth.Of(2026, 8));
    }

    [Fact]
    public void Apos_N_ocorrencias_grava_o_mes_final_e_gera_exatamente_N_US1_6()
    {
        var rec = Aluguel(YearMonth.Of(2026, 11), fim: RecurrenceEndType.AfterCount, limite: 4);

        rec.EndYearMonth.ShouldBe(YearMonth.Of(2027, 2));
        rec.OccurrenceLimit.ShouldBe(4);
        RecurrenceScheduler.Occurrences(rec, null, new DateOnly(2026, 11, 1)).Count.ShouldBe(4);
    }

    [Fact]
    public void Apos_N_maior_que_o_horizonte_limita_pelo_horizonte()
    {
        var rec = Aluguel(YearMonth.Of(2026, 3), fim: RecurrenceEndType.AfterCount, limite: 60);

        var ocorrencias = RecurrenceScheduler.Occurrences(rec, null, new DateOnly(2026, 3, 1));

        ocorrencias.Count.ShouldBe(22);
        rec.EndYearMonth.ShouldBe(YearMonth.Of(2031, 2));
    }

    [Fact]
    public void Regra_do_mes_de_referencia_vale_no_mes_da_data_ou_no_seguinte_US2_1()
    {
        var mesmo = Aluguel(YearMonth.Of(2026, 12), fim: RecurrenceEndType.AfterCount, limite: 1);
        var seguinte = Aluguel(YearMonth.Of(2026, 12), fim: RecurrenceEndType.AfterCount, limite: 1, regra: ReferenceMonthRule.NextMonth);
        var hoje = new DateOnly(2026, 12, 1);

        RecurrenceScheduler.Occurrences(mesmo, null, hoje)[0].ReferenceMonth.ShouldBe(YearMonth.Of(2026, 12));
        RecurrenceScheduler.Occurrences(seguinte, null, hoje)[0].ReferenceMonth.ShouldBe(YearMonth.Of(2027, 1));
        RecurrenceScheduler.Occurrences(seguinte, null, hoje)[0].InvoiceDueMonth.ShouldBeNull();
    }

    [Fact]
    public void Compra_no_cartao_usa_a_fatura_do_ciclo_do_cartao()
    {
        var rec = Recurrence.Create(
            RecurrenceKind.CardPurchase, "Streaming", new Dinheiro(3_990), 20, null, Cartao, null,
            YearMonth.Of(2026, 3), RecurrenceEndType.AfterCount, null, 2, null);

        // Fecha dia 5, vence dia 15: compra no dia 20 entra na fatura do mês seguinte.
        var ocorrencias = RecurrenceScheduler.Occurrences(rec, null, new DateOnly(2026, 3, 1), (5, 15));

        ocorrencias.Select(o => o.InvoiceDueMonth).ShouldBe([YearMonth.Of(2026, 4), YearMonth.Of(2026, 5)]);
        ocorrencias.ShouldAllBe(o => o.ReferenceMonth == null);
    }

    [Fact]
    public void Compra_no_cartao_exige_os_dias_do_cartao()
    {
        var rec = Recurrence.Create(
            RecurrenceKind.CardPurchase, "Streaming", new Dinheiro(3_990), 20, null, Cartao, null,
            YearMonth.Of(2026, 3), RecurrenceEndType.None, null, null, null);

        Should.Throw<ArgumentException>(() => RecurrenceScheduler.Occurrences(rec, null, new DateOnly(2026, 3, 1)));
    }

    [Fact]
    public void Horizonte_nao_passa_de_dezembro_de_2100()
    {
        RecurrenceScheduler.Horizon(new DateOnly(2100, 6, 1)).ShouldBe(YearMonth.Of(2100, 12));
    }
}

public class RecurrenceTests
{
    private static readonly Guid Carteira = Guid.CreateVersion7();
    private static readonly Guid Cartao = Guid.CreateVersion7();

    private static Recurrence Salario() =>
        Recurrence.Create(
            RecurrenceKind.Income, "  Salário Léo ", new Dinheiro(500_000), 5, Carteira, null, null,
            YearMonth.Of(2026, 3), RecurrenceEndType.None, null, null, ReferenceMonthRule.SameMonth);

    [Fact]
    public void Cria_com_descricao_aparada_e_normalizada()
    {
        var rec = Salario();

        rec.Description.ShouldBe("Salário Léo");
        rec.DescriptionNormalized.ShouldBe("salario leo");
        rec.StartMonth.ShouldBe(new DateOnly(2026, 3, 1));
        rec.EndMonth.ShouldBeNull();
        rec.GeneratedUntil.ShouldBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    public void Dia_fora_de_1_a_31_e_recusado(int dia)
    {
        var ex = Should.Throw<DomainException>(() => Recurrence.Create(
            RecurrenceKind.Income, "Salário", new Dinheiro(100), dia, Carteira, null, null,
            YearMonth.Of(2026, 3), RecurrenceEndType.None, null, null, ReferenceMonthRule.SameMonth));

        ex.Error.Code.ShouldBe("recurrence.day_invalid");
    }

    [Fact]
    public void Valor_zero_e_recusado()
    {
        Should.Throw<DomainException>(() => Recurrence.Create(
            RecurrenceKind.Income, "Salário", new Dinheiro(0), 5, Carteira, null, null,
            YearMonth.Of(2026, 3), RecurrenceEndType.None, null, null, ReferenceMonthRule.SameMonth));
    }

    [Fact]
    public void Entrada_ou_saida_exige_carteira_e_regra_e_nao_aceita_cartao()
    {
        Codigo(() => Recurrence.Create(RecurrenceKind.Expense, "Aluguel", new Dinheiro(100), 5, null, null, null, YearMonth.Of(2026, 3), RecurrenceEndType.None, null, null, ReferenceMonthRule.SameMonth))
            .ShouldBe("recurrence.wallet_required");
        Codigo(() => Recurrence.Create(RecurrenceKind.Expense, "Aluguel", new Dinheiro(100), 5, Carteira, Cartao, null, YearMonth.Of(2026, 3), RecurrenceEndType.None, null, null, ReferenceMonthRule.SameMonth))
            .ShouldBe("recurrence.card_not_allowed");
        Codigo(() => Recurrence.Create(RecurrenceKind.Expense, "Aluguel", new Dinheiro(100), 5, Carteira, null, null, YearMonth.Of(2026, 3), RecurrenceEndType.None, null, null, null))
            .ShouldBe("recurrence.reference_rule_required");
    }

    [Fact]
    public void Compra_exige_cartao_e_nao_aceita_carteira_nem_regra()
    {
        Codigo(() => Recurrence.Create(RecurrenceKind.CardPurchase, "Streaming", new Dinheiro(100), 5, null, null, null, YearMonth.Of(2026, 3), RecurrenceEndType.None, null, null, null))
            .ShouldBe("recurrence.card_required");
        Codigo(() => Recurrence.Create(RecurrenceKind.CardPurchase, "Streaming", new Dinheiro(100), 5, Carteira, Cartao, null, YearMonth.Of(2026, 3), RecurrenceEndType.None, null, null, null))
            .ShouldBe("recurrence.wallet_not_allowed");
        Codigo(() => Recurrence.Create(RecurrenceKind.CardPurchase, "Streaming", new Dinheiro(100), 5, null, Cartao, null, YearMonth.Of(2026, 3), RecurrenceEndType.None, null, null, ReferenceMonthRule.SameMonth))
            .ShouldBe("recurrence.reference_rule_not_allowed");
    }

    [Fact]
    public void Termino_valida_mes_final_e_limite()
    {
        Codigo(() => Recurrence.Create(RecurrenceKind.Income, "Salário", new Dinheiro(100), 5, Carteira, null, null, YearMonth.Of(2026, 3), RecurrenceEndType.UntilMonth, null, null, ReferenceMonthRule.SameMonth))
            .ShouldBe("recurrence.end_month_required");
        Codigo(() => Recurrence.Create(RecurrenceKind.Income, "Salário", new Dinheiro(100), 5, Carteira, null, null, YearMonth.Of(2026, 3), RecurrenceEndType.UntilMonth, YearMonth.Of(2026, 2), null, ReferenceMonthRule.SameMonth))
            .ShouldBe("recurrence.end_before_start");
        Codigo(() => Recurrence.Create(RecurrenceKind.Income, "Salário", new Dinheiro(100), 5, Carteira, null, null, YearMonth.Of(2026, 3), RecurrenceEndType.AfterCount, null, 0, ReferenceMonthRule.SameMonth))
            .ShouldBe("recurrence.limit_invalid");
        Codigo(() => Recurrence.Create(RecurrenceKind.Income, "Salário", new Dinheiro(100), 5, Carteira, null, null, YearMonth.Of(2026, 3), RecurrenceEndType.AfterCount, null, 361, ReferenceMonthRule.SameMonth))
            .ShouldBe("recurrence.limit_invalid");
    }

    [Fact]
    public void Encerrar_define_o_mes_anterior_como_ultimo()
    {
        var rec = Salario();

        rec.End(YearMonth.Of(2026, 9));

        rec.EndType.ShouldBe(RecurrenceEndType.UntilMonth);
        rec.EndYearMonth.ShouldBe(YearMonth.Of(2026, 8));
        rec.OccurrenceLimit.ShouldBeNull();
    }

    [Fact]
    public void Encerrar_no_mes_inicial_ou_antes_e_recusado()
    {
        Should.Throw<DomainException>(() => Salario().End(YearMonth.Of(2026, 3)));
    }

    [Fact]
    public void Alterar_a_partir_de_troca_o_modelo_sem_mudar_tipo_nem_inicio()
    {
        var rec = Salario();

        rec.ChangeFrom("Salário novo", new Dinheiro(600_000), 10, Carteira, null, null, RecurrenceEndType.AfterCount, null, 12, ReferenceMonthRule.NextMonth);

        rec.Description.ShouldBe("Salário novo");
        rec.AmountCents.ShouldBe(600_000);
        rec.DayOfMonth.ShouldBe(10);
        rec.EndYearMonth.ShouldBe(YearMonth.Of(2027, 2));
        rec.ReferenceMonthRule.ShouldBe(ReferenceMonthRule.NextMonth);
        rec.Kind.ShouldBe(RecurrenceKind.Income);
        rec.StartYearMonth.ShouldBe(YearMonth.Of(2026, 3));
    }

    [Fact]
    public void Ultimo_mes_gerado_so_avanca()
    {
        var rec = Salario();

        rec.AdvanceGeneratedUntil(YearMonth.Of(2027, 12));
        rec.AdvanceGeneratedUntil(YearMonth.Of(2026, 5));

        rec.GeneratedUntil.ShouldBe(YearMonth.Of(2027, 12));
    }

    private static string Codigo(Action acao) => Should.Throw<DomainException>(acao).Error.Code;
}
