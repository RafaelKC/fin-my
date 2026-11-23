using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Api.Tests.Support;
using FinMy.Application.Tests.Infrastructure;

namespace FinMy.Api.Tests.Auth;

/// <summary>US2: <c>GET /api/v1/me</c> devolve o usuário e o Espaço do contexto (<c>MeResponse</c>).</summary>
public class GetMeTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private const string Me = "/api/v1/me";

    [Fact]
    public async Task Com_token_responde_200_com_usuario_e_espaco_do_contexto()
    {
        using var anonymous = Factory.CreateClient();
        var account = await anonymous.SignUpAsync(name: "Maria Teste");
        using var client = Factory.CreateClient().WithBearer(account.AccessToken);

        var response = await client.GetAsync(Me);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl?.NoStore.ShouldBeTrue();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("user").GetProperty("id").GetString().ShouldBe(account.Session.User.Id.ToString());
        body.GetProperty("user").GetProperty("name").GetString().ShouldBe("Maria Teste");
        body.GetProperty("user").GetProperty("email").GetString().ShouldBe(account.Email);
        body.GetProperty("tenant").GetProperty("id").GetString().ShouldBe(account.Session.Tenant.Id.ToString());
        body.GetProperty("tenant").GetProperty("name").GetString().ShouldBe(account.Session.Tenant.Name);
        body.GetProperty("tenant").GetProperty("role").GetString().ShouldBe("owner");
        body.GetProperty("user").TryGetProperty("passwordHash", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Sem_token_responde_401()
    {
        (await Client.GetAsync(Me)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
