using System.Net;
using System.Text;
using System.Text.Json;

namespace FinMy.Api.Tests.Support;

/// <summary>Os helpers falam o contrato (openapi.yaml); aqui são verificados contra um servidor simulado, pois os endpoints chegam na T019/T021.</summary>
public class AccountHelpersTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(string Path, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!.AbsolutePath, body));
            return respond(request, body);
        }
    }

    private const string SessionJson = """
        {"accessToken":"jwt-123","accessTokenExpiresAt":"2026-11-23T12:15:00Z",
         "user":{"id":"11111111-1111-1111-1111-111111111111","name":"Maria Teste","email":"m@exemplo.com"},
         "tenant":{"id":"22222222-2222-2222-2222-222222222222","name":"Finanças de Maria","role":"owner"}}
        """;

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task SignUpAsync_posta_no_contrato_e_devolve_conta_com_token()
    {
        var handler = new StubHandler((_, _) => Json(SessionJson, HttpStatusCode.Created));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };

        var account = await client.SignUpAsync(name: "Maria Teste", email: "m@exemplo.com");

        handler.Requests.Single().Path.ShouldBe("/api/v1/auth/signup");
        using var sent = JsonDocument.Parse(handler.Requests.Single().Body!);
        sent.RootElement.GetProperty("email").GetString().ShouldBe("m@exemplo.com");
        sent.RootElement.GetProperty("password").GetString().ShouldBe(AccountHelpers.DefaultPassword);
        account.AccessToken.ShouldBe("jwt-123");
        account.Session.Tenant.Role.ShouldBe("owner");
        account.Email.ShouldBe("m@exemplo.com");
    }

    [Fact]
    public async Task SignUpAsync_gera_email_unico_por_chamada()
    {
        var handler = new StubHandler((_, _) => Json(SessionJson, HttpStatusCode.Created));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };

        var a = await client.SignUpAsync();
        var b = await client.SignUpAsync();

        a.Email.ShouldNotBe(b.Email);
    }

    [Fact]
    public async Task LoginAsync_posta_em_login_e_le_a_sessao()
    {
        var handler = new StubHandler((_, _) => Json(SessionJson));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };

        var session = await client.LoginAsync("m@exemplo.com");

        handler.Requests.Single().Path.ShouldBe("/api/v1/auth/login");
        session.AccessToken.ShouldBe("jwt-123");
        session.User.Name.ShouldBe("Maria Teste");
    }

    [Fact]
    public async Task SignUpAsync_falha_quando_a_API_recusa()
    {
        var handler = new StubHandler((_, _) => Json("{}", HttpStatusCode.Conflict));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };

        await Should.ThrowAsync<HttpRequestException>(() => client.SignUpAsync());
    }

    [Fact]
    public void WithBearer_define_o_cabecalho_Authorization()
    {
        using var client = new HttpClient();

        client.WithBearer("abc").DefaultRequestHeaders.Authorization!.ToString().ShouldBe("Bearer abc");
    }
}
