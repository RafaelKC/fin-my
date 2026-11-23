using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Api.Endpoints;
using FinMy.Api.Tests.Support;
using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace FinMy.Api.Tests.Auth;

public class SessionValidationTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override IEndpointModule[] ExtraEndpoints => [new AuthProbeEndpoints()];

    private async Task<HttpResponseMessage> GetAsync(string path, string? bearer)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, path);
        if (bearer is not null)
        {
            message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
        }

        return await Client.SendAsync(message);
    }

    private static async Task ShouldBeRevokedAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        problem.GetProperty("code").GetString().ShouldBe("session_revoked");
        problem.GetProperty("status").GetInt32().ShouldBe(401);
        problem.GetProperty("detail").GetString().ShouldNotBeNullOrWhiteSpace();
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Sessao_ativa_passa()
    {
        var session = await Factory.CreateSessionAsync();

        (await GetAsync("/test/whoami", session.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Revogada_a_sessao_a_requisicao_seguinte_responde_401_session_revoked()
    {
        var session = await Factory.CreateSessionAsync();
        (await GetAsync("/test/whoami", session.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await Factory.RevokeSessionAsync(session.SessionId);

        await ShouldBeRevokedAsync(await GetAsync("/test/whoami", session.AccessToken));
    }

    [Fact]
    public async Task Revogar_uma_sessao_nao_derruba_as_outras_do_mesmo_usuario()
    {
        var first = await Factory.CreateSessionAsync();
        var second = await Factory.CreateSessionAsync();
        (await GetAsync("/test/whoami", first.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync("/test/whoami", second.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await Factory.RevokeSessionAsync(first.SessionId);

        await ShouldBeRevokedAsync(await GetAsync("/test/whoami", first.AccessToken));
        (await GetAsync("/test/whoami", second.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Token_de_sessao_inexistente_responde_401_session_revoked()
    {
        var session = await Factory.CreateSessionAsync();
        var orphan = Factory.Services.GetRequiredService<ITokenService>()
            .IssueAccessToken(session.Request with { SessionId = Guid.NewGuid() }).Token;

        await ShouldBeRevokedAsync(await GetAsync("/test/whoami", orphan));
    }

    [Fact]
    public async Task Rota_aberta_com_sessao_revogada_tambem_responde_401()
    {
        var session = await Factory.CreateSessionAsync();
        await Factory.RevokeSessionAsync(session.SessionId);

        await ShouldBeRevokedAsync(await GetAsync("/test/maybe-whoami", session.AccessToken));
    }

    [Fact]
    public async Task Requisicao_anonima_nao_passa_pela_validacao_de_sessao()
    {
        (await GetAsync("/test/maybe-whoami", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync("/health", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
