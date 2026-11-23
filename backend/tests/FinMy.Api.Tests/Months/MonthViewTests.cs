using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Api.Tests.Support;
using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;
using FinMy.Domain.Wallets;
using static FinMy.Api.Tests.Months.MonthSeeding;

namespace FinMy.Api.Tests.Months;

/// <summary><c>GET /api/v1/months/{month}</c> (US3, US5.1, US5.2; research R4): exemplo numérico, filtros, ordem e erros.</summary>
public class MonthViewTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    // O relógio parte do instante real (o token é emitido e validado nele); "hoje" vem dele.
    private readonly FakeClock _clock = new(DateTimeOffset.UtcNow);

    protected override IClock? Clock => _clock;

    private static readonly DateOnly March1 = new(2026, 3, 1);

    private async Task<(HttpClient Http, TestAccount Account)> NewUserAsync(string name)
    {
        using var anonymous = Factory.CreateClient();
        var account = await anonymous.SignUpAsync(name: name);
        return (Factory.CreateClient().WithBearer(account.AccessToken), account);
    }

    private static async Task<JsonElement> GetOkAsync(HttpClient http, string url)
    {
        var response = await http.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static long Cents(JsonElement column, string name) => column.GetProperty(name).GetInt64();

    /// <summary>Carteira Azul (R$ 500,00 em 01/03/2026) e os quatro lançamentos do exemplo da US3.</summary>
    private async Task<(Wallet Azul, Entry Salario, Entry Duda, Entry Tim, Entry LeoCasa)> SeedExemploUs3Async(TestAccount account)
    {
        var azul = NewWallet("Azul", 50000, March1, primary: true);
        var salario = NewEntry(EntryKind.Income, "Salário", 150000, new DateOnly(2026, 3, 5), azul.Id, EntryStatus.Settled);
        var duda = NewEntry(EntryKind.Income, "Duda", 1827, new DateOnly(2026, 3, 20), azul.Id, EntryStatus.Planned);
        var tim = NewEntry(EntryKind.Expense, "Tim", 4499, new DateOnly(2026, 3, 10), azul.Id, EntryStatus.Settled);
        var leoCasa = NewEntry(EntryKind.Expense, "Léo Casa", 77003, new DateOnly(2026, 3, 25), azul.Id, EntryStatus.Planned);
        await Factory.SeedAsync(account, azul, salario, duda, tim, leoCasa);
        return (azul, salario, duda, tim, leoCasa);
    }

    [Fact]
    public async Task Exemplo_da_US3_devolve_todos_os_numeros_de_marco_e_o_saldo_inicial_de_abril()
    {
        var (http, lia) = await NewUserAsync("Lia");
        var (azul, _, _, _, _) = await SeedExemploUs3Async(lia);

        var march = await GetOkAsync(http, "/api/v1/months/2026-03");
        var planned = march.GetProperty("planned");
        var actual = march.GetProperty("actual");

        march.GetProperty("month").GetString().ShouldBe("2026-03");
        march.GetProperty("walletIds").ValueKind.ShouldBe(JsonValueKind.Null);

        Cents(planned, "openingBalanceCents").ShouldBe(50000);
        Cents(planned, "walletsInitialBalanceCents").ShouldBe(50000);
        Cents(planned, "incomeCents").ShouldBe(151827);
        Cents(planned, "expenseCents").ShouldBe(81502);
        Cents(planned, "resultCents").ShouldBe(70325);
        Cents(planned, "finalBalanceCents").ShouldBe(120325);

        Cents(actual, "openingBalanceCents").ShouldBe(50000);
        Cents(actual, "incomeCents").ShouldBe(150000);
        Cents(actual, "expenseCents").ShouldBe(4499);
        Cents(actual, "resultCents").ShouldBe(145501);
        Cents(actual, "finalBalanceCents").ShouldBe(195501);

        var april = await GetOkAsync(http, "/api/v1/months/2026-04");
        Cents(april.GetProperty("planned"), "openingBalanceCents").ShouldBe(120325);
        Cents(april.GetProperty("planned"), "walletsInitialBalanceCents").ShouldBe(0);
        Cents(april.GetProperty("actual"), "openingBalanceCents").ShouldBe(195501);
        april.GetProperty("entries").GetArrayLength().ShouldBe(0);
        azul.Id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Lista_de_marco_vem_na_ordem_da_RT_12_com_o_contrato_do_lancamento()
    {
        var (http, lia) = await NewUserAsync("Lia");
        var (azul, salario, _, _, _) = await SeedExemploUs3Async(lia);

        var march = await GetOkAsync(http, "/api/v1/months/2026-03");
        var entries = march.GetProperty("entries").EnumerateArray().ToList();

        entries.Select(e => e.GetProperty("description").GetString())
            .ShouldBe(["Salário", "Tim", "Duda", "Léo Casa"]);
        var first = entries[0];
        first.GetProperty("id").GetGuid().ShouldBe(salario.Id);
        first.GetProperty("kind").GetString().ShouldBe("income");
        first.GetProperty("status").GetString().ShouldBe("settled");
        first.GetProperty("amountCents").GetInt64().ShouldBe(150000);
        first.GetProperty("date").GetString().ShouldBe("2026-03-05");
        first.GetProperty("referenceMonth").GetString().ShouldBe("2026-03");
        first.GetProperty("walletId").GetGuid().ShouldBe(azul.Id);
        first.TryGetProperty("version", out _).ShouldBeTrue();
        first.TryGetProperty("createdAt", out _).ShouldBeTrue();
        entries[1].GetProperty("kind").GetString().ShouldBe("expense");
        entries[2].GetProperty("status").GetString().ShouldBe("planned");
    }

    [Fact]
    public async Task Mesma_data_desempata_pela_ordem_de_criacao()
    {
        var (http, lia) = await NewUserAsync("Lia");
        var azul = NewWallet("Azul", 0, March1, primary: true);
        var day = new DateOnly(2026, 3, 12);
        await Factory.SeedAsync(lia, azul);

        // O relógio avança entre as gravações: o desempate é pelo created_at (o id UUIDv7 só desempata o que sobrar).
        foreach (var entry in new[]
        {
            NewEntry(EntryKind.Expense, "Primeiro", 100, day, azul.Id, EntryStatus.Settled),
            NewEntry(EntryKind.Income, "Segundo", 200, day, azul.Id, EntryStatus.Settled),
            NewEntry(EntryKind.Expense, "Terceiro", 300, day, azul.Id, EntryStatus.Settled),
            NewEntry(EntryKind.Expense, "Antes", 400, new DateOnly(2026, 3, 2), azul.Id, EntryStatus.Settled),
        })
        {
            _clock.UtcNow = _clock.UtcNow.AddSeconds(1);
            await Factory.SeedAsync(lia, entry);
        }

        var march = await GetOkAsync(http, "/api/v1/months/2026-03");

        march.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("description").GetString())
            .ShouldBe(["Antes", "Primeiro", "Segundo", "Terceiro"]);
    }

    [Fact]
    public async Task Lista_do_mes_usa_o_mes_de_referencia_e_nao_a_data()
    {
        var (http, lia) = await NewUserAsync("Lia");
        var azul = NewWallet("Azul", 0, March1, primary: true);
        await Factory.SeedAsync(
            lia,
            azul,
            NewEntry(EntryKind.Expense, "Cartão Verde", 9000, new DateOnly(2026, 3, 28), azul.Id, EntryStatus.Settled, YearMonth.Of(2026, 4)));

        var march = await GetOkAsync(http, "/api/v1/months/2026-03");
        var april = await GetOkAsync(http, "/api/v1/months/2026-04");

        march.GetProperty("entries").GetArrayLength().ShouldBe(0);
        Cents(march.GetProperty("actual"), "expenseCents").ShouldBe(0);
        april.GetProperty("entries").GetArrayLength().ShouldBe(1);
        Cents(april.GetProperty("actual"), "expenseCents").ShouldBe(9000);
    }

    [Fact]
    public async Task US5_1_e_US5_2_transferencia_nao_muda_totais_gerais_e_aparece_no_filtro_por_carteira()
    {
        var (http, lia) = await NewUserAsync("Lia");
        var june1 = new DateOnly(2026, 6, 1);
        var azul = NewWallet("Azul", 100000, june1, primary: true);
        var poupanca = NewWallet("Verde Poupança", 0, june1);
        var transfer = NewTransfer(azul.Id, poupanca.Id, 2000, new DateOnly(2026, 6, 29));
        await Factory.SeedAsync(lia, azul, poupanca, transfer);

        var all = await GetOkAsync(http, "/api/v1/months/2026-06");
        var onlyAzul = await GetOkAsync(http, $"/api/v1/months/2026-06?walletIds={azul.Id}");
        var onlyPoupanca = await GetOkAsync(http, $"/api/v1/months/2026-06?walletIds={poupanca.Id}");
        var both = await GetOkAsync(http, $"/api/v1/months/2026-06?walletIds={azul.Id},{poupanca.Id}");

        Cents(all.GetProperty("actual"), "finalBalanceCents").ShouldBe(100000);
        Cents(all.GetProperty("actual"), "transfersOutCents").ShouldBe(0);
        Cents(all.GetProperty("actual"), "resultCents").ShouldBe(0);
        all.GetProperty("transfers").GetArrayLength().ShouldBe(1);
        all.GetProperty("transfers")[0].GetProperty("id").GetGuid().ShouldBe(transfer.Id);
        all.GetProperty("transfers")[0].GetProperty("description").GetString().ShouldBe("Transferência");

        Cents(onlyAzul.GetProperty("actual"), "transfersOutCents").ShouldBe(2000);
        Cents(onlyAzul.GetProperty("actual"), "finalBalanceCents").ShouldBe(98000);
        onlyAzul.GetProperty("walletIds").EnumerateArray().Select(i => i.GetGuid()).ShouldBe([azul.Id]);
        onlyAzul.GetProperty("transfers").GetArrayLength().ShouldBe(1);

        Cents(onlyPoupanca.GetProperty("actual"), "transfersInCents").ShouldBe(2000);
        Cents(onlyPoupanca.GetProperty("actual"), "finalBalanceCents").ShouldBe(2000);

        Cents(both.GetProperty("actual"), "transfersInCents").ShouldBe(0);
        Cents(both.GetProperty("actual"), "finalBalanceCents").ShouldBe(100000);

        var julyAzul = await GetOkAsync(http, $"/api/v1/months/2026-07?walletIds={azul.Id}");
        Cents(julyAzul.GetProperty("actual"), "openingBalanceCents").ShouldBe(98000);
        julyAzul.GetProperty("transfers").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Filtro_por_carteira_limita_lancamentos_e_saldos_iniciais_e_ignora_ids_desconhecidos()
    {
        var (http, lia) = await NewUserAsync("Lia");
        var azul = NewWallet("Azul", 50000, March1, primary: true);
        var verde = NewWallet("Verde", 20000, new DateOnly(2026, 2, 1));
        await Factory.SeedAsync(
            lia,
            azul,
            verde,
            NewEntry(EntryKind.Expense, "Mercado da Lia", 3000, new DateOnly(2026, 3, 4), azul.Id, EntryStatus.Settled),
            NewEntry(EntryKind.Expense, "Padaria do Léo", 700, new DateOnly(2026, 3, 6), verde.Id, EntryStatus.Settled));

        var onlyVerde = await GetOkAsync(http, $"/api/v1/months/2026-03?walletIds={verde.Id},{Guid.NewGuid()}");

        onlyVerde.GetProperty("walletIds").EnumerateArray().Select(i => i.GetGuid()).ShouldBe([verde.Id]);
        onlyVerde.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("description").GetString()).ShouldBe(["Padaria do Léo"]);
        var actual = onlyVerde.GetProperty("actual");
        Cents(actual, "openingBalanceCents").ShouldBe(20000);
        Cents(actual, "walletsInitialBalanceCents").ShouldBe(0);
        Cents(actual, "finalBalanceCents").ShouldBe(19300);
    }

    [Fact]
    public async Task Pendencias_sao_as_previstas_de_meses_anteriores_ao_mes_de_hoje_e_o_today_vem_na_resposta()
    {
        var (http, lia) = await NewUserAsync("Lia");
        var today = _clock.Today;
        var thisMonth = YearMonth.From(today);
        var azul = NewWallet("Azul", 0, new DateOnly(2026, 1, 1), primary: true);
        var past = thisMonth.AddMonths(-3).FirstDay;
        await Factory.SeedAsync(
            lia,
            azul,
            NewEntry(EntryKind.Income, "Carla atrasado", 25000, past, azul.Id, EntryStatus.Planned),
            NewEntry(EntryKind.Expense, "Tim", 4499, past, azul.Id, EntryStatus.Planned),
            NewEntry(EntryKind.Expense, "Duda", 1000, past, azul.Id, EntryStatus.Planned),
            NewEntry(EntryKind.Expense, "Já paga", 9999, past, azul.Id, EntryStatus.Settled),
            NewEntry(EntryKind.Expense, "Deste mês", 5000, thisMonth.FirstDay, azul.Id, EntryStatus.Planned));

        var view = await GetOkAsync(http, $"/api/v1/months/{thisMonth}");

        view.GetProperty("today").GetString().ShouldBe(today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        var pending = view.GetProperty("pending");
        pending.GetProperty("count").GetInt32().ShouldBe(3);
        Cents(pending, "toReceiveCents").ShouldBe(25000);
        Cents(pending, "toPayCents").ShouldBe(5499);
    }

    [Fact]
    public async Task Pendencias_ignoram_o_filtro_de_carteiras()
    {
        var (http, lia) = await NewUserAsync("Lia");
        var thisMonth = YearMonth.From(_clock.Today);
        var past = thisMonth.AddMonths(-2).FirstDay;
        var azul = NewWallet("Azul", 0, new DateOnly(2026, 1, 1), primary: true);
        var verde = NewWallet("Verde", 0, new DateOnly(2026, 1, 1));
        await Factory.SeedAsync(
            lia,
            azul,
            verde,
            NewEntry(EntryKind.Income, "Carla atrasado", 25000, past, azul.Id, EntryStatus.Planned),
            NewEntry(EntryKind.Expense, "Tim", 4499, past, verde.Id, EntryStatus.Planned));

        var view = await GetOkAsync(http, $"/api/v1/months/{thisMonth}?walletIds={azul.Id}");

        var pending = view.GetProperty("pending");
        pending.GetProperty("count").GetInt32().ShouldBe(2);
        Cents(pending, "toReceiveCents").ShouldBe(25000);
        Cents(pending, "toPayCents").ShouldBe(4499);
    }

    [Fact]
    public async Task Nao_mostra_dados_de_outro_Espaco()
    {
        var (_, lia) = await NewUserAsync("Lia");
        var (httpDeLeo, _) = await NewUserAsync("Léo");
        var (azul, _, _, _, _) = await SeedExemploUs3Async(lia);

        var view = await GetOkAsync(httpDeLeo, $"/api/v1/months/2026-03?walletIds={azul.Id}");

        view.GetProperty("entries").GetArrayLength().ShouldBe(0);
        view.GetProperty("walletIds").GetArrayLength().ShouldBe(0);
        Cents(view.GetProperty("planned"), "finalBalanceCents").ShouldBe(0);
        view.GetProperty("pending").GetProperty("count").GetInt32().ShouldBe(0);
    }

    [Theory]
    [InlineData("2026-13")]
    [InlineData("2026-3")]
    [InlineData("marco")]
    [InlineData("1999-12")]
    [InlineData("2101-01")]
    public async Task Mes_invalido_ou_fora_do_intervalo_responde_422(string month)
    {
        var (http, _) = await NewUserAsync("Lia");

        var response = await http.GetAsync($"/api/v1/months/{month}");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("month", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Limites_do_intervalo_sao_aceitos()
    {
        var (http, _) = await NewUserAsync("Lia");

        (await http.GetAsync("/api/v1/months/2000-01")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await http.GetAsync("/api/v1/months/2100-12")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task WalletIds_malformado_responde_422_e_sem_token_401()
    {
        var (http, _) = await NewUserAsync("Lia");

        var invalid = await http.GetAsync("/api/v1/months/2026-03?walletIds=nao-e-uuid");
        var anonymous = await Client.GetAsync("/api/v1/months/2026-03");

        invalid.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("walletIds", out _).ShouldBeTrue();
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
