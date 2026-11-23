using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Api.Endpoints;
using FinMy.Api.Tests.Support;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Common;
using FinMy.Domain.Identity;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinMy.Api.Tests.Auth;

/// <summary>T019: <c>POST /api/v1/auth/signup</c> — cenários 1.1 a 1.3 da US1 e os Edge Cases de e-mail.</summary>
public class SignUpTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private const string Path = "/api/v1/auth/signup";

    protected override IEndpointModule[] ExtraEndpoints => [new AuthProbeEndpoints()];

    private static string NewEmail() => $"maria.{Guid.NewGuid():N}@exemplo.com";

    private Task<HttpResponseMessage> PostAsync(object body) => Client.PostAsJsonAsync(Path, body);

    private async Task<(int Users, int Tenants, int Members)> CountsAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMyDbContext>();
        return (await db.Set<User>().CountAsync(), await db.Set<Tenant>().CountAsync(), await db.Set<TenantMember>().CountAsync());
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    // --- Cenário 1.1: e-mail novo + senha válida → conta, Espaço "Finanças de <primeiro nome>", dona, já logada ---

    [Fact]
    public async Task Cenario_1_1_cria_usuario_espaco_e_participacao_e_ja_entra()
    {
        var email = NewEmail();

        var response = await PostAsync(new { name = "Maria Souza", email, password = AccountHelpers.DefaultPassword });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var session = (await response.Content.ReadFromJsonAsync<SessionResponse>(JsonDefaults.Options))!;
        session.AccessToken.ShouldNotBeNullOrWhiteSpace();
        session.AccessTokenExpiresAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
        session.User.Name.ShouldBe("Maria Souza");
        session.User.Email.ShouldBe(email);
        session.Tenant.Name.ShouldBe("Finanças de Maria");
        session.Tenant.Role.ShouldBe("owner");

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMyDbContext>();

        var user = await db.Set<User>().SingleAsync();
        user.Id.ShouldBe(session.User.Id);
        user.PasswordHash.ShouldNotBe(AccountHelpers.DefaultPassword);
        user.PasswordHash.ShouldNotContain(AccountHelpers.DefaultPassword);

        var tenant = await db.Set<Tenant>().SingleAsync();
        tenant.Id.ShouldBe(session.Tenant.Id);
        tenant.CreatedBy.ShouldBe(user.Id);
        tenant.UpdatedBy.ShouldBe(user.Id);
        tenant.CreatedVia.ShouldBe(Channel.App);

        var member = await db.Set<TenantMember>().SingleAsync();
        member.UserId.ShouldBe(user.Id);
        member.TenantId.ShouldBe(tenant.Id);
        member.Role.ShouldBe(TenantMemberRole.Owner);

        var refresh = await db.Set<RefreshToken>().SingleAsync();
        refresh.UserId.ShouldBe(user.Id);
    }

    [Fact]
    public async Task Cenario_1_1_define_o_cookie_de_sessao_e_o_access_token_vale_no_Espaco_criado()
    {
        var response = await PostAsync(new { name = "Maria Souza", email = NewEmail(), password = AccountHelpers.DefaultPassword });
        var session = (await response.Content.ReadFromJsonAsync<SessionResponse>(JsonDefaults.Options))!;

        response.Headers.GetValues("Set-Cookie").ShouldContain(c => c.StartsWith("finmy_rt=", StringComparison.Ordinal));

        using var authenticated = Factory.CreateClient().WithBearer(session.AccessToken);
        var whoAmI = (await authenticated.GetFromJsonAsync<WhoAmI>("/test/whoami", JsonDefaults.Options))!;
        whoAmI.IsAuthenticated.ShouldBeTrue();
        whoAmI.UserId.ShouldBe(session.User.Id);
        whoAmI.TenantId.ShouldBe(session.Tenant.Id);
        whoAmI.Channel.ShouldBe("App");
    }

    [Fact]
    public async Task O_corpo_da_resposta_nao_traz_o_refresh_token_nem_o_hash()
    {
        var response = await PostAsync(new { name = "Maria", email = NewEmail(), password = AccountHelpers.DefaultPassword });

        var body = await response.Content.ReadAsStringAsync();
        var cookieValue = response.Headers.GetValues("Set-Cookie").Single().Split(';')[0]["finmy_rt=".Length..];
        body.ShouldNotContain(cookieValue);
        body.ShouldNotContain("hash", Case.Insensitive);
        body.ShouldNotContain("password", Case.Insensitive);
    }

    [Fact]
    public async Task Nome_com_espacos_nas_pontas_e_guardado_sem_eles_e_o_Espaco_usa_o_primeiro_nome()
    {
        var response = await PostAsync(new { name = "  Ana   Lima  ", email = NewEmail(), password = AccountHelpers.DefaultPassword });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var session = (await response.Content.ReadFromJsonAsync<SessionResponse>(JsonDefaults.Options))!;
        session.User.Name.ShouldBe("Ana   Lima");
        session.Tenant.Name.ShouldBe("Finanças de Ana");
    }

    [Fact]
    public async Task Nome_de_60_caracteres_sem_espaco_cria_Espaco_com_nome_truncado_em_60()
    {
        var response = await PostAsync(new { name = new string('a', 60), email = NewEmail(), password = AccountHelpers.DefaultPassword });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var session = (await response.Content.ReadFromJsonAsync<SessionResponse>(JsonDefaults.Options))!;
        session.Tenant.Name.Length.ShouldBe(60);
    }

    // --- Cenário 1.2: e-mail já cadastrado → 409, não cria nada ---

    [Fact]
    public async Task Cenario_1_2_email_repetido_responde_409_com_a_mensagem_e_nao_cria_nada()
    {
        var email = NewEmail();
        (await PostAsync(new { name = "Maria", email, password = AccountHelpers.DefaultPassword })).StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await PostAsync(new { name = "Outra Pessoa", email, password = "outra-senha-456" });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
        var problem = await ReadJsonAsync(response);
        problem.GetProperty("code").GetString().ShouldBe("email_already_registered");
        problem.GetProperty("detail").GetString().ShouldBe("Este e-mail já tem conta. Entrar ou recuperar senha?");
        (await CountsAsync()).ShouldBe((1, 1, 1));
    }

    // --- Cenário 1.3: senha curta → 422 explicando o requisito, não cria nada ---

    [Fact]
    public async Task Cenario_1_3_senha_com_menos_de_8_caracteres_responde_422_e_nao_cria_nada()
    {
        var response = await PostAsync(new { name = "Maria", email = NewEmail(), password = "1234567" });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await ReadJsonAsync(response);
        problem.GetProperty("code").GetString().ShouldBe("validation_failed");
        problem.GetProperty("errors").GetProperty("password")[0].GetString().ShouldBe("A senha precisa ter pelo menos 8 caracteres.");
        (await CountsAsync()).ShouldBe((0, 0, 0));
    }

    [Fact]
    public async Task Senha_de_exatamente_8_e_de_128_caracteres_e_aceita_e_de_129_nao()
    {
        (await PostAsync(new { name = "Maria", email = NewEmail(), password = new string('x', 8) })).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostAsync(new { name = "Maria", email = NewEmail(), password = new string('x', 128) })).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PostAsync(new { name = "Maria", email = NewEmail(), password = new string('x', 129) })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Theory]
    [InlineData("", "maria@exemplo.com", "senha-valida-1", "name")]
    [InlineData("   ", "maria@exemplo.com", "senha-valida-1", "name")]
    [InlineData("nome-com-61-caracteres", "maria@exemplo.com", "senha-valida-1", "name")]
    [InlineData("Maria", "", "senha-valida-1", "email")]
    [InlineData("Maria", "sem-arroba", "senha-valida-1", "email")]
    [InlineData("Maria", "dois@@exemplo.com", "senha-valida-1", "email")]
    [InlineData("Maria", "maria@semponto", "senha-valida-1", "email")]
    [InlineData("Maria", "ma ria@exemplo.com", "senha-valida-1", "email")]
    [InlineData("Maria", "email-com-255-caracteres", "senha-valida-1", "email")]
    [InlineData("Maria", "maria@exemplo.com", "", "password")]
    public async Task Entrada_invalida_responde_422_com_o_campo_e_nao_cria_nada(string name, string email, string password, string campo)
    {
        if (name == "nome-com-61-caracteres")
        {
            name = new string('n', 61);
        }

        if (email == "email-com-255-caracteres")
        {
            email = new string('e', 255 - "@exemplo.com".Length) + "@exemplo.com";
        }

        var response = await PostAsync(new { name, email, password });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await ReadJsonAsync(response);
        problem.GetProperty("code").GetString().ShouldBe("validation_failed");
        problem.GetProperty("errors").TryGetProperty(campo, out var mensagens).ShouldBeTrue();
        mensagens[0].GetString().ShouldNotBeNullOrWhiteSpace();
        (await CountsAsync()).ShouldBe((0, 0, 0));
    }

    [Fact]
    public async Task Campos_ausentes_no_corpo_respondem_422_e_nao_500()
    {
        var response = await PostAsync(new { });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var errors = (await ReadJsonAsync(response)).GetProperty("errors");
        errors.TryGetProperty("name", out _).ShouldBeTrue();
        errors.TryGetProperty("email", out _).ShouldBeTrue();
        errors.TryGetProperty("password", out _).ShouldBeTrue();
    }

    // --- Edge Cases de e-mail: maiúsculas/espaços e duas criações simultâneas ---

    [Theory]
    [InlineData("Maria@Exemplo.com", "maria@exemplo.com")]
    [InlineData("  maria@exemplo.com  ", "maria@exemplo.com")]
    [InlineData("  MARIA@EXEMPLO.COM ", "Maria@Exemplo.com")]
    public async Task Email_com_outras_maiusculas_ou_espacos_nas_pontas_e_o_mesmo_e_mail(string primeiro, string segundo)
    {
        (await PostAsync(new { name = "Maria", email = primeiro, password = AccountHelpers.DefaultPassword })).StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await PostAsync(new { name = "Maria", email = segundo, password = AccountHelpers.DefaultPassword });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadJsonAsync(response)).GetProperty("code").GetString().ShouldBe("email_already_registered");
        (await CountsAsync()).ShouldBe((1, 1, 1));
    }

    [Fact]
    public async Task Email_e_guardado_como_digitado_sem_espacos_e_normalizado_em_minusculas()
    {
        var response = await PostAsync(new { name = "Maria", email = "  Maria.Souza@Exemplo.COM  ", password = AccountHelpers.DefaultPassword });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var session = (await response.Content.ReadFromJsonAsync<SessionResponse>(JsonDefaults.Options))!;
        session.User.Email.ShouldBe("Maria.Souza@Exemplo.COM");

        await using var scope = Factory.Services.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<FinMyDbContext>().Set<User>().SingleAsync();
        user.Email.ShouldBe("Maria.Souza@Exemplo.COM");
        user.EmailNormalized.ShouldBe("maria.souza@exemplo.com");
    }

    [Fact]
    public async Task Duas_criacoes_simultaneas_com_o_mesmo_email_criam_so_uma_conta()
    {
        var email = NewEmail();

        var respostas = await Task.WhenAll(Enumerable.Range(0, 4).Select(i =>
            PostAsync(new { name = $"Maria {i}", email = i % 2 == 0 ? email : email.ToUpperInvariant(), password = AccountHelpers.DefaultPassword })));

        respostas.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        respostas.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(3);
        foreach (var perdedora in respostas.Where(r => r.StatusCode == HttpStatusCode.Conflict))
        {
            (await ReadJsonAsync(perdedora)).GetProperty("code").GetString().ShouldBe("email_already_registered");
        }

        (await CountsAsync()).ShouldBe((1, 1, 1));
    }

    [Fact]
    public async Task Contas_com_emails_diferentes_ganham_Espacos_diferentes()
    {
        var a = await Client.SignUpAsync(name: "Léo Alves");
        var b = await Client.SignUpAsync(name: "Lia Alves");

        a.Session.Tenant.Id.ShouldNotBe(b.Session.Tenant.Id);
        a.Session.Tenant.Name.ShouldBe("Finanças de Léo");
        b.Session.Tenant.Name.ShouldBe("Finanças de Lia");
        (await CountsAsync()).ShouldBe((2, 2, 2));
    }
}
