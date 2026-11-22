using System.Reflection;
using FinMy.Application.Abstractions;
using FinMy.Domain.Common;
using FinMy.Infrastructure.Persistence;
using NetArchTest.Rules;

namespace FinMy.Architecture.Tests;

/// <summary>docs/arquitetura.md §1: Api → Application → Domain; Infrastructure → Application, Domain.</summary>
public class LayerDependencyTests
{
    private const string Domain = "FinMy.Domain";
    private const string Application = "FinMy.Application";
    private const string Infrastructure = "FinMy.Infrastructure";
    private const string Api = "FinMy.Api";

    private static readonly Assembly DomainAssembly = typeof(IEntity).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(IClock).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(FinMyDbContext).Assembly;
    private static readonly Assembly ApiAssembly = typeof(Program).Assembly;

    [Fact]
    public void Domain_nao_depende_de_nenhuma_outra_camada_nem_de_EF_Core()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(Application, Infrastructure, Api, "Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.AspNetCore")
            .GetResult();

        Falhas(result).ShouldBeEmpty();
    }

    [Fact]
    public void Application_depende_so_do_Domain()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(Infrastructure, Api, "Microsoft.EntityFrameworkCore", "Npgsql")
            .GetResult();

        Falhas(result).ShouldBeEmpty();
    }

    [Fact]
    public void Infrastructure_nao_depende_da_Api()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOn(Api)
            .GetResult();

        Falhas(result).ShouldBeEmpty();
    }

    [Fact]
    public void Referencias_de_projeto_seguem_as_camadas_permitidas()
    {
        Referencias(DomainAssembly).ShouldBeEmpty();
        // O compilador omite referências sem uso, por isso a checagem é de "somente estas", não de "todas estas".
        Referencias(ApplicationAssembly).Except([Domain]).ShouldBeEmpty();
        Referencias(InfrastructureAssembly).Except([Application, Domain]).ShouldBeEmpty();
        Referencias(ApiAssembly).Except([Application, Infrastructure]).ShouldBeEmpty();
    }

    [Fact]
    public void Application_nao_referencia_Microsoft_AspNetCore_Http()
    {
        Referencias(ApplicationAssembly, "Microsoft.AspNetCore").ShouldBeEmpty();

        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.AspNetCore.Http")
            .GetResult();

        Falhas(result).ShouldBeEmpty();
    }

    [Fact]
    public void Endpoints_da_Api_nao_usam_tipos_da_Infrastructure_fora_da_raiz_de_composicao()
    {
        // Program.cs é a raiz de composição (DI e comando migrate); qualquer outro tipo da Api fica de fora da Infrastructure.
        var result = Types.InAssembly(ApiAssembly)
            .That().DoNotHaveNameMatching(@"^(Program|<Program>.*|.*Composition.*|.*ServiceCollectionExtensions|.*Migrat.*)$")
            .And().DoNotResideInNamespace("FinMy.Api.Composition")
            .ShouldNot()
            .HaveDependencyOn(Infrastructure)
            .GetResult();

        Falhas(result).ShouldBeEmpty();
    }

    private static string[] Referencias(Assembly assembly, string? prefixo = null) =>
        [.. assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => prefixo is null ? n.StartsWith("FinMy.", StringComparison.Ordinal) : n.StartsWith(prefixo, StringComparison.Ordinal))];

    private static IEnumerable<string> Falhas(TestResult result) => result.FailingTypeNames ?? [];
}
