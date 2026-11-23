using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Api.Endpoints;
using FinMy.Api.Tests.Support;
using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Identity;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinMy.Api.Tests.Auth;

/// <summary>T022: <c>POST /api/v1/auth/refresh</c> e <c>/logout</c> — cenários 2.3 e 2.4 da US2, com relógio controlado.</summary>
public class RefreshAndLogoutTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private const string RefreshPath = "/api/v1/auth/refresh";
    private const string LogoutPath = "/api/v1/auth/logout";

    private readonly FakeClock _clock = new(DateTimeOffset.UtcNow);

    protected override IClock? Clock => _clock;

    protected override IEndpointModule[] ExtraEndpoints => [new AuthProbeEndpoints()];

    private static string? CookieValue(HttpResponseMessage response)
    {
        var header = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(c => c.StartsWith("finmy_rt=", StringComparison.Ordinal))
            : null;
        return header?.Split(';')[0]["finmy_rt=".Length..];
    }

    private async Task<(TestAccount Account, string RefreshToken)> SignUpWithCookieAsync()
    {
        var email = $"maria.{Guid.NewGuid():N}@exemplo.com";
        var response = await Client.PostAsJsonAsync("/api/v1/auth/signup", new { name = "Maria", email, password = AccountHelpers.DefaultPassword });
        response.EnsureSuccessStatusCode();
        var session = (await response.Content.ReadFromJsonAsync<SessionResponse>(JsonDefaults.Options))!;
        return (new TestAccount(email, AccountHelpers.DefaultPassword, session), CookieValue(response)!);
    }

    private Task<HttpResponseMessage> PostWithCookieAsync(string path, string? token)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, path);
        if (token is not null)
        {
            message.Headers.Add("Cookie", $"finmy_rt={token}");
        }

        return Client.SendAsync(message);
    }

    private static async Task<string> CodeAsync(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("code").GetString()!;
    }

    private async Task<RefreshToken[]> TokensAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMyDbContext>();
        return await db.Set<RefreshToken>().AsNoTracking().OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).ToArrayAsync();
    }

    // --- Cenário 2.3: dentro de 90 dias desde o último uso continua logada ---

    [Fact]
    public async Task Cenario_2_3_refresh_devolve_novo_access_token_e_novo_cookie_e_o_anterior_deixa_de_valer()
    {
        var (account, token) = await SignUpWithCookieAsync();
        _clock.UtcNow = _clock.UtcNow.AddDays(1);

        var response = await PostWithCookieAsync(RefreshPath, token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var rotated = CookieValue(response);
        rotated.ShouldNotBeNullOrWhiteSpace();
        rotated.ShouldNotBe(token);
        response.Headers.GetValues("Set-Cookie").Single().ShouldContain("httponly", Case.Insensitive);

        var session = (await response.Content.ReadFromJsonAsync<SessionResponse>(JsonDefaults.Options))!;
        session.AccessToken.ShouldNotBeNullOrWhiteSpace();
        session.User.Id.ShouldBe(account.Session.User.Id);
        session.Tenant.Id.ShouldBe(account.Session.Tenant.Id);
        session.Tenant.Role.ShouldBe("owner");

        // O novo cookie renova de novo; o antigo, já trocado, é reuso.
        (await PostWithCookieAsync(RefreshPath, rotated)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Cenario_2_3_cada_uso_prolonga_a_validade_em_90_dias_a_partir_dele()
    {
        var (_, token) = await SignUpWithCookieAsync();
        var start = _clock.UtcNow;

        // Usa a cada 60 dias: 180 dias depois da entrada continua logada (validade deslizante).
        for (var i = 0; i < 3; i++)
        {
            _clock.UtcNow = _clock.UtcNow.AddDays(60);
            var response = await PostWithCookieAsync(RefreshPath, token);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            token = CookieValue(response)!;
        }

        var tokens = await TokensAsync();
        var current = tokens.Single(t => t.RevokedAt is null);
        current.ExpiresAt.ShouldBe(start.AddDays(180).AddDays(90), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Cenario_2_3_o_refresh_mantem_a_mesma_sessao_do_access_token()
    {
        var (_, token) = await SignUpWithCookieAsync();

        var response = await PostWithCookieAsync(RefreshPath, token);
        var session = (await response.Content.ReadFromJsonAsync<SessionResponse>(JsonDefaults.Options))!;

        using var authenticated = Factory.CreateClient().WithBearer(session.AccessToken);
        (await authenticated.GetAsync("/test/whoami")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TokensAsync()).Select(t => t.FamilyId).Distinct().Count().ShouldBe(1);
    }

    // --- Cenário 2.4: mais de 90 dias sem usar → precisa entrar de novo ---

    [Fact]
    public async Task Cenario_2_4_apos_90_dias_sem_uso_responde_401_session_expired()
    {
        var (_, token) = await SignUpWithCookieAsync();
        _clock.UtcNow = _clock.UtcNow.AddDays(90).AddSeconds(1);

        var response = await PostWithCookieAsync(RefreshPath, token);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodeAsync(response)).ShouldBe("session_expired");
    }

    [Fact]
    public async Task Cenario_2_4_um_dia_antes_dos_90_dias_ainda_renova()
    {
        var (_, token) = await SignUpWithCookieAsync();
        _clock.UtcNow = _clock.UtcNow.AddDays(89);

        (await PostWithCookieAsync(RefreshPath, token)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Sem_cookie_ou_com_token_desconhecido_responde_401_session_expired()
    {
        var semCookie = await PostWithCookieAsync(RefreshPath, null);
        semCookie.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodeAsync(semCookie)).ShouldBe("session_expired");

        var desconhecido = await PostWithCookieAsync(RefreshPath, "token-que-nunca-existiu");
        desconhecido.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodeAsync(desconhecido)).ShouldBe("session_expired");
    }

    // --- Reuso de token rotacionado ---

    [Fact]
    public async Task Reuso_de_token_rotacionado_responde_refresh_reused_e_revoga_a_sessao_inteira()
    {
        var (_, first) = await SignUpWithCookieAsync();
        var second = CookieValue(await PostWithCookieAsync(RefreshPath, first))!;

        var reuse = await PostWithCookieAsync(RefreshPath, first);

        reuse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodeAsync(reuse)).ShouldBe("refresh_reused");

        // O token novo também caiu: a família toda foi revogada.
        var afterwards = await PostWithCookieAsync(RefreshPath, second);
        afterwards.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodeAsync(afterwards)).ShouldBe("session_revoked");

        var tokens = await TokensAsync();
        tokens.ShouldAllBe(t => t.RevokedAt != null);
        tokens.Last().RevokedReason.ShouldBe(RefreshTokenRevocationReason.ReuseDetected);
    }

    [Fact]
    public async Task Reuso_derruba_o_access_token_da_sessao_na_requisicao_seguinte()
    {
        var (account, first) = await SignUpWithCookieAsync();
        await PostWithCookieAsync(RefreshPath, first);
        await PostWithCookieAsync(RefreshPath, first);

        using var authenticated = Factory.CreateClient().WithBearer(account.AccessToken);
        var response = await authenticated.GetAsync("/test/whoami");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodeAsync(response)).ShouldBe("session_revoked");
    }

    [Fact]
    public async Task Reuso_numa_sessao_nao_afeta_outra_sessao_da_mesma_pessoa()
    {
        var (account, first) = await SignUpWithCookieAsync();
        var otherLogin = await Client.PostAsJsonAsync("/api/v1/auth/login", new { email = account.Email, password = account.Password });
        var other = CookieValue(otherLogin)!;

        await PostWithCookieAsync(RefreshPath, first);
        await PostWithCookieAsync(RefreshPath, first);

        (await PostWithCookieAsync(RefreshPath, other)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // --- Logout ---

    [Fact]
    public async Task Logout_responde_204_apaga_o_cookie_e_encerra_so_esta_sessao()
    {
        var (account, token) = await SignUpWithCookieAsync();
        var otherLogin = await Client.PostAsJsonAsync("/api/v1/auth/login", new { email = account.Email, password = account.Password });
        var other = CookieValue(otherLogin)!;

        var response = await PostWithCookieAsync(LogoutPath, token);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var cleared = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("finmy_rt=", StringComparison.Ordinal));
        cleared.ShouldContain("expires=Thu, 01 Jan 1970", Case.Insensitive);
        cleared.ShouldContain("path=/api/v1/auth", Case.Insensitive);

        var refresh = await PostWithCookieAsync(RefreshPath, token);
        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodeAsync(refresh)).ShouldBe("session_revoked");

        (await TokensAsync()).Where(t => t.RevokedReason == RefreshTokenRevocationReason.Logout).ShouldNotBeEmpty();
        (await PostWithCookieAsync(RefreshPath, other)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Logout_vale_na_requisicao_seguinte_para_o_access_token()
    {
        var (account, token) = await SignUpWithCookieAsync();

        await PostWithCookieAsync(LogoutPath, token);

        using var authenticated = Factory.CreateClient().WithBearer(account.AccessToken);
        var response = await authenticated.GetAsync("/test/whoami");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodeAsync(response)).ShouldBe("session_revoked");
    }

    [Fact]
    public async Task Logout_e_idempotente_sem_cookie_com_token_desconhecido_e_repetido()
    {
        var (_, token) = await SignUpWithCookieAsync();

        (await PostWithCookieAsync(LogoutPath, null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await PostWithCookieAsync(LogoutPath, "token-que-nunca-existiu")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await PostWithCookieAsync(LogoutPath, token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await PostWithCookieAsync(LogoutPath, token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
}
