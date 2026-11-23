using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Api.Endpoints;
using FinMy.Api.Tests.Support;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Identity;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinMy.Api.Tests.Auth;

/// <summary>T021: <c>POST /api/v1/auth/login</c> — cenários 2.1, 2.2 e 2.5 da US2.</summary>
public class LoginTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    protected override IEndpointModule[] ExtraEndpoints => [new AuthProbeEndpoints()];

    private const string Path = "/api/v1/auth/login";
    private const string Wrong = "senha-errada-999";

    private Task<HttpResponseMessage> LoginPostAsync(string email, string password) => Client.PostAsJsonAsync(Path, new { email, password });

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<LoginThrottle?> ThrottleAsync(string email)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMyDbContext>();
        var normalized = User.NormalizeEmail(email);
        return await db.Set<LoginThrottle>().AsNoTracking().SingleOrDefaultAsync(t => t.EmailNormalized == normalized);
    }

    private async Task ExecuteAsync(FormattableString sql)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMyDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(sql);
    }

    private async Task FailTimesAsync(string email, int times)
    {
        for (var i = 0; i < times; i++)
        {
            (await LoginPostAsync(email, Wrong)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
    }

    // --- Cenário 2.1: e-mail e senha corretos → entra no seu Espaço ---

    [Fact]
    public async Task Cenario_2_1_entra_no_proprio_Espaco_com_cookie_de_sessao_e_access_token_valido()
    {
        var account = await Client.SignUpAsync(name: "Maria Souza");

        var response = await LoginPostAsync(account.Email, account.Password);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        response.Headers.GetValues("Set-Cookie").ShouldContain(c => c.StartsWith("finmy_rt=", StringComparison.Ordinal));

        var session = (await response.Content.ReadFromJsonAsync<SessionResponse>(JsonDefaults.Options))!;
        session.User.Id.ShouldBe(account.Session.User.Id);
        session.Tenant.Id.ShouldBe(account.Session.Tenant.Id);
        session.Tenant.Role.ShouldBe("owner");
        session.AccessToken.ShouldNotBeNullOrWhiteSpace();
        session.AccessTokenExpiresAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow);

        using var authenticated = Factory.CreateClient().WithBearer(session.AccessToken);
        var whoami = await authenticated.GetAsync("/test/whoami");
        whoami.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await whoami.Content.ReadAsStringAsync()).ShouldContain(session.Tenant.Id.ToString());
    }

    [Fact]
    public async Task Cada_login_abre_uma_sessao_propria_e_o_corpo_nao_traz_refresh_nem_hash()
    {
        var account = await Client.SignUpAsync();

        var response = await LoginPostAsync(account.Email, account.Password);
        var body = await response.Content.ReadAsStringAsync();
        var cookie = response.Headers.GetValues("Set-Cookie").Single().Split(';')[0]["finmy_rt=".Length..];

        body.ShouldNotContain(cookie);
        body.ShouldNotContain("passwordHash", Case.Insensitive);

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMyDbContext>();
        (await db.Set<RefreshToken>().Select(r => r.FamilyId).Distinct().CountAsync()).ShouldBe(2); // cadastro + login
    }

    [Theory]
    [InlineData("MAIL@Exemplo.com", "mail@exemplo.com")]
    [InlineData("  mail@exemplo.com  ", "mail@exemplo.com")]
    public async Task Email_com_outras_maiusculas_ou_espacos_nas_pontas_e_o_mesmo_e_mail(string digitado, string cadastrado)
    {
        var account = await Client.SignUpAsync(email: cadastrado);

        var response = await LoginPostAsync(digitado, account.Password);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // --- Cenário 2.2: erro único, sem dizer qual dos dois está errado ---

    [Fact]
    public async Task Cenario_2_2_senha_errada_e_email_inexistente_dao_exatamente_a_mesma_resposta()
    {
        var account = await Client.SignUpAsync();

        var wrongPassword = await LoginPostAsync(account.Email, Wrong);
        var unknownEmail = await LoginPostAsync($"ninguem.{Guid.NewGuid():N}@exemplo.com", Wrong);

        wrongPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        unknownEmail.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        wrongPassword.Headers.Contains("Set-Cookie").ShouldBeFalse();
        unknownEmail.Headers.Contains("Set-Cookie").ShouldBeFalse();

        var a = await ReadJsonAsync(wrongPassword);
        var b = await ReadJsonAsync(unknownEmail);
        a.GetProperty("code").GetString().ShouldBe("invalid_credentials");
        b.GetProperty("code").GetString().ShouldBe("invalid_credentials");
        a.GetProperty("detail").GetString().ShouldBe("E-mail ou senha incorretos");
        b.GetProperty("detail").GetString().ShouldBe("E-mail ou senha incorretos");
        a.GetProperty("title").GetString().ShouldBe(b.GetProperty("title").GetString());
        a.TryGetProperty("retryAfterSeconds", out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null, "qualquer")]
    [InlineData("", "qualquer")]
    [InlineData("   ", "qualquer")]
    [InlineData("maria@exemplo.com", null)]
    [InlineData("maria@exemplo.com", "")]
    public async Task Campos_ausentes_ou_vazios_respondem_o_mesmo_erro_unico_e_nao_500(string? email, string? password)
    {
        var response = await Client.PostAsJsonAsync(Path, new { email, password });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadJsonAsync(response)).GetProperty("code").GetString().ShouldBe("invalid_credentials");
    }

    [Fact]
    public async Task Corpo_vazio_responde_o_erro_unico()
    {
        var response = await Client.PostAsJsonAsync(Path, new { });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Email_inexistente_tambem_ganha_registro_de_tentativas()
    {
        var email = $"ninguem.{Guid.NewGuid():N}@exemplo.com";

        await FailTimesAsync(email, 2);

        (await ThrottleAsync(email))!.FailedCount.ShouldBe(2);
    }

    // --- Cenário 2.5: 5 erradas seguidas → a 6ª é bloqueada por 15 minutos ---

    [Fact]
    public async Task Cenario_2_5_cinco_erradas_dao_401_e_a_sexta_responde_429_login_locked_com_tempo_de_espera()
    {
        var account = await Client.SignUpAsync();

        await FailTimesAsync(account.Email, 5);
        var sixth = await LoginPostAsync(account.Email, Wrong);

        sixth.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        var problem = await ReadJsonAsync(sixth);
        problem.GetProperty("code").GetString().ShouldBe("login_locked");
        problem.GetProperty("retryAfterSeconds").GetInt32().ShouldBeInRange(890, 900);
        sixth.Headers.Contains("Set-Cookie").ShouldBeFalse();
    }

    [Fact]
    public async Task Durante_o_bloqueio_ate_a_senha_certa_e_recusada_e_nao_abre_sessao()
    {
        var account = await Client.SignUpAsync();
        await FailTimesAsync(account.Email, 5);

        var response = await LoginPostAsync(account.Email, account.Password);

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await ReadJsonAsync(response)).GetProperty("code").GetString().ShouldBe("login_locked");
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
    }

    [Fact]
    public async Task Quatro_erradas_nao_bloqueiam_e_a_senha_certa_entra()
    {
        var account = await Client.SignUpAsync();
        await FailTimesAsync(account.Email, 4);

        var response = await LoginPostAsync(account.Email, account.Password);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_com_sucesso_zera_a_contagem()
    {
        var account = await Client.SignUpAsync();
        await FailTimesAsync(account.Email, 4);

        (await LoginPostAsync(account.Email, account.Password)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ThrottleAsync(account.Email)).ShouldBeNull();

        await FailTimesAsync(account.Email, 4); // recomeça do zero: ainda não bloqueia
        (await LoginPostAsync(account.Email, account.Password)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task O_bloqueio_vale_so_para_o_email_que_errou()
    {
        var blocked = await Client.SignUpAsync();
        var other = await Client.SignUpAsync();
        await FailTimesAsync(blocked.Email, 5);

        (await LoginPostAsync(other.Email, other.Password)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Email_inexistente_tambem_e_bloqueado_na_sexta_tentativa_com_a_mesma_resposta_de_uma_conta_real()
    {
        var email = $"ninguem.{Guid.NewGuid():N}@exemplo.com";
        await FailTimesAsync(email, 5);

        var response = await LoginPostAsync(email, Wrong);

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await ReadJsonAsync(response)).GetProperty("code").GetString().ShouldBe("login_locked");
    }

    [Fact]
    public async Task Maiusculas_e_espacos_no_email_contam_na_mesma_contagem()
    {
        var account = await Client.SignUpAsync(email: "mix@exemplo.com");

        await FailTimesAsync("MIX@exemplo.com", 2);
        await FailTimesAsync(" mix@exemplo.com ", 3);

        (await LoginPostAsync(account.Email, account.Password)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Depois_dos_15_minutos_do_bloqueio_a_pessoa_volta_a_entrar()
    {
        var account = await Client.SignUpAsync();
        await FailTimesAsync(account.Email, 5);
        var email = account.Email;
        await ExecuteAsync($"UPDATE login_throttles SET locked_until = now() - interval '1 second', window_started_at = now() - interval '16 minutes' WHERE email_normalized = {email}");

        var response = await LoginPostAsync(account.Email, account.Password);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Falhas_de_uma_janela_velha_nao_somam_com_as_novas()
    {
        var account = await Client.SignUpAsync();
        await FailTimesAsync(account.Email, 4);
        var email = account.Email;
        await ExecuteAsync($"UPDATE login_throttles SET window_started_at = now() - interval '16 minutes' WHERE email_normalized = {email}");

        await FailTimesAsync(account.Email, 1);

        var throttle = (await ThrottleAsync(account.Email))!;
        throttle.FailedCount.ShouldBe(1);
        throttle.LockedUntil.ShouldBeNull();
        (await LoginPostAsync(account.Email, account.Password)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Tentativas_simultaneas_nao_perdem_contagem()
    {
        var account = await Client.SignUpAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => LoginPostAsync(account.Email, Wrong)));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Unauthorized);
        var throttle = (await ThrottleAsync(account.Email))!;
        throttle.FailedCount.ShouldBe(5);
        throttle.LockedUntil.ShouldNotBeNull();
    }
}
