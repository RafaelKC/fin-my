using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Api.Endpoints;
using FinMy.Api.Tests.Support;
using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Common;
using FinMy.Infrastructure.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FinMy.Api.Tests.Auth;

public class AuthenticationTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override IEndpointModule[] ExtraEndpoints => [new AuthProbeEndpoints()];

    private static AccessTokenRequest Request(Guid? userId = null, Guid? tenantId = null, Channel channel = Channel.App)
        => new(userId ?? Guid.NewGuid(), tenantId ?? Guid.NewGuid(), Guid.NewGuid(), "Maria Teste", channel);

    private ITokenService Tokens => Factory.Services.GetRequiredService<ITokenService>();

    private static string TokenAt(DateTimeOffset now, AccessTokenRequest request)
        => new JwtTokenService(Options.Create(new JwtOptions { SigningKey = ApiFactory.TestSigningKey }), new FakeClock(now)).IssueAccessToken(request).Token;

    private async Task<HttpResponseMessage> GetAsync(string path, string? bearer)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, path);
        if (bearer is not null)
        {
            message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
        }

        return await Client.SendAsync(message);
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    [Fact]
    public async Task Token_valido_preenche_o_contexto_com_usuario_espaco_e_canal()
    {
        var session = await Factory.CreateSessionAsync(Channel.Claude);
        var request = session.Request;

        var response = await GetAsync("/test/whoami", session.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var who = (await response.Content.ReadFromJsonAsync<WhoAmI>(Json))!;
        who.IsAuthenticated.ShouldBeTrue();
        who.IsSystem.ShouldBeFalse();
        who.UserId.ShouldBe(request.UserId);
        who.TenantId.ShouldBe(request.TenantId);
        who.Channel.ShouldBe("Claude");
    }

    [Fact]
    public async Task Sem_token_a_rota_protegida_responde_401_unauthorized()
    {
        var response = await GetAsync("/test/whoami", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var problem = await ProblemAsync(response);
        problem.GetProperty("code").GetString().ShouldBe("unauthorized");
        problem.GetProperty("detail").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Sem_token_a_rota_aberta_segue_com_contexto_vazio()
    {
        var response = await GetAsync("/test/maybe-whoami", null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var who = (await response.Content.ReadFromJsonAsync<WhoAmI>(Json))!;
        who.IsAuthenticated.ShouldBeFalse();
        who.UserId.ShouldBeNull();
        who.TenantId.ShouldBeNull();
    }

    [Fact]
    public async Task Token_vencido_responde_401_token_expired()
    {
        var expired = TokenAt(DateTimeOffset.UtcNow.AddMinutes(-16), Request());

        var response = await GetAsync("/test/whoami", expired);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var problem = await ProblemAsync(response);
        problem.GetProperty("code").GetString().ShouldBe("token_expired");
        problem.GetProperty("status").GetInt32().ShouldBe(401);
        problem.GetProperty("detail").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Token_vencido_numa_rota_aberta_nao_preenche_o_contexto()
    {
        var expired = TokenAt(DateTimeOffset.UtcNow.AddMinutes(-16), Request());

        var response = await GetAsync("/test/maybe-whoami", expired);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<WhoAmI>(Json))!.IsAuthenticated.ShouldBeFalse();
    }

    [Fact]
    public async Task Token_adulterado_ou_de_outra_chave_responde_401_unauthorized_e_nao_token_expired()
    {
        var valid = Tokens.IssueAccessToken(Request()).Token;
        var parts = valid.Split('.');
        var tampered = $"{parts[0]}.{parts[1]}.{(parts[2][0] == 'A' ? 'B' : 'A')}{parts[2][1..]}";
        var otherKey = new JwtTokenService(
            Options.Create(new JwtOptions { SigningKey = "outra-chave-outra-chave-outra-chave-outra-chave-0123" }),
            new FakeClock(DateTimeOffset.UtcNow)).IssueAccessToken(Request()).Token;

        foreach (var bad in new[] { tampered, otherKey, "nao-e-um-jwt" })
        {
            var response = await GetAsync("/test/whoami", bad);

            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await ProblemAsync(response)).GetProperty("code").GetString().ShouldBe("unauthorized");
        }
    }

    [Fact]
    public async Task Claims_do_payload_trocadas_sem_a_assinatura_nao_valem()
    {
        var valid = Tokens.IssueAccessToken(Request()).Token;
        var parts = valid.Split('.');
        var payload = System.Text.Encoding.UTF8.GetString(Microsoft.IdentityModel.Tokens.Base64UrlEncoder.DecodeBytes(parts[1]))
            .Replace("\"channel\":\"app\"", "\"channel\":\"claude\"", StringComparison.Ordinal);
        var forged = $"{parts[0]}.{Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode(payload)}.{parts[2]}";

        var response = await GetAsync("/test/whoami", forged);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Escrita_autenticada_grava_o_autor_e_o_canal_vindos_do_token()
    {
        var request = (await Factory.CreateSessionAsync(Channel.Claude)).Request;
        using var message = new HttpRequestMessage(HttpMethod.Post, "/test/audited");
        message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Tokens.IssueAccessToken(request).Token);

        var response = await Client.SendAsync(message);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("createdBy").GetGuid().ShouldBe(request.UserId);
        body.GetProperty("createdVia").GetString().ShouldBe("Claude");
    }

    [Fact]
    public async Task Contexto_nao_vaza_entre_requisicoes()
    {
        var token = (await Factory.CreateSessionAsync()).AccessToken;

        (await GetAsync("/test/maybe-whoami", token)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var anonymous = await GetAsync("/test/maybe-whoami", null);

        (await anonymous.Content.ReadFromJsonAsync<WhoAmI>(Json))!.IsAuthenticated.ShouldBeFalse();
    }

    [Fact]
    public async Task Cookie_de_refresh_sai_com_os_atributos_do_contrato()
    {
        var response = await Client.PostAsync("/test/cookie/set", content: null);

        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        cookie.ShouldStartWith("finmy_rt=valor-do-token");
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("secure", Case.Insensitive);
        cookie.ShouldContain("samesite=strict", Case.Insensitive);
        cookie.ShouldContain("path=/api/v1/auth", Case.Insensitive);
        cookie.ShouldContain("max-age=7776000", Case.Insensitive);
    }

    [Fact]
    public async Task Apagar_o_cookie_usa_o_mesmo_caminho_e_expira_no_passado()
    {
        var response = await Client.PostAsync("/test/cookie/clear", content: null);

        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        cookie.ShouldStartWith("finmy_rt=;");
        cookie.ShouldContain("path=/api/v1/auth", Case.Insensitive);
        cookie.ShouldContain("expires=Thu, 01 Jan 1970", Case.Insensitive);
    }

    [Fact]
    public async Task Cookie_de_refresh_e_lido_da_requisicao()
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, "/test/cookie/read");
        message.Headers.Add("Cookie", "finmy_rt=abc123; outro=1");

        var response = await Client.SendAsync(message);

        (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("token").GetString().ShouldBe("abc123");
        var empty = await Client.GetFromJsonAsync<JsonElement>("/test/cookie/read", Json);
        empty.GetProperty("token").ValueKind.ShouldBe(JsonValueKind.Null);
    }
}
