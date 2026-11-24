using System.Net;
using System.Net.Http.Json;
using FinMy.Api.Tests.Support;
using FinMy.Application.Tests.Infrastructure;

namespace FinMy.Api.Tests.OAuth;

/// <summary>A lista <c>Mcp:AllowedRedirectUris</c> da configuração substitui a padrão (008 research R2).</summary>
public class OAuthRedirectConfigTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private const string Custom = "https://outro.exemplo.com/callback";

    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?> { ["Mcp:AllowedRedirectUris:0"] = Custom };

    [Fact]
    public async Task Redirect_da_configuracao_substitui_a_lista_padrao()
    {
        var padrao = await Client.PostAsJsonAsync("/oauth/register", new { client_name = "Claude", redirect_uris = new[] { OAuthFlow.Redirect } });
        var configurado = await Client.PostAsJsonAsync("/oauth/register", new { client_name = "Outro", redirect_uris = new[] { Custom } });

        padrao.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        configurado.StatusCode.ShouldBe(HttpStatusCode.Created);
    }
}
