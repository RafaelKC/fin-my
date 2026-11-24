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

namespace FinMy.Api.Tests.OAuth;

/// <summary>008 T002 / US1.2 e FR-003: "Aplicativos conectados" (<c>GET/DELETE /api/v1/me/connected-apps</c>) e o último uso.</summary>
public class ConnectedAppsTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private const string Path = "/api/v1/me/connected-apps";
    private const string AppUrl = "http://finmy.exemplo.com";

    // Sem frações abaixo de 1 µs, a precisão do PostgreSQL.
    private static readonly DateTimeOffset Start = new(DateTime.UtcNow.Ticks / 10 * 10, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly FakeClock _clock = new(Start);

    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?> { ["APP_URL"] = AppUrl };

    protected override IEndpointModule[] ExtraEndpoints => [new OAuthProbeEndpoints()];

    protected override IClock? Clock => _clock;

    private OAuthFlow Flow => field ??= new OAuthFlow(Factory);

    private async Task<TestAccount> SignUpAsync(string name)
    {
        using var anonymous = Factory.CreateClient();
        return await anonymous.SignUpAsync(name);
    }

    private HttpClient AppOf(TestAccount account) => Factory.CreateClient().WithBearer(account.AccessToken);

    private static async Task<List<ConnectedAppDto>> ListAsync(HttpClient app)
    {
        var response = await app.GetAsync(Path);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<ConnectedAppDto>>(Json))!;
    }

    [Fact]
    public async Task Sem_conexoes_a_lista_vem_vazia()
    {
        using var app = AppOf(await SignUpAsync("Lia"));

        (await ListAsync(app)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Lista_o_aplicativo_com_nome_e_desde_quando_esta_conectado_e_sem_ultimo_uso()
    {
        var lia = await SignUpAsync("Lia");
        await Flow.ConnectAsync(lia.AccessToken, "Claude");
        using var app = AppOf(lia);

        var apps = await ListAsync(app);

        var claude = apps.ShouldHaveSingleItem();
        claude.Name.ShouldBe("Claude");
        claude.Id.ShouldNotBeNullOrWhiteSpace();
        claude.ConnectedAt.ShouldBeInRange(Start.AddMinutes(-1), Start.AddMinutes(1));
        claude.LastUsedAt.ShouldBeNull();
    }

    [Fact]
    public async Task O_uso_do_token_anota_o_ultimo_uso_no_maximo_uma_vez_a_cada_5_minutos()
    {
        var lia = await SignUpAsync("Lia");
        var (_, tokens) = await Flow.ConnectAsync(lia.AccessToken);
        using var app = AppOf(lia);

        (await Flow.WhoAmIAsync(tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var first = (await ListAsync(app)).Single().LastUsedAt;
        first.ShouldBe(Start);

        _clock.UtcNow = Start.AddMinutes(4);
        (await Flow.WhoAmIAsync(tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ListAsync(app)).Single().LastUsedAt.ShouldBe(first);

        _clock.UtcNow = Start.AddMinutes(5);
        (await Flow.WhoAmIAsync(tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ListAsync(app)).Single().LastUsedAt.ShouldBe(Start.AddMinutes(5));
    }

    [Fact]
    public async Task Uso_com_token_do_app_nao_anota_ultimo_uso()
    {
        var lia = await SignUpAsync("Lia");
        await Flow.ConnectAsync(lia.AccessToken);
        using var app = AppOf(lia);

        (await app.GetAsync("/api/v1/me")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await ListAsync(app)).Single().LastUsedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Desconectar_devolve_204_remove_da_lista_e_encerra_o_acesso_e_o_refresh_na_hora()
    {
        var lia = await SignUpAsync("Lia");
        var (clientId, tokens) = await Flow.ConnectAsync(lia.AccessToken);
        using var app = AppOf(lia);
        (await Flow.WhoAmIAsync(tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var id = (await ListAsync(app)).Single().Id;

        var response = await app.DeleteAsync($"{Path}/{id}");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ListAsync(app)).ShouldBeEmpty();
        (await Flow.WhoAmIAsync(tokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Flow.RefreshAsync(clientId, tokens.RefreshToken)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMyDbContext>();
        (await db.Set<OAuthAuthorizationUsage>().AnyAsync(u => u.AuthorizationId == id)).ShouldBeFalse();
    }

    [Fact]
    public async Task Desconectar_de_novo_ou_um_id_inexistente_e_404()
    {
        var lia = await SignUpAsync("Lia");
        await Flow.ConnectAsync(lia.AccessToken);
        using var app = AppOf(lia);
        var id = (await ListAsync(app)).Single().Id;
        (await app.DeleteAsync($"{Path}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var again = await app.DeleteAsync($"{Path}/{id}");
        var unknown = await app.DeleteAsync($"{Path}/nao-existe");

        again.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await again.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().ShouldBe("connected_app_not_found");
    }

    [Fact]
    public async Task Cada_pessoa_so_ve_e_desconecta_as_proprias_conexoes()
    {
        var lia = await SignUpAsync("Lia");
        var bruna = await SignUpAsync("Bruna");
        var (_, liaTokens) = await Flow.ConnectAsync(lia.AccessToken);
        var (_, brunaTokens) = await Flow.ConnectAsync(bruna.AccessToken);
        using var liaApp = AppOf(lia);
        using var brunaApp = AppOf(bruna);
        var liaConnection = (await ListAsync(liaApp)).Single().Id;
        (await ListAsync(brunaApp)).Single().Id.ShouldNotBe(liaConnection);

        // Bruna tenta desconectar a conexão da Lia: 404, e a conexão segue valendo.
        (await brunaApp.DeleteAsync($"{Path}/{liaConnection}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await Flow.WhoAmIAsync(liaTokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ListAsync(liaApp)).Single().Id.ShouldBe(liaConnection);

        // Desconectar a da Lia não derruba a da Bruna.
        (await liaApp.DeleteAsync($"{Path}/{liaConnection}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Flow.WhoAmIAsync(brunaTokens.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Lista_varias_conexoes_da_mais_recente_para_a_mais_antiga()
    {
        var lia = await SignUpAsync("Lia");
        await Flow.ConnectAsync(lia.AccessToken, "Claude Desktop");
        _clock.UtcNow = Start.AddMinutes(10);
        await Flow.ConnectAsync(lia.AccessToken, "Claude Celular");
        using var app = AppOf(lia);

        (await ListAsync(app)).Select(a => a.Name).ShouldBe(["Claude Celular", "Claude Desktop"]);
    }

    [Fact]
    public async Task Exige_login_no_app()
    {
        using var anonymous = Factory.CreateClient();

        (await anonymous.GetAsync(Path)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.DeleteAsync($"{Path}/qualquer")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private sealed record ConnectedAppDto(string Id, string Name, DateTimeOffset ConnectedAt, DateTimeOffset? LastUsedAt);
}
