using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Api.Endpoints;
using FinMy.Api.Tests.Support;
using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Common;
using FinMy.Domain.Identity;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinMy.Api.Tests.Auth;

/// <summary>US5: nome, senha e nome do Espaço (cenários 5.1 e 5.3 e o Edge Case de dois aparelhos).</summary>
public class ProfileEndpointsTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private const string Me = "/api/v1/me";
    private const string Password = "/api/v1/me/password";
    private const string Tenant = "/api/v1/tenant";
    private const string NewPassword = "nova-senha-456";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override IEndpointModule[] ExtraEndpoints => [new AuthProbeEndpoints()];

    private async Task<(HttpClient Client, TestAccount Account)> SignedInAsync()
    {
        using var anonymous = Factory.CreateClient();
        var account = await anonymous.SignUpAsync(name: "Maria Teste");
        return (Factory.CreateClient().WithBearer(account.AccessToken), account);
    }

    /// <summary>Segunda sessão da mesma pessoa (outro aparelho), emitida como o login faria.</summary>
    private async Task<(HttpClient Client, Guid SessionId)> SecondDeviceAsync(TestAccount account)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var refresh = await scope.ServiceProvider.GetRequiredService<IRefreshTokenStore>()
            .IssueAsync(account.Session.User.Id, null, "Outro aparelho", CancellationToken.None);
        var token = scope.ServiceProvider.GetRequiredService<ITokenService>().IssueAccessToken(
            new AccessTokenRequest(account.Session.User.Id, account.Session.Tenant.Id, refresh.SessionId, account.Session.User.Name, Channel.App)).Token;
        return (Factory.CreateClient().WithBearer(token), refresh.SessionId);
    }

    private async Task<T> InDbAsync<T>(Func<FinMyDbContext, Task<T>> query)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<FinMyDbContext>());
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>(Json);

    [Fact]
    public async Task Alterar_o_nome_responde_200_com_usuario_e_espaco_e_mantem_o_nome_do_espaco()
    {
        var (client, account) = await SignedInAsync();

        var response = await client.PatchAsJsonAsync(Me, new { name = "  Maria Souza  " });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await ReadAsync(response);
        body.GetProperty("user").GetProperty("name").GetString().ShouldBe("Maria Souza");
        body.GetProperty("user").GetProperty("email").GetString().ShouldBe(account.Email);
        body.GetProperty("tenant").GetProperty("name").GetString().ShouldBe(account.Session.Tenant.Name);
        body.GetProperty("tenant").GetProperty("role").GetString().ShouldBe("owner");
        (await InDbAsync(db => db.Set<User>().Where(u => u.Id == account.Session.User.Id).Select(u => u.Name).SingleAsync())).ShouldBe("Maria Souza");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Alterar_o_nome_vazio_e_422(string? name)
    {
        var (client, _) = await SignedInAsync();

        var response = await client.PatchAsJsonAsync(Me, new { name });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await ReadAsync(response);
        problem.GetProperty("code").GetString().ShouldBe("validation_failed");
        problem.GetProperty("errors").TryGetProperty("name", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Alterar_o_nome_com_mais_de_60_caracteres_e_422()
    {
        var (client, _) = await SignedInAsync();

        var response = await client.PatchAsJsonAsync(Me, new { name = new string('a', 61) });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Sem_token_os_tres_endpoints_respondem_401()
    {
        (await Client.PatchAsJsonAsync(Me, new { name = "X" })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Client.PostAsJsonAsync(Password, new { currentPassword = "a", newPassword = NewPassword })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Client.PatchAsJsonAsync(Tenant, new { name = "X" })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Cenario_5_3_renomear_o_espaco_responde_200_e_grava_a_auditoria()
    {
        var (client, account) = await SignedInAsync();

        var response = await client.PatchAsJsonAsync(Tenant, new { name = "  Casa da Maria " });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await ReadAsync(response);
        body.GetProperty("id").GetString().ShouldBe(account.Session.Tenant.Id.ToString());
        body.GetProperty("name").GetString().ShouldBe("Casa da Maria");
        body.GetProperty("role").GetString().ShouldBe("owner");

        var tenant = await InDbAsync(db => db.Set<Domain.Identity.Tenant>().SingleAsync(t => t.Id == account.Session.Tenant.Id));
        tenant.Name.ShouldBe("Casa da Maria");
        tenant.UpdatedBy.ShouldBe(account.Session.User.Id);
        tenant.UpdatedVia.ShouldBe(Channel.App);
    }

    [Fact]
    public async Task Renomear_o_espaco_com_nome_invalido_e_422()
    {
        var (client, _) = await SignedInAsync();

        (await client.PatchAsJsonAsync(Tenant, new { name = " " })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await client.PatchAsJsonAsync(Tenant, new { name = new string('a', 61) })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Cenario_5_1_trocar_a_senha_responde_204_a_nova_vale_e_a_antiga_nao()
    {
        var (client, account) = await SignedInAsync();
        var before = await InDbAsync(db => db.Set<User>().Where(u => u.Id == account.Session.User.Id).Select(u => new { u.PasswordHash, u.SecurityStamp }).SingleAsync());

        var response = await client.PostAsJsonAsync(Password, new { currentPassword = account.Password, newPassword = NewPassword });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var after = await InDbAsync(db => db.Set<User>().Where(u => u.Id == account.Session.User.Id).Select(u => new { u.PasswordHash, u.SecurityStamp }).SingleAsync());
        after.SecurityStamp.ShouldNotBe(before.SecurityStamp);
        var hasher = Factory.Services.GetRequiredService<IPasswordHasher>();
        hasher.Verify(after.PasswordHash, NewPassword).ShouldNotBe(PasswordCheck.Failed);
        hasher.Verify(after.PasswordHash, account.Password).ShouldBe(PasswordCheck.Failed);
    }

    [Fact]
    public async Task Senha_atual_errada_e_422_current_password_incorrect_e_nada_muda()
    {
        var (client, account) = await SignedInAsync();
        var (other, _) = await SecondDeviceAsync(account);
        var before = await InDbAsync(db => db.Set<User>().Where(u => u.Id == account.Session.User.Id).Select(u => u.PasswordHash).SingleAsync());

        var response = await client.PostAsJsonAsync(Password, new { currentPassword = "senha-errada-999", newPassword = NewPassword });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var problem = await ReadAsync(response);
        problem.GetProperty("code").GetString().ShouldBe("current_password_incorrect");
        problem.GetProperty("errors").TryGetProperty("currentPassword", out _).ShouldBeTrue();
        (await InDbAsync(db => db.Set<User>().Where(u => u.Id == account.Session.User.Id).Select(u => u.PasswordHash).SingleAsync())).ShouldBe(before);
        (await other.PatchAsJsonAsync(Me, new { name = "Ainda vale" })).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(null, NewPassword, "currentPassword")]
    [InlineData("senha-de-teste-123", "curta", "newPassword")]
    [InlineData("senha-de-teste-123", null, "newPassword")]
    public async Task Troca_de_senha_com_campos_invalidos_e_422_validation_failed(string? current, string? next, string field)
    {
        var (client, _) = await SignedInAsync();

        var response = await client.PostAsJsonAsync(Password, new { currentPassword = current, newPassword = next });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await ReadAsync(response);
        problem.GetProperty("code").GetString().ShouldBe("validation_failed");
        problem.GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Edge_case_dois_aparelhos_trocar_a_senha_encerra_o_outro_na_proxima_acao_e_mantem_este()
    {
        var (thisDevice, account) = await SignedInAsync();
        var (otherDevice, otherSession) = await SecondDeviceAsync(account);
        (await otherDevice.PatchAsJsonAsync(Me, new { name = "Maria Teste" })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var changed = await thisDevice.PostAsJsonAsync(Password, new { currentPassword = account.Password, newPassword = NewPassword });

        changed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // O outro aparelho cai na próxima ação...
        var revoked = await otherDevice.PatchAsJsonAsync(Me, new { name = "Digitado no outro aparelho" });
        revoked.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadAsync(revoked)).GetProperty("code").GetString().ShouldBe("session_revoked");
        (await InDbAsync(db => db.Set<User>().Where(u => u.Id == account.Session.User.Id).Select(u => u.Name).SingleAsync())).ShouldBe("Maria Teste");

        // ...e o aparelho que trocou a senha continua logado.
        (await thisDevice.PatchAsJsonAsync(Me, new { name = "Maria Souza" })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var tokens = await InDbAsync(db => db.Set<RefreshToken>().Where(t => t.UserId == account.Session.User.Id).ToListAsync());
        tokens.Where(t => t.FamilyId == otherSession).ShouldAllBe(t => t.RevokedReason == RefreshTokenRevocationReason.PasswordChanged);
        tokens.Where(t => t.FamilyId != otherSession).ShouldAllBe(t => t.RevokedAt == null);
    }
}
