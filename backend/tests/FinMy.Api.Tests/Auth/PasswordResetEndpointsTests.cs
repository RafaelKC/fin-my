using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Api.Endpoints;
using FinMy.Api.Tests.Support;
using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Identity;
using FinMy.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace FinMy.Api.Tests.Auth;

public class PasswordResetEndpointsTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private const string Request = "/api/v1/auth/password-reset/request";
    private const string Confirm = "/api/v1/auth/password-reset/confirm";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?> { ["APP_URL"] = "https://finmy.exemplo.com" };

    protected override IEndpointModule[] ExtraEndpoints => [new AuthProbeEndpoints()];

    private async Task<User> SeedUserAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMyDbContext>();
        var hash = scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash("senha-antiga-123");
        var user = new User("Maria Teste", $"maria.{Guid.NewGuid():N}@exemplo.com", hash, DateTimeOffset.UtcNow);
        db.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private string LastToken()
    {
        var text = Factory.Emails.Sent[^1].TextBody;
        var start = text.IndexOf("token=", StringComparison.Ordinal) + "token=".Length;
        return text[start..].Split('\n', '\r', ' ')[0];
    }

    [Fact]
    public async Task Pedido_responde_202_com_a_mesma_mensagem_exista_ou_nao_a_conta()
    {
        var user = await SeedUserAsync();

        var existing = await Client.PostAsJsonAsync(Request, new { email = user.Email });
        var missing = await Client.PostAsJsonAsync(Request, new { email = "ninguem@exemplo.com" });

        existing.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        missing.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var a = await existing.Content.ReadAsStringAsync();
        var b = await missing.Content.ReadAsStringAsync();
        a.ShouldBe(b);
        JsonDocument.Parse(a).RootElement.GetProperty("detail").GetString().ShouldBe("Se houver conta com este e-mail, enviaremos um link");

        var mail = Factory.Emails.Sent.ShouldHaveSingleItem();
        mail.To.ShouldBe(user.Email);
        mail.TextBody.ShouldContain("https://finmy.exemplo.com/redefinir-senha?token=");
    }

    [Fact]
    public async Task Pedido_com_SMTP_fora_do_ar_ainda_responde_202_igual_ao_de_conta_inexistente()
    {
        var user = await SeedUserAsync();
        Factory.Emails.Failure = new IOException("SMTP fora do ar");

        var existing = await Client.PostAsJsonAsync(Request, new { email = user.Email });
        var missing = await Client.PostAsJsonAsync(Request, new { email = "ninguem@exemplo.com" });

        existing.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        missing.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await existing.Content.ReadAsStringAsync()).ShouldBe(await missing.Content.ReadAsStringAsync());
        Factory.Emails.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Pedido_sem_e_mail_e_422_validation_failed()
    {
        var response = await Client.PostAsJsonAsync(Request, new { });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        problem.GetProperty("code").GetString().ShouldBe("validation_failed");
        problem.GetProperty("errors").TryGetProperty("email", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Confirmar_responde_204_encerra_a_sessao_e_o_link_nao_vale_de_novo()
    {
        var session = await Factory.CreateSessionAsync();
        var email = await EmailOfAsync(session.Request.UserId);
        (await Client.PostAsJsonAsync(Request, new { email })).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var token = LastToken();

        using (var before = new HttpRequestMessage(HttpMethod.Get, "/test/whoami"))
        {
            before.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.AccessToken);
            (await Client.SendAsync(before)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var confirmed = await Client.PostAsJsonAsync(Confirm, new { token, newPassword = "nova-senha-456" });

        confirmed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using (var after = new HttpRequestMessage(HttpMethod.Get, "/test/whoami"))
        {
            after.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.AccessToken);
            var revoked = await Client.SendAsync(after);
            revoked.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await revoked.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("code").GetString().ShouldBe("session_revoked");
        }

        var again = await Client.PostAsJsonAsync(Confirm, new { token, newPassword = "outra-senha-789" });
        again.StatusCode.ShouldBe(HttpStatusCode.Gone);
        again.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        (await again.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("code").GetString().ShouldBe("reset_token_invalid");
    }

    [Fact]
    public async Task Confirmar_com_token_inexistente_e_410()
    {
        var response = await Client.PostAsJsonAsync(Confirm, new { token = "nao-existe", newPassword = "nova-senha-456" });

        response.StatusCode.ShouldBe(HttpStatusCode.Gone);
    }

    [Fact]
    public async Task Confirmar_com_senha_curta_e_422()
    {
        var response = await Client.PostAsJsonAsync(Confirm, new { token = "qualquer", newPassword = "curta" });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        problem.GetProperty("code").GetString().ShouldBe("validation_failed");
        problem.GetProperty("errors").TryGetProperty("newPassword", out _).ShouldBeTrue();
    }

    private async Task<string> EmailOfAsync(Guid userId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMyDbContext>();
        return (await db.Set<User>().FindAsync(userId))!.Email;
    }
}
