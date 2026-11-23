using System.Globalization;
using FinMy.Domain.Common;
using FinMy.Domain.Monetary;
using FinMy.Domain.Wallets;
using Dinheiro = FinMy.Domain.Monetary.Money;

namespace FinMy.Domain.Tests.Wallets;

public class WalletRulesTests
{
    private static DateOnly DataIso(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static readonly DateOnly Hoje = new(2026, 3, 10);
    private static readonly DateTimeOffset Agora = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    private static Wallet Azul(Dinheiro? saldo = null, bool principal = false) =>
        Wallet.Create("Azul", WalletType.Checking, "#1E88E5", saldo ?? Dinheiro.Zero, Hoje, principal);

    [Fact]
    public void Cria_com_nome_aparado_e_normalizado()
    {
        var wallet = Wallet.Create("  Cartão Verde ", WalletType.Digital, "#43a047", Dinheiro.FromCents(50_000), Hoje);

        wallet.Name.ShouldBe("Cartão Verde");
        wallet.NameNormalized.ShouldBe("cartao verde");
        wallet.Type.ShouldBe(WalletType.Digital);
        wallet.InitialBalanceCents.ShouldBe(50_000);
        wallet.InitialBalanceDate.ShouldBe(Hoje);
        wallet.IsPrimary.ShouldBeFalse();
        wallet.IsArchived.ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-12_345)]
    [InlineData(999_999_999)]
    [InlineData(-999_999_999)]
    public void Saldo_inicial_aceita_zero_negativo_e_o_limite_em_modulo(long cents)
    {
        Azul(Dinheiro.FromCents(cents)).InitialBalanceCents.ShouldBe(cents);
    }

    [Theory]
    [InlineData(1_000_000_000)]
    [InlineData(-1_000_000_000)]
    public void Saldo_inicial_acima_do_limite_e_recusado(long cents)
    {
        Should.Throw<DomainException>(() => Azul(Dinheiro.FromCents(cents))).Error.Code.ShouldBe("amount.too_large");
    }

    [Fact]
    public void Nome_aceita_40_e_recusa_41_caracteres_e_vazio()
    {
        Wallet.Create(new string('a', 40), WalletType.Cash, "#000000", Dinheiro.Zero, Hoje).Name.Length.ShouldBe(40);

        Should.Throw<DomainException>(() => Wallet.Create(new string('a', 41), WalletType.Cash, "#000000", Dinheiro.Zero, Hoje))
            .Error.Code.ShouldBe("wallet.name.too_long");
        Should.Throw<DomainException>(() => Wallet.Create("  ", WalletType.Cash, "#000000", Dinheiro.Zero, Hoje))
            .Error.Code.ShouldBe("wallet.name.required");
    }

    [Theory]
    [InlineData("1E88E5")]
    [InlineData("#1E88E")]
    [InlineData("#GGGGGG")]
    [InlineData("")]
    public void Cor_fora_do_formato_e_recusada(string color)
    {
        Should.Throw<DomainException>(() => Wallet.Create("Azul", WalletType.Checking, color, Dinheiro.Zero, Hoje))
            .Error.Code.ShouldBe("wallet.color_invalid");
    }

    [Theory]
    [InlineData("1999-12-31")]
    [InlineData("2101-01-01")]
    public void Data_do_saldo_inicial_fora_do_intervalo_e_recusada(string date)
    {
        Should.Throw<DomainException>(() => Wallet.Create("Azul", WalletType.Checking, "#000000", Dinheiro.Zero, DataIso(date)))
            .Error.Code.ShouldBe("date.out_of_range");
    }

    [Fact]
    public void Edicoes_revalidam_e_atualizam_o_normalizado()
    {
        var wallet = Azul();

        wallet.Rename("Verde Ágil");
        wallet.ChangeColor("#43A047");
        wallet.ChangeType(WalletType.Savings);
        wallet.ChangeInitialBalance(Dinheiro.FromCents(-100), new DateOnly(2026, 1, 1));

        wallet.NameNormalized.ShouldBe("verde agil");
        wallet.Color.ShouldBe("#43A047");
        wallet.Type.ShouldBe(WalletType.Savings);
        wallet.InitialBalanceCents.ShouldBe(-100);
        Should.Throw<DomainException>(() => wallet.Rename(""));
    }

    [Fact]
    public void Arquivar_a_principal_exige_trocar_a_principal_antes()
    {
        var wallet = Azul(principal: true);

        Should.Throw<DomainException>(() => wallet.Archive(Agora)).Error.Code.ShouldBe("wallet.new_primary_required");

        wallet.UnmarkPrimary();
        wallet.Archive(Agora);
        wallet.IsArchived.ShouldBeTrue();
        wallet.ArchivedAt.ShouldBe(Agora);

        wallet.Unarchive();
        wallet.IsArchived.ShouldBeFalse();
    }

    [Fact]
    public void Carteira_arquivada_nao_pode_ser_principal()
    {
        var wallet = Azul();
        wallet.Archive(Agora);

        Should.Throw<DomainException>(() => wallet.MarkPrimary()).Error.Code.ShouldBe("wallet.archived");
    }
}
