using System.Globalization;
using FinMy.Domain.Common;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;
using Dinheiro = FinMy.Domain.Monetary.Money;

namespace FinMy.Domain.Tests.Entries;

/// <summary>Regras de domínio de Entrada/Saída: US2.1, US2.3, US6.1, US6.2, FR-012 e validações.</summary>
public class EntryRulesTests
{
    private static DateOnly DataIso(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static readonly DateOnly Hoje = new(2026, 3, 10);
    private static readonly Guid Azul = Guid.CreateVersion7();

    private static Entry Saida(DateOnly date, EntryStatus? status = null, YearMonth? month = null, string description = "Tim") =>
        Entry.Create(EntryKind.Expense, description, Dinheiro.FromCents(4499), date, Azul, status, month, null, Hoje);

    [Fact]
    public void US2_1_saida_de_hoje_nasce_paga_com_mes_da_data()
    {
        var entry = Saida(Hoje);

        entry.Kind.ShouldBe(EntryKind.Expense);
        entry.AmountCents.ShouldBe(4499);
        entry.EntryDate.ShouldBe(Hoje);
        entry.WalletId.ShouldBe(Azul);
        entry.Status.ShouldBe(EntryStatus.Settled);
        entry.ReferenceMonth.ShouldBe(new DateOnly(2026, 3, 1));
        entry.ReferenceYearMonth.ShouldBe(YearMonth.Of(2026, 3));
    }

    [Fact]
    public void US2_3_data_futura_nasce_prevista()
    {
        Saida(new DateOnly(2026, 3, 25)).Status.ShouldBe(EntryStatus.Planned);
    }

    [Theory]
    [InlineData("2026-03-09", EntryStatus.Settled)]
    [InlineData("2026-03-10", EntryStatus.Settled)]
    [InlineData("2026-03-11", EntryStatus.Planned)]
    public void FR_012_situacao_padrao_compara_a_data_com_hoje(string date, EntryStatus expected)
    {
        Saida(DataIso(date)).Status.ShouldBe(expected);
        Entry.DefaultStatus(DataIso(date), Hoje).ShouldBe(expected);
    }

    [Fact]
    public void FR_012_com_data_igual_a_hoje_a_situacao_e_efetivada()
    {
        Entry.DefaultStatus(Hoje, Hoje).ShouldBe(EntryStatus.Settled);
    }

    [Fact]
    public void Situacao_informada_prevalece_sobre_a_padrao()
    {
        Saida(Hoje, EntryStatus.Planned).Status.ShouldBe(EntryStatus.Planned);
        Saida(new DateOnly(2026, 4, 1), EntryStatus.Settled).Status.ShouldBe(EntryStatus.Settled);
    }

    [Fact]
    public void Mes_de_referencia_informado_prevalece_sobre_o_mes_da_data()
    {
        var entry = Saida(new DateOnly(2026, 2, 25), month: YearMonth.Of(2026, 3));

        entry.ReferenceMonth.ShouldBe(new DateOnly(2026, 3, 1));
    }

    [Fact]
    public void US6_1_mudar_a_data_leva_o_mes_junto_quando_ele_nunca_foi_alterado()
    {
        var entry = Saida(new DateOnly(2026, 3, 2));

        entry.ChangeDate(new DateOnly(2026, 4, 5));

        entry.EntryDate.ShouldBe(new DateOnly(2026, 4, 5));
        entry.ReferenceMonth.ShouldBe(new DateOnly(2026, 4, 1));
    }

    [Fact]
    public void US6_2_mudar_a_data_nao_mexe_no_mes_alterado_manualmente()
    {
        var entry = Saida(new DateOnly(2026, 2, 25), month: YearMonth.Of(2026, 3));

        entry.ChangeDate(new DateOnly(2026, 2, 26));

        entry.EntryDate.ShouldBe(new DateOnly(2026, 2, 26));
        entry.ReferenceMonth.ShouldBe(new DateOnly(2026, 3, 1));
    }

    [Fact]
    public void Mes_alterado_a_mao_e_depois_igual_ao_da_data_antiga_volta_a_acompanhar()
    {
        var entry = Saida(new DateOnly(2026, 2, 25));
        entry.ChangeReferenceMonth(YearMonth.Of(2026, 3));

        entry.ChangeDate(new DateOnly(2026, 3, 3)); // mês antigo da data (02) ≠ 03: não acompanha
        entry.ReferenceMonth.ShouldBe(new DateOnly(2026, 3, 1));

        entry.ChangeDate(new DateOnly(2026, 5, 3)); // agora data antiga (03) = mês de referência (03): acompanha
        entry.ReferenceMonth.ShouldBe(new DateOnly(2026, 5, 1));
    }

    [Fact]
    public void MoveToMonth_muda_so_o_mes_de_referencia()
    {
        var entry = Saida(new DateOnly(2026, 1, 20), EntryStatus.Planned);

        entry.MoveToMonth(YearMonth.Of(2026, 3));

        entry.EntryDate.ShouldBe(new DateOnly(2026, 1, 20));
        entry.ReferenceMonth.ShouldBe(new DateOnly(2026, 3, 1));
        entry.Status.ShouldBe(EntryStatus.Planned);
    }

    [Fact]
    public void ToggleStatus_alterna_entre_prevista_e_efetivada()
    {
        var entry = Saida(Hoje);

        entry.ToggleStatus();
        entry.Status.ShouldBe(EntryStatus.Planned);

        entry.ToggleStatus();
        entry.Status.ShouldBe(EntryStatus.Settled);
    }

    [Fact]
    public void Descricao_e_aparada_e_normalizada()
    {
        var entry = Saida(Hoje, description: "  Açaí do Léo  ");

        entry.Description.ShouldBe("Açaí do Léo");
        entry.DescriptionNormalized.ShouldBe("acai do leo");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Descricao_vazia_e_recusada(string description)
    {
        var ex = Should.Throw<DomainException>(() => Saida(Hoje, description: description));

        ex.Error.Code.ShouldBe("description.required");
        ex.Error.Kind.ShouldBe(DomainErrorKind.Validation);
        ex.Error.FieldErrors!.ShouldContainKey("description");
    }

    [Fact]
    public void Descricao_aceita_80_e_recusa_81_caracteres()
    {
        Saida(Hoje, description: new string('a', 80)).Description.Length.ShouldBe(80);

        Should.Throw<DomainException>(() => Saida(Hoje, description: new string('a', 81)))
            .Error.Code.ShouldBe("description.too_long");
    }

    [Fact]
    public void Observacao_aceita_500_recusa_501_e_vazia_vira_nula()
    {
        Entry Com(string? notes) => Entry.Create(EntryKind.Income, "Salário", Dinheiro.FromCents(100), Hoje, Azul, null, null, notes, Hoje);

        Com(new string('x', 500)).Notes!.Length.ShouldBe(500);
        Com("  ").Notes.ShouldBeNull();
        Should.Throw<DomainException>(() => Com(new string('x', 501))).Error.Code.ShouldBe("notes.too_long");
    }

    [Theory]
    [InlineData(0, "amount.must_be_positive")]
    [InlineData(-5, "amount.must_be_positive")]
    [InlineData(1_000_000_000, "amount.too_large")]
    public void Valor_fora_do_intervalo_e_recusado(long cents, string code)
    {
        var ex = Should.Throw<DomainException>(() =>
            Entry.Create(EntryKind.Expense, "Tim", Dinheiro.FromCents(cents), Hoje, Azul, null, null, null, Hoje));

        ex.Error.Code.ShouldBe(code);
    }

    [Fact]
    public void Valor_limite_e_aceito()
    {
        Entry.Create(EntryKind.Expense, "Tim", Dinheiro.FromCents(1), Hoje, Azul, null, null, null, Hoje).AmountCents.ShouldBe(1);
        Entry.Create(EntryKind.Expense, "Tim", Dinheiro.FromCents(999_999_999), Hoje, Azul, null, null, null, Hoje)
            .AmountCents.ShouldBe(999_999_999);
    }

    [Theory]
    [InlineData("1999-12-31")]
    [InlineData("2101-01-01")]
    public void Data_fora_do_intervalo_da_RT_03_e_recusada(string date)
    {
        Should.Throw<DomainException>(() => Saida(DataIso(date))).Error.Code.ShouldBe("date.out_of_range");
    }

    [Fact]
    public void Datas_limite_da_RT_03_sao_aceitas()
    {
        Saida(new DateOnly(2000, 1, 1)).EntryDate.ShouldBe(new DateOnly(2000, 1, 1));
        Saida(new DateOnly(2100, 12, 31)).ReferenceMonth.ShouldBe(new DateOnly(2100, 12, 1));
    }

    [Fact]
    public void ChangeDate_fora_do_intervalo_e_recusada_sem_alterar_o_lancamento()
    {
        var entry = Saida(new DateOnly(2026, 3, 2));

        Should.Throw<DomainException>(() => entry.ChangeDate(new DateOnly(2101, 1, 1)));

        entry.EntryDate.ShouldBe(new DateOnly(2026, 3, 2));
        entry.ReferenceMonth.ShouldBe(new DateOnly(2026, 3, 1));
    }

    [Fact]
    public void Mes_de_referencia_fora_do_intervalo_e_recusado()
    {
        var entry = Saida(Hoje);

        Should.Throw<DomainException>(() => entry.ChangeReferenceMonth(YearMonth.Of(1999, 12)))
            .Error.Code.ShouldBe("date.out_of_range");
        Should.Throw<DomainException>(() => entry.MoveToMonth(YearMonth.Of(2101, 1)))
            .Error.Code.ShouldBe("date.out_of_range");
    }
}
