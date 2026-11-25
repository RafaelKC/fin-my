using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using FinMy.Api.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FinMy.Api.Tests.OAuth;

/// <summary>Resposta de <c>POST /oauth/token</c>.</summary>
public sealed record TokenResponse(string AccessToken, string RefreshToken, string TokenType, int ExpiresIn);

/// <summary>Os passos do conector (Claude) contra a API em teste: registrar, consentir (como o front), autorizar e trocar o código.</summary>
public sealed class OAuthFlow(ApiFactory factory)
{
    public const string Redirect = "https://claude.ai/api/mcp/auth_callback";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    /// <summary>Cliente sem seguir redirects (o <c>302</c> do authorize é o que se quer ver).</summary>
    public HttpClient Raw { get; } = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public static string NewVerifier() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string ChallengeOf(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    public async Task<string> RegisterAsync(string name = "Claude", string redirect = Redirect)
    {
        var response = await Raw.PostAsJsonAsync("/oauth/register", new { client_name = name, redirect_uris = new[] { redirect } });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("client_id").GetString()!;
    }

    public static string AuthorizeUrl(string clientId, string challenge, string? ticket = null, string redirect = Redirect, string method = "S256", string state = "estado-1")
    {
        var query = $"response_type=code&client_id={clientId}&redirect_uri={Uri.EscapeDataString(redirect)}&code_challenge={challenge}&code_challenge_method={method}&state={state}&resource={Uri.EscapeDataString("http://finmy.exemplo.com/mcp")}";
        return $"/oauth/authorize?{query}{(ticket is null ? string.Empty : $"&consent_ticket={ticket}")}";
    }

    /// <summary>O que o front faz ao clicar em "Autorizar": o ticket de consentimento com o bearer do app.</summary>
    public async Task<HttpResponseMessage> RequestTicketAsync(string appToken, string? clientId, string? redirect, string? challenge)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/oauth/consent-tickets")
        {
            Content = JsonContent.Create(new { clientId, redirectUri = redirect, codeChallenge = challenge }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", appToken);
        return await Raw.SendAsync(request);
    }

    public async Task<string> TicketAsync(string appToken, string clientId, string challenge, string redirect = Redirect)
    {
        var response = await RequestTicketAsync(appToken, clientId, redirect, challenge);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return body.GetProperty("ticket").GetString()!;
    }

    /// <summary>Código devolvido no redirect do authorize (falha o teste se o redirect trouxer erro).</summary>
    public static async Task<string> CodeFromAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect, await ReadAsync(response));
        var location = response.Headers.Location!;
        location.GetLeftPart(UriPartial.Path).ShouldBe(Redirect);
        var query = HttpUtility.ParseQueryString(location.Query);
        query["error"].ShouldBeNull();
        query["iss"].ShouldNotBeNull().ShouldNotEndWith("/"); // RFC 9207: igual ao issuer da descoberta
        return query["code"]!;
    }

    public async Task<HttpResponseMessage> ExchangeAsync(string clientId, string code, string verifier, string redirect = Redirect)
        => await Raw.PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = redirect,
        }));

    public async Task<HttpResponseMessage> RefreshAsync(string clientId, string refreshToken)
        => await Raw.PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = clientId,
            ["refresh_token"] = refreshToken,
        }));

    public static async Task<TokenResponse> TokensAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await ReadAsync(response));
        return (await response.Content.ReadFromJsonAsync<TokenResponse>(Json))!;
    }

    /// <summary>Conecta do começo ao fim: registro, ticket, authorize e token. Devolve o cliente e os tokens.</summary>
    public async Task<(string ClientId, TokenResponse Tokens)> ConnectAsync(string appToken, string clientName = "Claude")
    {
        var clientId = await RegisterAsync(clientName);
        var verifier = NewVerifier();
        var ticket = await TicketAsync(appToken, clientId, ChallengeOf(verifier));
        var code = await CodeFromAsync(await Raw.GetAsync(AuthorizeUrl(clientId, ChallengeOf(verifier), ticket)));
        return (clientId, await TokensAsync(await ExchangeAsync(clientId, code, verifier)));
    }

    public async Task<HttpResponseMessage> WhoAmIAsync(string? bearer, string path = OAuthProbeEndpoints.WhoAmIPath)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        return await Raw.SendAsync(request);
    }

    private static async Task<string> ReadAsync(HttpResponseMessage response) => await response.Content.ReadAsStringAsync() + " " + response.Headers.Location;

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
