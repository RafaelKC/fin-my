using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Api.Tests.Support;
using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;
using static FinMy.Api.Tests.Cards.CardTestSupport;

namespace FinMy.Api.Tests.Onboarding;

/// <summary>
/// <c>/api/v1/onboarding</c> (006 T003, research R3): estado por etapa, <c>autoStart</c> só logo após criar a conta,
/// dispensa do guia, etapa/estado inválidos em 422 e isolamento entre Espaços.
/// </summary>
public class OnboardingTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 3, 20, 15, 0, 0, TimeSpan.Zero);

    private readonly FakeClock _clock = new(DateTimeOffset.UtcNow);

    protected override IClock? Clock => _clock;

    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?> { ["RateLimiting:Auth:PermitLimit"] = "100000" };

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient http, string step, object? state) =>
        http.PutAsJsonAsync($"/api/v1/onboarding/steps/{step}", new { state });

    [Fact]
    public async Task Espaco_novo_sem_carteiras_inicia_o_guia_sozinho()
    {
        var (http, _) = await NewSpaceAsync(Factory, "Lia");

        var guide = await ReadAsync(await http.GetAsync("/api/v1/onboarding"));

        guide.GetProperty("steps").EnumerateObject().ShouldBeEmpty();
        guide.GetProperty("dismissed").GetBoolean().ShouldBeFalse();
        guide.GetProperty("autoStart").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Marcar_etapas_como_concluida_ou_pulada_grava_o_estado_e_pode_ser_trocado()
    {
        var (http, _) = await NewSpaceAsync(Factory, "Lia");

        await ReadAsync(await PutAsync(http, "wallets", "done"));
        var guide = await ReadAsync(await PutAsync(http, "cards", "skipped"));
        guide.GetProperty("steps").GetProperty("wallets").GetString().ShouldBe("done");
        guide.GetProperty("steps").GetProperty("cards").GetString().ShouldBe("skipped");
        guide.GetProperty("steps").EnumerateObject().Count().ShouldBe(2);

        var trocada = await ReadAsync(await PutAsync(http, "cards", "done"));
        trocada.GetProperty("steps").GetProperty("cards").GetString().ShouldBe("done");

        var lida = await ReadAsync(await http.GetAsync("/api/v1/onboarding"));
        lida.GetProperty("steps").GetProperty("wallets").GetString().ShouldBe("done");
        lida.GetProperty("steps").GetProperty("cards").GetString().ShouldBe("done");
    }

    [Fact]
    public async Task AutoStart_some_com_todas_as_etapas_resolvidas()
    {
        var (http, _) = await NewSpaceAsync(Factory, "Lia");

        JsonElement guide = default;
        foreach (var step in new[] { "wallets", "cards", "settlement_tags", "installments", "recurrences" })
        {
            guide = await ReadAsync(await PutAsync(http, step, "skipped"));
        }

        guide.GetProperty("autoStart").GetBoolean().ShouldBeFalse();
        guide.GetProperty("dismissed").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task AutoStart_some_depois_de_criar_a_primeira_carteira()
    {
        var (http, _) = await NewSpaceAsync(Factory, "Lia");

        var criada = await http.PostAsJsonAsync("/api/v1/wallets", new { name = "Azul", type = "checking", color = "#1E88E5", initialBalance = "0", initialBalanceDate = "2026-01-01" });
        criada.StatusCode.ShouldBe(HttpStatusCode.Created, await criada.Content.ReadAsStringAsync());

        var guide = await ReadAsync(await http.GetAsync("/api/v1/onboarding"));
        guide.GetProperty("autoStart").GetBoolean().ShouldBeFalse();
        guide.GetProperty("dismissed").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Dispensar_o_guia_desliga_autoStart_e_e_idempotente()
    {
        var (http, _) = await NewSpaceAsync(Factory, "Lia");

        var primeira = await ReadAsync(await http.PostAsync("/api/v1/onboarding/dismiss", null));
        primeira.GetProperty("dismissed").GetBoolean().ShouldBeTrue();
        primeira.GetProperty("autoStart").GetBoolean().ShouldBeFalse();

        var segunda = await ReadAsync(await http.PostAsync("/api/v1/onboarding/dismiss", null));
        segunda.GetProperty("dismissed").GetBoolean().ShouldBeTrue();

        var lida = await ReadAsync(await http.GetAsync("/api/v1/onboarding"));
        lida.GetProperty("dismissed").GetBoolean().ShouldBeTrue();
        lida.GetProperty("autoStart").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Dispensar_guarda_a_data_da_primeira_vez()
    {
        _clock.UtcNow = DateTimeOffset.UtcNow;
        var (http, _) = await NewSpaceAsync(Factory, "Lia");

        _clock.UtcNow = Now;
        await ReadAsync(await http.PostAsync("/api/v1/onboarding/dismiss", null));
        _clock.UtcNow = Now.AddDays(3);
        await ReadAsync(await http.PostAsync("/api/v1/onboarding/dismiss", null));

        await using var conn = new Npgsql.NpgsqlConnection(DatabaseConnection);
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand("select onboarding_dismissed_at from tenants", conn);
        new DateTimeOffset((DateTime)(await cmd.ExecuteScalarAsync())!, TimeSpan.Zero).ShouldBe(Now);
    }

    [Theory]
    [InlineData("desconhecida", "done")]
    [InlineData("wallets", "pending")]
    [InlineData("wallets", null)]
    [InlineData("wallets", "")]
    public async Task Etapa_ou_estado_invalido_e_422_e_nao_grava(string step, string? state)
    {
        var (http, _) = await NewSpaceAsync(Factory, "Lia");

        var response = await PutAsync(http, step, state);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, await response.Content.ReadAsStringAsync());
        var guide = await ReadAsync(await http.GetAsync("/api/v1/onboarding"));
        guide.GetProperty("steps").EnumerateObject().ShouldBeEmpty();
    }

    [Fact]
    public async Task Corpo_ausente_e_422()
    {
        var (http, _) = await NewSpaceAsync(Factory, "Lia");

        var response = await http.PutAsync("/api/v1/onboarding/steps/wallets", null);

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.UnprocessableEntity, HttpStatusCode.BadRequest, HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task Cada_Espaco_tem_o_proprio_estado()
    {
        var (lia, _) = await NewSpaceAsync(Factory, "Lia");
        var (leo, _) = await NewSpaceAsync(Factory, "Léo");

        await ReadAsync(await PutAsync(lia, "wallets", "done"));
        await ReadAsync(await lia.PostAsync("/api/v1/onboarding/dismiss", null));

        var doLeo = await ReadAsync(await leo.GetAsync("/api/v1/onboarding"));
        doLeo.GetProperty("steps").EnumerateObject().ShouldBeEmpty();
        doLeo.GetProperty("dismissed").GetBoolean().ShouldBeFalse();
        doLeo.GetProperty("autoStart").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Sem_login_e_401()
    {
        (await Client.GetAsync("/api/v1/onboarding")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Client.PostAsync("/api/v1/onboarding/dismiss", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await PutAsync(Client, "wallets", "done")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
