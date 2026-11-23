using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Api.Tests.Support;
using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Identity;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinMy.Api.Tests.Isolation;

/// <summary>
/// docs/arquitetura.md §3.6 (parte c): o <c>tid</c> do access token só é emitido para quem tem <c>tenant_members</c>
/// (e o refresh confere de novo). Autorização = "participa do Espaço do contexto?".
/// </summary>
public class TenantClaimTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private static string? Tid(string accessToken) => JwtClaims.Get(accessToken, "tid");

    private static string RefreshCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("finmy_rt=", StringComparison.Ordinal)).Split(';')[0];

    private async Task<bool> IsMemberAsync(Guid tenantId, Guid userId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FinMyDbContext>().Set<TenantMember>()
            .AnyAsync(m => m.TenantId == tenantId && m.UserId == userId);
    }

    private async Task RemoveMembershipAsync(Guid userId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<FinMyDbContext>().Set<TenantMember>()
            .Where(m => m.UserId == userId).ExecuteDeleteAsync();
    }

    private Task<HttpResponseMessage> RefreshAsync(string cookie)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        message.Headers.Add("Cookie", cookie);
        return Client.SendAsync(message);
    }

    private static async Task<string> CodeAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString()!;

    [Fact]
    public async Task Cadastro_emite_tid_do_Espaco_do_qual_a_pessoa_participa()
    {
        var account = await Client.SignUpAsync();

        Tid(account.AccessToken).ShouldBe(account.Session.Tenant.Id.ToString());
        (await IsMemberAsync(account.Session.Tenant.Id, account.Session.User.Id)).ShouldBeTrue();
    }

    [Fact]
    public async Task Login_e_refresh_emitem_tid_do_Espaco_com_participacao()
    {
        var account = await Client.SignUpAsync();

        var login = await Client.PostAsJsonAsync("/api/v1/auth/login", new { email = account.Email, password = account.Password });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var session = (await login.Content.ReadFromJsonAsync<SessionResponse>(JsonDefaults.Options))!;
        Tid(session.AccessToken).ShouldBe(account.Session.Tenant.Id.ToString());

        var refreshed = await RefreshAsync(RefreshCookie(login));
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var renewed = (await refreshed.Content.ReadFromJsonAsync<SessionResponse>(JsonDefaults.Options))!;
        Tid(renewed.AccessToken).ShouldBe(account.Session.Tenant.Id.ToString());
    }

    [Fact]
    public async Task Quem_nao_tem_participacao_nao_recebe_token_no_login()
    {
        var email = $"sem.espaco.{Guid.NewGuid():N}@exemplo.com";
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var hash = scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(AccountHelpers.DefaultPassword);
            var db = scope.ServiceProvider.GetRequiredService<FinMyDbContext>();
            db.Add(new User("Sem Espaço", email, hash, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        var response = await Client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = AccountHelpers.DefaultPassword });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("accessToken");
    }

    [Fact]
    public async Task Participacao_removida_impede_novo_token_no_login_e_no_refresh()
    {
        var account = await Client.SignUpAsync();
        var login = await Client.PostAsJsonAsync("/api/v1/auth/login", new { email = account.Email, password = account.Password });
        var cookie = RefreshCookie(login);

        await RemoveMembershipAsync(account.Session.User.Id);

        var refresh = await RefreshAsync(cookie);
        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await CodeAsync(refresh)).ShouldBe("session_revoked");
        (await refresh.Content.ReadAsStringAsync()).ShouldNotContain("accessToken");

        var again = await Client.PostAsJsonAsync("/api/v1/auth/login", new { email = account.Email, password = account.Password });
        again.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await again.Content.ReadAsStringAsync()).ShouldNotContain("accessToken");
    }
}
