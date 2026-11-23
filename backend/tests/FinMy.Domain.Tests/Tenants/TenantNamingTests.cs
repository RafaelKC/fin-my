using FinMy.Domain.Tenants;

namespace FinMy.Domain.Tests.Tenants;

public class TenantNamingTests
{
    [Theory]
    [InlineData("Maria Souza", "Finanças de Maria")]
    [InlineData("Maria", "Finanças de Maria")]
    [InlineData("  Maria   Souza  ", "Finanças de Maria")]
    [InlineData("Ana\tLima", "Finanças de Ana")]
    [InlineData("José da Silva", "Finanças de José")]
    public void Usa_o_primeiro_trecho_do_nome_antes_do_espaco(string nome, string esperado)
    {
        TenantNaming.DefaultName(nome).ShouldBe(esperado);
    }

    [Fact]
    public void Trunca_o_primeiro_nome_para_caber_em_60_caracteres()
    {
        var nome = TenantNaming.DefaultName(new string('a', 80) + " Souza");

        nome.Length.ShouldBe(TenantNaming.MaxLength);
        nome.ShouldBe("Finanças de " + new string('a', 48));
    }

    [Fact]
    public void Primeiro_nome_de_exatamente_48_caracteres_nao_e_truncado()
    {
        var nome = TenantNaming.DefaultName(new string('b', 48));

        nome.Length.ShouldBe(60);
        nome.ShouldEndWith(new string('b', 48));
    }

    [Fact]
    public void Nao_corta_um_par_substituto_ao_truncar()
    {
        var nome = TenantNaming.DefaultName(new string('a', 47) + "😀😀");

        nome.ShouldBe("Finanças de " + new string('a', 47));
    }

    [Fact]
    public void Nome_nulo_e_rejeitado()
    {
        Should.Throw<ArgumentNullException>(() => TenantNaming.DefaultName(null!));
    }
}
