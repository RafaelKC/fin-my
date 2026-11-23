using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Api.Tests.Support;
using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;

namespace FinMy.Api.Tests.Entries;

/// <summary><c>GET /entries/suggestions</c> (FR-011, research R8). "Hoje" é 10/03/2026.</summary>
public class EntrySuggestionsTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private static readonly DateOnly Today = new(2026, 3, 10);

    protected override IClock? Clock => new FixedTodayClock(Today);

    private sealed record Suggestion(string Description, int Uses, Guid? WalletId);

    private async Task<(HttpClient Http, TestAccount Account)> NewUserAsync(string name = "Lia")
    {
        using var anonymous = Factory.CreateClient();
        var account = await anonymous.SignUpAsync(name: name);
        return (Factory.CreateClient().WithBearer(account.AccessToken), account);
    }

    private static async Task AddAsync(HttpClient http, Guid wallet, string description, string date = "2026-03-01", string kind = "expense", int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            var response = await http.PostAsJsonAsync("/api/v1/entries", new { kind, description, amount = "10", date, walletId = wallet });
            response.EnsureSuccessStatusCode();
        }
    }

    private static async Task<List<Suggestion>> SuggestAsync(HttpClient http, string q, string kind = "expense")
    {
        var response = await http.GetAsync($"/api/v1/entries/suggestions?q={Uri.EscapeDataString(q)}&kind={kind}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<Suggestion>>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
    }

    [Fact]
    public async Task Ordena_por_usos_e_traz_a_carteira_do_lancamento_mais_recente_US2_4()
    {
        var (lia, account) = await NewUserAsync();
        var azul = await Factory.SeedWalletAsync(account, "Azul");
        var verde = await Factory.SeedWalletAsync(account, "Verde");
        await AddAsync(lia, azul, "Tim", "2026-01-10");
        await AddAsync(lia, verde, "Tim", "2026-02-10");
        await AddAsync(lia, azul, "Tigela", "2026-03-01");
        await AddAsync(lia, azul, "Tinta", "2026-03-02", times: 3);

        var result = await SuggestAsync(lia, "Ti");

        result.Select(s => (s.Description, s.Uses)).ShouldBe([("Tinta", 3), ("Tim", 2), ("Tigela", 1)]);
        result.Single(s => s.Description == "Tim").WalletId.ShouldBe(verde);
        result.Single(s => s.Description == "Tinta").WalletId.ShouldBe(azul);
    }

    [Fact]
    public async Task Empate_em_usos_fica_com_o_uso_mais_recente_primeiro()
    {
        var (lia, account) = await NewUserAsync();
        var azul = await Factory.SeedWalletAsync(account, "Azul");
        await AddAsync(lia, azul, "Padaria Antiga", "2026-01-05");
        await AddAsync(lia, azul, "Padaria Nova", "2026-03-05");

        var result = await SuggestAsync(lia, "padaria");

        result.Select(s => s.Description).ShouldBe(["Padaria Nova", "Padaria Antiga"]);
    }

    [Fact]
    public async Task A_carteira_vem_so_do_mesmo_tipo()
    {
        var (lia, account) = await NewUserAsync();
        var azul = await Factory.SeedWalletAsync(account, "Azul");
        var verde = await Factory.SeedWalletAsync(account, "Verde");
        await AddAsync(lia, azul, "Duda", "2026-02-01", kind: "expense");
        await AddAsync(lia, verde, "Duda", "2026-03-01", kind: "income");

        var asExpense = await SuggestAsync(lia, "duda", "expense");
        var asIncome = await SuggestAsync(lia, "duda", "income");

        asExpense.Single().WalletId.ShouldBe(azul);
        asExpense.Single().Uses.ShouldBe(2);
        asIncome.Single().WalletId.ShouldBe(verde);
    }

    [Fact]
    public async Task Descricao_que_so_existe_no_outro_tipo_vem_sem_carteira()
    {
        var (lia, account) = await NewUserAsync();
        var azul = await Factory.SeedWalletAsync(account, "Azul");
        await AddAsync(lia, azul, "Salário", kind: "income");

        var result = await SuggestAsync(lia, "salario", "expense");

        result.Single().Description.ShouldBe("Salário");
        result.Single().WalletId.ShouldBeNull();
    }

    [Fact]
    public async Task Carteira_arquivada_nao_e_sugerida()
    {
        var (lia, account) = await NewUserAsync();
        var azul = await Factory.SeedWalletAsync(account, "Azul");
        await AddAsync(lia, azul, "Tim");
        await Factory.ArchiveWalletAsync(account, azul);

        var result = await SuggestAsync(lia, "tim");

        result.Single().WalletId.ShouldBeNull();
    }

    [Fact]
    public async Task Considera_so_os_ultimos_12_meses_e_inclui_datas_futuras()
    {
        var (lia, account) = await NewUserAsync();
        var azul = await Factory.SeedWalletAsync(account, "Azul");
        await AddAsync(lia, azul, "Velha", "2025-03-09"); // um dia antes do limite (10/03/2025)
        await AddAsync(lia, azul, "Limite", "2025-03-10");
        await AddAsync(lia, azul, "Futura", "2026-09-01");

        var result = await SuggestAsync(lia, "");
        var velha = await SuggestAsync(lia, "velha");
        var limite = await SuggestAsync(lia, "limite");
        var futura = await SuggestAsync(lia, "futura");

        result.ShouldBeEmpty();
        velha.ShouldBeEmpty();
        limite.Single().Description.ShouldBe("Limite");
        futura.Single().Description.ShouldBe("Futura");
    }

    [Fact]
    public async Task Devolve_no_maximo_6()
    {
        var (lia, account) = await NewUserAsync();
        var azul = await Factory.SeedWalletAsync(account, "Azul");
        foreach (var name in new[] { "Mercado A", "Mercado B", "Mercado C", "Mercado D", "Mercado E", "Mercado F", "Mercado G", "Mercado H" })
        {
            await AddAsync(lia, azul, name);
        }

        var result = await SuggestAsync(lia, "mercado");

        result.Count.ShouldBe(6);
    }

    [Fact]
    public async Task Ignora_maiusculas_e_acentos_e_mostra_a_grafia_do_uso_mais_recente()
    {
        var (lia, account) = await NewUserAsync();
        var azul = await Factory.SeedWalletAsync(account, "Azul");
        await AddAsync(lia, azul, "CAFÉ da Bruna", "2026-02-01");
        await AddAsync(lia, azul, "Cafe da Bruna", "2026-03-01");

        var comAcento = await SuggestAsync(lia, "café");
        var semAcento = await SuggestAsync(lia, "CAFE");
        var meio = await SuggestAsync(lia, "da bru");

        comAcento.Single().Uses.ShouldBe(2);
        comAcento.Single().Description.ShouldBe("Cafe da Bruna");
        semAcento.Single().Uses.ShouldBe(2);
        meio.Single().Uses.ShouldBe(2);
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("\\")]
    public async Task Curingas_do_LIKE_sao_literais(string q)
    {
        var (lia, account) = await NewUserAsync();
        var azul = await Factory.SeedWalletAsync(account, "Azul");
        await AddAsync(lia, azul, "Tim");
        await AddAsync(lia, azul, "Promoção 50% off");
        await AddAsync(lia, azul, "Conta_luz");
        await AddAsync(lia, azul, "Pasta C:\\docs");

        var result = await SuggestAsync(lia, q);

        result.Count.ShouldBe(1);
        result.Single().Description.ShouldContain(q);
    }

    [Fact]
    public async Task So_enxerga_os_lancamentos_do_proprio_Espaco()
    {
        var (lia, liaAccount) = await NewUserAsync("Lia");
        var (leo, _) = await NewUserAsync("Léo");
        await AddAsync(lia, await Factory.SeedWalletAsync(liaAccount, "Azul"), "Tim");

        var result = await SuggestAsync(leo, "tim");

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task Parametros_invalidos_respondem_422()
    {
        var (lia, _) = await NewUserAsync();

        var kind = await lia.GetAsync("/api/v1/entries/suggestions?q=ti&kind=transfer");
        var semKind = await lia.GetAsync("/api/v1/entries/suggestions?q=ti");
        var longo = await lia.GetAsync($"/api/v1/entries/suggestions?q={new string('a', 81)}&kind=expense");
        var semQ = await lia.GetAsync("/api/v1/entries/suggestions?kind=expense");

        kind.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        semKind.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        longo.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        semQ.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await semQ.Content.ReadFromJsonAsync<JsonElement>()).GetArrayLength().ShouldBe(0);
    }
}
