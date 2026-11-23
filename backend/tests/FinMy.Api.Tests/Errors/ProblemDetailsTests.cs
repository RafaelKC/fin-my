using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FinMy.Api.Endpoints;
using FinMy.Api.Tests.Support;
using FinMy.Application.Tests.Infrastructure;

namespace FinMy.Api.Tests.Errors;

public class ProblemDetailsTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    protected override IEndpointModule[] ExtraEndpoints => [new TestEndpoints()];

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    [Theory]
    [InlineData("/test/not-found", HttpStatusCode.NotFound, "wallet.not_found", "Carteira não encontrada.")]
    [InlineData("/test/conflict", HttpStatusCode.Conflict, "email_already_registered", "Este e-mail já tem conta. Entrar ou recuperar senha?")]
    [InlineData("/test/domain-result", HttpStatusCode.NotFound, "not_found", "Não encontramos o que você procura.")]
    public async Task Erro_de_dominio_vira_problem_com_code_e_detail_em_ptbr(string path, HttpStatusCode status, string code, string detail)
    {
        var response = await Client.GetAsync(path);

        response.StatusCode.ShouldBe(status);
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("code").GetString().ShouldBe(code);
        problem.GetProperty("detail").GetString().ShouldBe(detail);
        problem.GetProperty("status").GetInt32().ShouldBe((int)status);
        problem.GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
        problem.GetProperty("type").GetString().ShouldNotBeNullOrWhiteSpace();
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Limitacao_de_dominio_traz_retryAfterSeconds_e_cabecalho()
    {
        var response = await Client.GetAsync("/test/locked");

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter!.Delta.ShouldBe(TimeSpan.FromSeconds(900));
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("code").GetString().ShouldBe("login_locked");
        problem.GetProperty("retryAfterSeconds").GetInt32().ShouldBe(900);
    }

    [Fact]
    public async Task Validacao_do_FluentValidation_vira_422_com_errors_por_campo_em_camelCase()
    {
        var response = await Client.GetAsync("/test/validation");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("code").GetString().ShouldBe("validation_failed");
        var errors = problem.GetProperty("errors");
        errors.GetProperty("name").EnumerateArray().Select(e => e.GetString()).ShouldBe(["Informe o nome.", "O nome é muito longo."]);
        errors.GetProperty("address.zipCode").EnumerateArray().Single().GetString().ShouldBe("CEP inválido.");
    }

    [Fact]
    public async Task Validacao_de_dominio_vira_422_com_errors()
    {
        var response = await Client.GetAsync("/test/domain-validation");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("errors").GetProperty("name").EnumerateArray().Single().GetString().ShouldBe("Informe o nome.");
    }

    [Fact]
    public async Task Excecao_inesperada_vira_500_generico_sem_vazar_a_mensagem()
    {
        var response = await Client.GetAsync("/test/boom?email=ana@exemplo.com");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain("segredo interno");
        body.ShouldNotContain("ana@exemplo.com");
        body.ShouldNotContain("InvalidOperationException");
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("code").GetString().ShouldBe("internal_error");
        json.RootElement.GetProperty("detail").GetString().ShouldBe("Algo deu errado do nosso lado. Tente novamente em instantes.");
    }

    [Fact]
    public async Task Rota_inexistente_vira_404_problem_not_found()
    {
        var response = await Client.GetAsync("/nao-existe");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("code").GetString().ShouldBe("not_found");
        problem.GetProperty("detail").GetString().ShouldBe("Não encontramos o que você procura.");
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Metodo_nao_permitido_vira_405_problem()
    {
        var response = await Client.DeleteAsync("/test/not-found");

        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        (await ReadProblemAsync(response)).GetProperty("code").GetString().ShouldBe("method_not_allowed");
    }

    [Fact]
    public async Task Json_malformado_vira_400_problem()
    {
        using var content = new StringContent("{ nome:", Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/test/echo", content);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("code").GetString().ShouldBe("bad_request");
        problem.GetProperty("detail").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Tipo_de_conteudo_nao_suportado_vira_415_problem()
    {
        using var content = new StringContent("nome", Encoding.UTF8, "text/plain");

        var response = await Client.PostAsync("/test/echo", content);

        response.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        (await ReadProblemAsync(response)).GetProperty("code").GetString().ShouldBe("unsupported_media_type");
    }

    [Fact]
    public async Task Corpo_valido_continua_funcionando()
    {
        var response = await Client.PostAsJsonAsync("/test/echo", new { name = "Maria" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
