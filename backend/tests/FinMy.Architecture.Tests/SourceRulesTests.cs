using System.Text.RegularExpressions;

namespace FinMy.Architecture.Tests;

/// <summary>Regras sobre o código-fonte de produção: filtros, relógio e HTTP na Application.</summary>
public partial class SourceRulesTests
{
    private const string JobsFolder = "FinMy.Infrastructure/Jobs/";
    private const string ClockFile = "FinMy.Infrastructure/Time/SystemClock.cs";

    [Fact]
    public void Codigo_de_producao_foi_encontrado()
    {
        SourceTree.ProductionFiles().Count().ShouldBeGreaterThan(10);
    }

    [Fact]
    public void Nenhum_IgnoreQueryFilters_sem_nome_nem_com_Tenant_fora_de_Infrastructure_Jobs()
    {
        var violacoes = SourceTree.ProductionFiles()
            .Where(f => !f.RelativePath.StartsWith(JobsFolder, StringComparison.Ordinal))
            .SelectMany(f => FiltrosProibidos(f.Code).Select(c => $"{f.RelativePath}: {c}"));

        violacoes.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("db.Parents.IgnoreQueryFilters()")]
    [InlineData("db.Parents.IgnoreQueryFilters( )")]
    [InlineData("db.Parents.IgnoreQueryFilters([\"Tenant\"])")]
    [InlineData("db.Parents.IgnoreQueryFilters([\"SoftDelete\", \"Tenant\"])")]
    [InlineData("db.Parents.IgnoreQueryFilters([ModelBuilderExtensions.TenantFilter])")]
    [InlineData("db.Parents.IgnoreQueryFilters(new[] { \"Tenant\" })")]
    [InlineData("db.Parents.IgnoreQueryFilters(filtros)")]
    public void Detecta_uso_proibido_de_IgnoreQueryFilters(string codigo)
    {
        FiltrosProibidos(codigo).ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("db.Parents.IgnoreQueryFilters([\"SoftDelete\"])")]
    [InlineData("db.Parents.IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])")]
    [InlineData("// db.Parents.IgnoreQueryFilters()")]
    [InlineData("/// <c>IgnoreQueryFilters([\"Tenant\"])</c>")]
    public void Aceita_IgnoreQueryFilters_nomeado_sem_Tenant_e_comentarios(string codigo)
    {
        FiltrosProibidos(SourceTree.StripComments(codigo)).ShouldBeEmpty();
    }

    [Fact]
    public void Nenhum_uso_de_DateTime_Now_UtcNow_ou_DateTimeOffset_Now_fora_de_SystemClock()
    {
        var violacoes = SourceTree.ProductionFiles()
            .Where(f => f.RelativePath != ClockFile)
            .SelectMany(f => RelogioProibido(f.Code).Select(c => $"{f.RelativePath}: {c}"));

        violacoes.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("var a = DateTime.Now;")]
    [InlineData("var a = DateTime.UtcNow;")]
    [InlineData("var a = DateTimeOffset.Now;")]
    [InlineData("var a = DateTimeOffset.UtcNow;")]
    [InlineData("var a = System.DateTime.UtcNow;")]
    [InlineData("var a = DateTime . Now;")]
    public void Detecta_acesso_direto_ao_relogio(string codigo)
    {
        RelogioProibido(codigo).ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("var a = clock.UtcNow;")]
    [InlineData("var a = timeProvider.GetUtcNow();")]
    [InlineData("var a = MyDateTime.Now;")]
    public void Aceita_o_que_nao_e_relogio_proibido(string codigo)
    {
        RelogioProibido(codigo).ShouldBeEmpty();
    }

    [Fact]
    public void Application_nao_usa_Microsoft_AspNetCore_Http_no_codigo()
    {
        var violacoes = SourceTree.ProductionFiles()
            .Where(f => f.RelativePath.StartsWith("FinMy.Application/", StringComparison.Ordinal))
            .Where(f => f.Code.Contains("Microsoft.AspNetCore", StringComparison.Ordinal))
            .Select(f => f.RelativePath);

        violacoes.ShouldBeEmpty();
    }

    private static IEnumerable<string> FiltrosProibidos(string code)
    {
        foreach (Match m in IgnoreQueryFiltersRegex().Matches(code))
        {
            var args = m.Groups["args"].Value.Trim();
            if (args.Length == 0)
            {
                yield return "IgnoreQueryFilters() sem nome";
            }
            else if (args.Contains("Tenant", StringComparison.Ordinal) || !args.Contains("SoftDelete", StringComparison.Ordinal))
            {
                yield return $"IgnoreQueryFilters({args})";
            }
        }
    }

    private static IEnumerable<string> RelogioProibido(string code) =>
        ClockRegex().Matches(code).Select(m => Regex.Replace(m.Value, @"\s+", string.Empty));

    [GeneratedRegex(@"\bIgnoreQueryFilters\s*\((?<args>[^;]*?)\)\s*(?=[.;)\s]|$)", RegexOptions.Singleline)]
    private static partial Regex IgnoreQueryFiltersRegex();

    [GeneratedRegex(@"(?<![\w.])(?:System\s*\.\s*)?(?:DateTime\s*\.\s*(?:Now|UtcNow)|DateTimeOffset\s*\.\s*(?:Now|UtcNow))\b")]
    private static partial Regex ClockRegex();
}
