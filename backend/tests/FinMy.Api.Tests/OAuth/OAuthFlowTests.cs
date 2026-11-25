using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using FinMy.Api.Endpoints;
using FinMy.Api.Tests.Support;
using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Identity;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

namespace FinMy.Api.Tests.OAuth;

/// <summary>008 T001 / US1: o fluxo OAuth 2.1 do conector (descoberta, registro, consentimento, código + PKCE, refresh, revogação).</summary>
public class OAuthFlowTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private const string AppUrl = "http://finmy.exemplo.com";

    private static readonly DateTimeOffset Start = DateTimeOffset.UtcNow;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly FakeClock _clock = new(Start);

    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?> { ["APP_URL"] = AppUrl };

    protected override IEndpointModule[] ExtraEndpoints => [new OAuthProbeEndpoints()];

    protected override IClock? Clock => _clock;

    private OAuthFlow Flow => field ??= new OAuthFlow(Factory);

    private async Task<TestAccount> SignUpAsync(string name = "Lia")
    {
        using var anonymous = Factory.CreateClient();
        return await anonymous.SignUpAsync(name);
    }

    private async Task<McpWhoAmI> WhoAmIOkAsync(string token)
    {
        var response = await Flow.WhoAmIAsync(token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<McpWhoAmI>(Json))!;
    }

    // ---- Descoberta ----

    [Fact]
    public async Task Descoberta_do_recurso_protegido_aponta_o_mcp_e_o_servidor_de_autorizacao()
    {
        foreach (var path in new[] { "/.well-known/oauth-protected-resource", "/.well-known/oauth-protected-resource/mcp" })
        {
            var response = await Flow.Raw.GetAsync(path);

            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("resource").GetString().ShouldBe($"{AppUrl}/mcp");
            body.GetProperty("authorization_servers").EnumerateArray().Select(e => e.GetString()).ShouldBe([AppUrl]);
        }
    }

    [Fact]
    public async Task Descoberta_do_servidor_de_autorizacao_anuncia_endpoints_PKCE_S256_e_registro()
    {
        var response = await Flow.Raw.GetAsync("/.well-known/oauth-authorization-server");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("issuer").GetString().ShouldBe(AppUrl);
        body.GetProperty("authorization_endpoint").GetString().ShouldEndWith("/oauth/authorize");
        body.GetProperty("token_endpoint").GetString().ShouldEndWith("/oauth/token");
        body.GetProperty("registration_endpoint").GetString().ShouldBe($"{AppUrl}/oauth/register");
        body.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(e => e.GetString()).ShouldBe(["S256"]);
        body.GetProperty("grant_types_supported").EnumerateArray().Select(e => e.GetString()).ShouldBe(["authorization_code", "refresh_token"], ignoreOrder: true);
        body.GetProperty("token_endpoint_auth_methods_supported").EnumerateArray().Select(e => e.GetString()).ShouldBe(["none"]);
    }

    // ---- Registro dinâmico ----

    [Theory]
    [InlineData("https://claude.ai/api/mcp/auth_callback")]
    [InlineData("https://claude.com/api/mcp/auth_callback")]
    public async Task Registro_com_redirect_permitido_cria_cliente_publico(string redirect)
    {
        var response = await Flow.Raw.PostAsJsonAsync("/oauth/register", new { client_name = "Claude", redirect_uris = new[] { redirect }, token_endpoint_auth_method = "none" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("client_id").GetString().ShouldNotBeNullOrWhiteSpace();
        body.GetProperty("client_name").GetString().ShouldBe("Claude");
        body.GetProperty("redirect_uris").EnumerateArray().Select(e => e.GetString()).ShouldBe([redirect]);
        body.GetProperty("token_endpoint_auth_method").GetString().ShouldBe("none");
        body.TryGetProperty("client_secret", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Registro_com_redirect_fora_da_lista_e_400_invalid_redirect_uri_e_nao_cria_nada()
    {
        var response = await Flow.Raw.PostAsJsonAsync("/oauth/register", new
        {
            client_name = "Claude",
            redirect_uris = new[] { OAuthFlow.Redirect, "https://malicioso.exemplo.com/callback" },
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().ShouldBe("invalid_redirect_uri");

        await using var scope = Factory.Services.CreateAsyncScope();
        // Só o cliente semeado do app Android (009) existe: o registro recusado não criou outro.
        (await scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>().CountAsync()).ShouldBe(1);
    }

    [Theory]
    [InlineData(null, "https://claude.ai/api/mcp/auth_callback", null, "invalid_client_metadata")]
    [InlineData("Claude", null, null, "invalid_redirect_uri")]
    [InlineData("Claude", "https://claude.ai/api/mcp/auth_callback", "client_secret_basic", "invalid_client_metadata")]
    public async Task Registro_invalido_e_400_com_o_erro_do_protocolo(string? name, string? redirect, string? authMethod, string expected)
    {
        var response = await Flow.Raw.PostAsJsonAsync("/oauth/register", new
        {
            client_name = name,
            redirect_uris = redirect is null ? null : new[] { redirect },
            token_endpoint_auth_method = authMethod,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().ShouldBe(expected);
    }

    // ---- /oauth/authorize ----

    [Fact]
    public async Task Authorize_sem_ticket_leva_ao_front_com_os_mesmos_parametros()
    {
        var clientId = await Flow.RegisterAsync();
        var challenge = OAuthFlow.ChallengeOf(OAuthFlow.NewVerifier());

        var response = await Flow.Raw.GetAsync(OAuthFlow.AuthorizeUrl(clientId, challenge));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect, await response.Content.ReadAsStringAsync());
        var location = response.Headers.Location!;
        location.GetLeftPart(UriPartial.Path).ShouldBe($"{AppUrl}/autorizar");
        var query = HttpUtility.ParseQueryString(location.Query);
        query["client_id"].ShouldBe(clientId);
        query["redirect_uri"].ShouldBe(OAuthFlow.Redirect);
        query["code_challenge"].ShouldBe(challenge);
        query["code_challenge_method"].ShouldBe("S256");
        query["state"].ShouldBe("estado-1");
        query["response_type"].ShouldBe("code");
        query["consent_ticket"].ShouldBeNull();
    }

    [Fact]
    public async Task Cancelar_volta_ao_redirect_registrado_com_access_denied_e_o_state_sem_emitir_codigo()
    {
        var clientId = await Flow.RegisterAsync();
        var challenge = OAuthFlow.ChallengeOf(OAuthFlow.NewVerifier());

        var response = await Flow.Raw.GetAsync($"{OAuthFlow.AuthorizeUrl(clientId, challenge)}&consent_denied=1");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = response.Headers.Location!;
        location.GetLeftPart(UriPartial.Path).ShouldBe(OAuthFlow.Redirect);
        var query = HttpUtility.ParseQueryString(location.Query);
        query["error"].ShouldBe("access_denied");
        query["state"].ShouldBe("estado-1");
        query["code"].ShouldBeNull();
    }

    [Fact]
    public async Task Cancelar_com_redirect_nao_registrado_nao_redireciona_para_ele()
    {
        var clientId = await Flow.RegisterAsync();
        var challenge = OAuthFlow.ChallengeOf(OAuthFlow.NewVerifier());

        var response = await Flow.Raw.GetAsync($"{OAuthFlow.AuthorizeUrl(clientId, challenge, redirect: "https://outro.exemplo.com/cb")}&consent_denied=1");

        response.Headers.Location?.GetLeftPart(UriPartial.Path).ShouldNotBe("https://outro.exemplo.com/cb");
    }

    [Fact]
    public async Task Authorize_recusa_pedido_sem_PKCE_ou_com_plain_sem_levar_ao_front()
    {
        var clientId = await Flow.RegisterAsync();
        var challenge = OAuthFlow.ChallengeOf(OAuthFlow.NewVerifier());

        var plain = await Flow.Raw.GetAsync(OAuthFlow.AuthorizeUrl(clientId, challenge, method: "plain"));
        var withoutPkce = await Flow.Raw.GetAsync($"/oauth/authorize?response_type=code&client_id={clientId}&redirect_uri={Uri.EscapeDataString(OAuthFlow.Redirect)}");

        foreach (var response in new[] { plain, withoutPkce })
        {
            response.Headers.Location?.GetLeftPart(UriPartial.Path).ShouldNotBe($"{AppUrl}/autorizar");
            (response.StatusCode == HttpStatusCode.BadRequest || response.Headers.Location!.Query.Contains("error=", StringComparison.Ordinal)).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Authorize_com_cliente_ou_redirect_desconhecido_e_recusado_sem_redirecionar()
    {
        var clientId = await Flow.RegisterAsync();
        var challenge = OAuthFlow.ChallengeOf(OAuthFlow.NewVerifier());

        var unknownClient = await Flow.Raw.GetAsync(OAuthFlow.AuthorizeUrl("cliente-que-nao-existe", challenge));
        var otherRedirect = await Flow.Raw.GetAsync(OAuthFlow.AuthorizeUrl(clientId, challenge, redirect: "https://malicioso.exemplo.com/callback"));

        foreach (var response in new[] { unknownClient, otherRedirect })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            response.Headers.Location.ShouldBeNull();
        }
    }

    // ---- Ticket de consentimento ----

    [Fact]
    public async Task Ticket_exige_login_no_app()
    {
        var clientId = await Flow.RegisterAsync();

        var anonymous = await Flow.Raw.PostAsJsonAsync("/api/v1/oauth/consent-tickets", new { clientId, redirectUri = OAuthFlow.Redirect, codeChallenge = OAuthFlow.ChallengeOf(OAuthFlow.NewVerifier()) });

        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Ticket_guarda_so_o_hash_e_vale_2_minutos()
    {
        var lia = await SignUpAsync();
        var clientId = await Flow.RegisterAsync();
        var challenge = OAuthFlow.ChallengeOf(OAuthFlow.NewVerifier());

        var response = await Flow.RequestTicketAsync(lia.AccessToken, clientId, OAuthFlow.Redirect, challenge);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        var ticket = body.GetProperty("ticket").GetString()!;
        body.GetProperty("expiresAt").GetDateTimeOffset().ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(1));

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMyDbContext>();
        var stored = await db.Set<OAuthConsentTicket>().AsNoTracking().SingleAsync();
        stored.UserId.ShouldBe(lia.Session.User.Id);
        stored.TenantId.ShouldBe(lia.Session.Tenant.Id);
        stored.ClientId.ShouldBe(clientId);
        stored.TicketHash.ShouldNotBe(System.Text.Encoding.UTF8.GetBytes(ticket));
        (stored.ExpiresAt - stored.CreatedAt).ShouldBe(TimeSpan.FromMinutes(2));
    }

    [Theory]
    [InlineData("desconhecido", null, "oauth_client_unknown")]
    [InlineData(null, "https://malicioso.exemplo.com/callback", "oauth_redirect_not_registered")]
    [InlineData(null, null, "oauth_code_challenge_invalid")]
    public async Task Ticket_para_pedido_invalido_e_422(string? clientOverride, string? redirectOverride, string code)
    {
        var lia = await SignUpAsync();
        var clientId = await Flow.RegisterAsync();
        var challenge = OAuthFlow.ChallengeOf(OAuthFlow.NewVerifier());

        var response = await Flow.RequestTicketAsync(
            lia.AccessToken,
            clientOverride ?? clientId,
            redirectOverride ?? OAuthFlow.Redirect,
            code == "oauth_code_challenge_invalid" ? "curto" : challenge);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("code").GetString().ShouldBe(code);
    }

    // ---- Fluxo completo ----

    [Fact]
    public async Task Fluxo_completo_emite_token_que_preenche_o_contexto_com_canal_claude_e_o_espaco_da_pessoa()
    {
        var lia = await SignUpAsync();
        var leo = await SignUpAsync("Léo");

        var (_, liaTokens) = await Flow.ConnectAsync(lia.AccessToken);
        var (_, leoTokens) = await Flow.ConnectAsync(leo.AccessToken);

        liaTokens.TokenType.ShouldBe("Bearer");
        liaTokens.ExpiresIn.ShouldBeInRange(3590, 3600);
        liaTokens.RefreshToken.ShouldNotBeNullOrWhiteSpace();

        var asLia = await WhoAmIOkAsync(liaTokens.AccessToken);
        asLia.IsAuthenticated.ShouldBeTrue();
        asLia.Channel.ShouldBe("Claude");
        asLia.UserId.ShouldBe(lia.Session.User.Id);
        asLia.TenantId.ShouldBe(lia.Session.Tenant.Id);

        // Cada conexão age só no Espaço de quem autorizou (US1.4 / RT-01).
        var asLeo = await WhoAmIOkAsync(leoTokens.AccessToken);
        asLeo.UserId.ShouldBe(leo.Session.User.Id);
        asLeo.TenantId.ShouldBe(leo.Session.Tenant.Id);
        asLeo.TenantId.ShouldNotBe(asLia.TenantId);
    }

    // ---- Cliente do app Android (009 T001) ----

    [Fact]
    public async Task Cliente_finmy_android_e_semeado_como_publico_com_PKCE_e_o_redirect_do_app_e_o_seed_e_idempotente()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var seeder = scope.ServiceProvider.GetRequiredService<FinMy.Infrastructure.Auth.OAuth.AndroidClientSeeder>();
        await seeder.SeedAsync(CancellationToken.None); // segunda vez: o ApiFactory já semeou ao migrar

        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        (await applications.CountAsync()).ShouldBe(1);
        var android = (await applications.FindByClientIdAsync(OAuthFlow.AndroidClientId)).ShouldNotBeNull();
        (await applications.GetDisplayNameAsync(android)).ShouldBe("App Fin-My (Android)");
        (await applications.GetClientTypeAsync(android)).ShouldBe("public");
        (await applications.GetRedirectUrisAsync(android)).ShouldBe([OAuthFlow.AndroidRedirect]);
        (await applications.GetRequirementsAsync(android)).ShouldContain("ft:pkce");
        (await applications.GetPermissionsAsync(android)).ShouldContain("gt:refresh_token");
    }

    [Fact]
    public async Task Fluxo_completo_do_finmy_android_emite_token_com_canal_android_widget_e_o_espaco_da_pessoa()
    {
        var lia = await SignUpAsync();

        var tokens = await Flow.ConnectAndroidAsync(lia.AccessToken);

        tokens.TokenType.ShouldBe("Bearer");
        tokens.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        var who = await WhoAmIOkAsync(tokens.AccessToken);
        who.Channel.ShouldBe("AndroidWidget");
        who.UserId.ShouldBe(lia.Session.User.Id);
        who.TenantId.ShouldBe(lia.Session.Tenant.Id);
    }

    [Fact]
    public async Task Refresh_do_finmy_android_rotaciona_o_token_e_mantem_o_canal_do_widget()
    {
        var lia = await SignUpAsync();
        var first = await Flow.ConnectAndroidAsync(lia.AccessToken);

        var second = await OAuthFlow.TokensAsync(await Flow.RefreshAsync(OAuthFlow.AndroidClientId, first.RefreshToken));

        second.RefreshToken.ShouldNotBe(first.RefreshToken);
        (await WhoAmIOkAsync(second.AccessToken)).Channel.ShouldBe("AndroidWidget");
        _clock.UtcNow += TimeSpan.FromSeconds(31); // passada a tolerância de reenvio do OpenIddict, o refresh antigo é reuso
        (await Flow.RefreshAsync(OAuthFlow.AndroidClientId, first.RefreshToken)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Redirect_do_claude_nao_vale_para_o_finmy_android_e_o_do_android_nao_vale_para_outros_clientes()
    {
        var lia = await SignUpAsync();
        var challenge = OAuthFlow.ChallengeOf(OAuthFlow.NewVerifier());
        var claudeClient = await Flow.RegisterAsync();

        (await Flow.RequestTicketAsync(lia.AccessToken, OAuthFlow.AndroidClientId, OAuthFlow.Redirect, challenge)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await Flow.RequestTicketAsync(lia.AccessToken, claudeClient, OAuthFlow.AndroidRedirect, challenge)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await Flow.Raw.PostAsJsonAsync("/oauth/register", new { client_name = "Falso", redirect_uris = new[] { OAuthFlow.AndroidRedirect } })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Gravacao_feita_com_token_do_conector_registra_o_usuario_e_created_via_claude()
    {
        var lia = await SignUpAsync();
        var (_, tokens) = await Flow.ConnectAsync(lia.AccessToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, OAuthProbeEndpoints.AuditedPath);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var response = await Flow.Raw.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("createdBy").GetGuid().ShouldBe(lia.Session.User.Id);
        body.GetProperty("createdVia").GetString().ShouldBe("Claude");
    }

    [Fact]
    public async Task Token_do_conector_so_vale_no_mcp_e_jwt_do_app_nao_vale_no_mcp()
    {
        var lia = await SignUpAsync();
        var (_, tokens) = await Flow.ConnectAsync(lia.AccessToken);

        (await Flow.WhoAmIAsync(tokens.AccessToken, "/api/v1/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Flow.RequestTicketAsync(tokens.AccessToken, "x", OAuthFlow.Redirect, "y")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Flow.WhoAmIAsync(lia.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Flow.WhoAmIAsync(null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Flow.WhoAmIAsync("token-inventado")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Ticket_e_de_uso_unico()
    {
        var lia = await SignUpAsync();
        var clientId = await Flow.RegisterAsync();
        var verifier = OAuthFlow.NewVerifier();
        var challenge = OAuthFlow.ChallengeOf(verifier);
        var ticket = await Flow.TicketAsync(lia.AccessToken, clientId, challenge);

        await OAuthFlow.CodeFromAsync(await Flow.Raw.GetAsync(OAuthFlow.AuthorizeUrl(clientId, challenge, ticket)));
        var again = await Flow.Raw.GetAsync(OAuthFlow.AuthorizeUrl(clientId, challenge, ticket));

        again.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        HttpUtility.ParseQueryString(again.Headers.Location!.Query)["error"].ShouldBe("access_denied");
        HttpUtility.ParseQueryString(again.Headers.Location.Query)["code"].ShouldBeNull();
    }

    [Fact]
    public async Task Ticket_vencido_depois_de_2_minutos_e_negado()
    {
        var lia = await SignUpAsync();
        var clientId = await Flow.RegisterAsync();
        var challenge = OAuthFlow.ChallengeOf(OAuthFlow.NewVerifier());
        var ticket = await Flow.TicketAsync(lia.AccessToken, clientId, challenge);

        _clock.UtcNow += TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(1);
        var response = await Flow.Raw.GetAsync(OAuthFlow.AuthorizeUrl(clientId, challenge, ticket));

        HttpUtility.ParseQueryString(response.Headers.Location!.Query)["error"].ShouldBe("access_denied");
    }

    [Fact]
    public async Task Ticket_nao_vale_para_outro_cliente_nem_outro_code_challenge()
    {
        var lia = await SignUpAsync();
        var clientId = await Flow.RegisterAsync();
        var otherClient = await Flow.RegisterAsync("Outro");
        var challenge = OAuthFlow.ChallengeOf(OAuthFlow.NewVerifier());
        var otherChallenge = OAuthFlow.ChallengeOf(OAuthFlow.NewVerifier());
        var ticket = await Flow.TicketAsync(lia.AccessToken, clientId, challenge);

        var wrongClient = await Flow.Raw.GetAsync(OAuthFlow.AuthorizeUrl(otherClient, challenge, ticket));
        var wrongChallenge = await Flow.Raw.GetAsync(OAuthFlow.AuthorizeUrl(clientId, otherChallenge, ticket));
        var invented = await Flow.Raw.GetAsync(OAuthFlow.AuthorizeUrl(clientId, challenge, "ticket-inventado"));

        foreach (var response in new[] { wrongClient, wrongChallenge, invented })
        {
            HttpUtility.ParseQueryString(response.Headers.Location!.Query)["error"].ShouldBe("access_denied");
        }

        // Os pedidos recusados não gastaram o ticket verdadeiro.
        await OAuthFlow.CodeFromAsync(await Flow.Raw.GetAsync(OAuthFlow.AuthorizeUrl(clientId, challenge, ticket)));
    }

    // ---- /oauth/token ----

    [Fact]
    public async Task Token_com_verifier_errado_e_invalid_grant()
    {
        var lia = await SignUpAsync();
        var clientId = await Flow.RegisterAsync();
        var verifier = OAuthFlow.NewVerifier();
        var ticket = await Flow.TicketAsync(lia.AccessToken, clientId, OAuthFlow.ChallengeOf(verifier));
        var code = await OAuthFlow.CodeFromAsync(await Flow.Raw.GetAsync(OAuthFlow.AuthorizeUrl(clientId, OAuthFlow.ChallengeOf(verifier), ticket)));

        var response = await Flow.ExchangeAsync(clientId, code, OAuthFlow.NewVerifier());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().ShouldBe("invalid_grant");
    }

    [Fact]
    public async Task Codigo_so_vale_uma_vez_e_o_reuso_revoga_o_que_ele_emitiu()
    {
        var lia = await SignUpAsync();
        var clientId = await Flow.RegisterAsync();
        var verifier = OAuthFlow.NewVerifier();
        var ticket = await Flow.TicketAsync(lia.AccessToken, clientId, OAuthFlow.ChallengeOf(verifier));
        var code = await OAuthFlow.CodeFromAsync(await Flow.Raw.GetAsync(OAuthFlow.AuthorizeUrl(clientId, OAuthFlow.ChallengeOf(verifier), ticket)));

        var tokens = await OAuthFlow.TokensAsync(await Flow.ExchangeAsync(clientId, code, verifier));
        var reuse = await Flow.ExchangeAsync(clientId, code, verifier);

        reuse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await reuse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().ShouldBe("invalid_grant");
        (await Flow.WhoAmIAsync(tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_exige_cliente_conhecido_e_grant_suportado()
    {
        var response = await Flow.Raw.PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = "cliente-que-nao-existe",
        }));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().ShouldBeOneOf("invalid_client", "unsupported_grant_type");
    }

    [Fact]
    public async Task Refresh_rotaciona_o_token_e_o_anterior_deixa_de_valer()
    {
        var lia = await SignUpAsync();
        var (clientId, first) = await Flow.ConnectAsync(lia.AccessToken);

        var second = await OAuthFlow.TokensAsync(await Flow.RefreshAsync(clientId, first.RefreshToken));

        second.RefreshToken.ShouldNotBe(first.RefreshToken);
        second.AccessToken.ShouldNotBe(first.AccessToken);
        (await WhoAmIOkAsync(second.AccessToken)).UserId.ShouldBe(lia.Session.User.Id);

        // Passada a tolerância de reenvio do OpenIddict (30 s, para respostas perdidas), reapresentar o refresh antigo é reuso:
        // falha e encerra a cadeia inteira.
        _clock.UtcNow += TimeSpan.FromSeconds(31);
        var reuse = await Flow.RefreshAsync(clientId, first.RefreshToken);
        reuse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await reuse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().ShouldBe("invalid_grant");
        (await Flow.WhoAmIAsync(second.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Access_token_vence_em_1_hora_e_o_refresh_em_90_dias()
    {
        var lia = await SignUpAsync();
        var (clientId, tokens) = await Flow.ConnectAsync(lia.AccessToken);

        _clock.UtcNow += TimeSpan.FromMinutes(61);
        (await Flow.WhoAmIAsync(tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var renewed = await OAuthFlow.TokensAsync(await Flow.RefreshAsync(clientId, tokens.RefreshToken));
        (await WhoAmIOkAsync(renewed.AccessToken)).UserId.ShouldBe(lia.Session.User.Id);

        _clock.UtcNow += TimeSpan.FromDays(91);
        (await Flow.RefreshAsync(clientId, renewed.RefreshToken)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---- Revogação (SC-004, US1.3) ----

    [Fact]
    public async Task Revogar_a_autorizacao_bloqueia_o_acesso_na_proxima_chamada_e_o_refresh()
    {
        var lia = await SignUpAsync();
        var leo = await SignUpAsync("Léo");
        var (clientId, liaTokens) = await Flow.ConnectAsync(lia.AccessToken);
        var (_, leoTokens) = await Flow.ConnectAsync(leo.AccessToken);
        (await Flow.WhoAmIAsync(liaTokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var revoker = scope.ServiceProvider.GetRequiredService<IOAuthAuthorizationRevoker>();
            await revoker.RevokeAllAsync(lia.Session.User.Id, CancellationToken.None);
        }

        (await Flow.WhoAmIAsync(liaTokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Flow.RefreshAsync(clientId, liaTokens.RefreshToken)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Só as conexões de quem foi revogado.
        (await Flow.WhoAmIAsync(leoTokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Revogar_so_a_autorizacao_ja_bloqueia_o_access_token()
    {
        var lia = await SignUpAsync();
        var (_, tokens) = await Flow.ConnectAsync(lia.AccessToken);

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
            await foreach (var authorization in authorizations.FindBySubjectAsync(lia.Session.User.Id.ToString()))
            {
                await authorizations.TryRevokeAsync(authorization);
            }
        }

        (await Flow.WhoAmIAsync(tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Trocar_a_senha_revoga_as_autorizacoes_do_conector()
    {
        var lia = await SignUpAsync();
        var (_, tokens) = await Flow.ConnectAsync(lia.AccessToken);
        (await Flow.WhoAmIAsync(tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var app = Factory.CreateClient().WithBearer(lia.AccessToken);
        var change = await app.PostAsJsonAsync("/api/v1/me/password", new { currentPassword = lia.Password, newPassword = "outra-senha-456" });
        change.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Flow.WhoAmIAsync(tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Redefinir_a_senha_revoga_as_autorizacoes_do_conector()
    {
        var lia = await SignUpAsync();
        var (_, tokens) = await Flow.ConnectAsync(lia.AccessToken);
        var other = await SignUpAsync("Bruna");
        var (_, otherTokens) = await Flow.ConnectAsync(other.AccessToken);

        (await Flow.Raw.PostAsJsonAsync("/api/v1/auth/password-reset/request", new { email = lia.Email })).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var text = Factory.Emails.Sent[^1].TextBody;
        var start = text.IndexOf("token=", StringComparison.Ordinal) + "token=".Length;
        var token = text[start..].Split('\n', '\r', ' ')[0];
        var confirm = await Flow.Raw.PostAsJsonAsync("/api/v1/auth/password-reset/confirm", new { token, newPassword = "nova-senha-456" });
        confirm.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Flow.WhoAmIAsync(tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Flow.WhoAmIAsync(otherTokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Logout_do_app_nao_derruba_o_conector()
    {
        var lia = await SignUpAsync();
        var (_, tokens) = await Flow.ConnectAsync(lia.AccessToken);

        using var app = Factory.CreateClient().WithBearer(lia.AccessToken);
        (await app.PostAsync("/api/v1/auth/logout", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Flow.WhoAmIAsync(tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
