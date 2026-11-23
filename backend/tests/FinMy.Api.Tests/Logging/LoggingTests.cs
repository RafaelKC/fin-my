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
            await using var factory = new ApiFactory(banco, null, new TestEndpoints());
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
}
