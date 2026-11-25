using FinMy.Api.Composition;
using FinMy.Api.Endpoints;
using FinMy.Api.Tests.Support;
using FinMy.Application.Tests.Infrastructure;
using Serilog.Events;
using Serilog.Parsing;

namespace FinMy.Api.Tests.Logging;

public class RedactingJsonFormatterTests
{
    private static string Format(string template, params object[] values)
    {
        var parser = new MessageTemplateParser();
        var tokens = parser.Parse(template);
        var properties = tokens.Tokens.OfType<PropertyToken>().Zip(values, (t, v) => new LogEventProperty(t.PropertyName, new ScalarValue(v)));
        var evt = new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Information, new InvalidOperationException("falha com joana@exemplo.com"), tokens, properties.ToArray());

        using var writer = new StringWriter();
        new RedactingJsonFormatter().Format(evt, writer);
        return writer.ToString();
    }

    [Fact]
    public void Saida_e_json_de_uma_linha_com_mensagem_renderizada()
    {
        var line = Format("Requisição {Path} concluída", "/health").TrimEnd();

        line.ShouldContain("\"@m\":\"Requisição \\\"/health\\\" concluída\"");
        line.Contains('\n', StringComparison.Ordinal).ShouldBeFalse();
        System.Text.Json.JsonDocument.Parse(line).ShouldNotBeNull();
    }

    [Fact]
    public void Enderecos_de_email_somem_da_mensagem_das_propriedades_e_da_excecao()
    {
        var line = Format("Usuário {Email} entrou", "maria.silva+teste@exemplo.com.br");

        line.ShouldNotContain("maria.silva");
        line.ShouldNotContain("joana@exemplo.com");
        line.ShouldContain("[email]");
    }
}

[Collection(GlobalStateGroup.Name)]
public class LoggingOutputTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    protected override IEndpointModule[] ExtraEndpoints => [new TestEndpoints()];

    [Fact]
    public async Task Logs_da_API_saem_em_json_e_nao_contem_email()
    {
        var original = Console.Out;
        using var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            // Novo host criado já com o console capturado (o sink do Serilog fixa Console.Out ao ser criado).
            var banco = await Postgres.CreateEmptyDatabaseAsync();
            await using var factory = new ApiFactory(banco, null, null, new TestEndpoints());
            await factory.MigrateAsync();
            using var client = factory.CreateClient();

            (await client.GetAsync("/test/boom?email=ana@exemplo.com")).StatusCode.ShouldBe(System.Net.HttpStatusCode.InternalServerError);
            await Task.Delay(200);
        }
        finally
        {
            Console.SetOut(original);
        }

        var output = captured.ToString();
        output.ShouldContain("/test/boom");
        output.ShouldContain("\"@l\":\"Error\"");
        output.ShouldNotContain("ana@exemplo.com");
    }

    [Fact]
    public async Task Logs_nao_contem_email_nem_senha_enviados_em_query_corpo_e_cabecalhos()
    {
        const string email = "carla.segredo@exemplo.com";
        const string senha = "S3nha-Super-Secreta-9876";

        var original = Console.Out;
        using var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            var banco = await Postgres.CreateEmptyDatabaseAsync();
            await using var factory = new ApiFactory(banco, null, null, new TestEndpoints());
            await factory.MigrateAsync();
            using var client = factory.CreateClient();

            // Erro 500 com e-mail na URL e na mensagem da exceção.
            await client.GetAsync($"/test/boom?email={Uri.EscapeDataString(email)}");

            // Corpo com e-mail/senha em rota existente e em rota de auth (ainda inexistente = 404/405).
            using var body = System.Net.Http.Json.JsonContent.Create(new { name = "Carla", email, password = senha });
            await client.PostAsync("/test/echo", body);
            await client.PostAsync("/api/v1/auth/login", System.Net.Http.Json.JsonContent.Create(new { email, password = senha }));

            // Cabeçalhos com credenciais.
            using var request = new HttpRequestMessage(HttpMethod.Get, "/test/validation");
            request.Headers.TryAddWithoutValidation("Authorization", $"Basic {email}:{senha}");
            request.Headers.TryAddWithoutValidation("Cookie", $"finmy_rt={senha}");
            await client.SendAsync(request);

            await Task.Delay(200);
        }
        finally
        {
            Console.SetOut(original);
        }

        var output = captured.ToString();
        output.ShouldContain("\"@l\"");
        output.ShouldNotContain(email);
        output.ShouldNotContain("carla.segredo");
        output.ShouldNotContain(senha);
    }
}
