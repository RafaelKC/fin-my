using FinMy.Domain.Common;
using FinMy.Domain.Tags;

namespace FinMy.Domain.Tests.Tags;

public class TagRulesTests
{
    private static readonly DateTimeOffset Agora = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Cria_com_nome_aparado_e_normalizado_e_sem_acerto_por_padrao()
    {
        var tag = Tag.Create("  Léo ", "#43a047");

        tag.Name.ShouldBe("Léo");
        tag.NameNormalized.ShouldBe("leo");
        tag.Color.ShouldBe("#43a047");
        tag.IsSettlement.ShouldBeFalse();
        tag.IsArchived.ShouldBeFalse();
    }

    [Fact]
    public void Aceita_nome_de_30_caracteres_e_recusa_31()
    {
        Tag.Create(new string('a', 30), "#1E88E5").Name.Length.ShouldBe(30);

        var erro = Should.Throw<DomainException>(() => Tag.Create(new string('a', 31), "#1E88E5"));
        erro.Error.Code.ShouldBe("tag.name.too_long");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Recusa_nome_vazio(string nome)
    {
        Should.Throw<DomainException>(() => Tag.Create(nome, "#1E88E5")).Error.Code.ShouldBe("tag.name.required");
    }

    [Theory]
    [InlineData("azul")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("1E88E5")]
    public void Recusa_cor_fora_do_formato(string cor)
    {
        Should.Throw<DomainException>(() => Tag.Create("Bruna", cor)).Error.Code.ShouldBe("tag.color_invalid");
    }

    [Fact]
    public void Renomeia_recolore_marca_e_desmarca_acerto()
    {
        var tag = Tag.Create("Bruna", "#1E88E5");

        tag.Rename("Duda");
        tag.ChangeColor("#E53935");
        tag.MarkSettlement();

        tag.Name.ShouldBe("Duda");
        tag.NameNormalized.ShouldBe("duda");
        tag.Color.ShouldBe("#E53935");
        tag.IsSettlement.ShouldBeTrue();

        tag.UnmarkSettlement();
        tag.IsSettlement.ShouldBeFalse();
    }

    [Fact]
    public void Arquiva_mantendo_a_primeira_data_e_desarquiva()
    {
        var tag = Tag.Create("Bruna", "#1E88E5", isSettlement: true);

        tag.Archive(Agora);
        tag.Archive(Agora.AddDays(1));
        tag.ArchivedAt.ShouldBe(Agora);
        tag.IsArchived.ShouldBeTrue();

        tag.Unarchive();
        tag.IsArchived.ShouldBeFalse();
    }
}
