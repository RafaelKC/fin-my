using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Api.Tests.Cards;
using FinMy.Api.Tests.Support;
using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Entries;
using static FinMy.Api.Tests.Cards.CardTestSupport;
using static FinMy.Api.Tests.Months.MonthSeeding;

namespace FinMy.Api.Tests.Panel;

/// <summary>
/// <c>GET /api/v1/years/{yyyy}/panel</c> (006 T001, research R1, SC-001): cada mês do painel é igual ao <c>GET /months/{m}</c> sem filtro,
/// com lançamentos Previstos e Realizados, transferências, faturas com pagamento parcial e total, e pendências, em mais de 3 meses.
/// </summary>
public class PanelParityTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private readonly FakeClock _clock = new(DateTimeOffset.UtcNow);

    protected override IClock? Clock => _clock;

    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?> { ["RateLimiting:Auth:PermitLimit"] = "100000" };

    private static readonly string[] Columns =
    [
        "openingBalanceCents", "walletsInitialBalanceCents", "incomeCents", "expenseCents",
        "resultCents", "transfersInCents", "transfersOutCents", "finalBalanceCents",
    ];

    private async Task<HttpClient> SeedSpaceAsync()
    {
        _clock.UtcNow = DateTimeOffset.UtcNow;
        var (http, account) = await NewSpaceAsync(Factory);
        var azulResponse = await http.PostAsJsonAsync(
            "/api/v1/wallets",
            new { name = "Azul", type = "checking", color = "#1E88E5", initialBalance = "2000", initialBalanceDate = "2025-12-01" });
        azulResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var azul = (await azulResponse.Content.ReadFromJsonAsync<WalletRefDto>(JsonDefaults.Options))!;
        var verde = await CreateWalletAsync(http, "Verde");
        var card = await CreateCardAsync(http, "Azul", azul.Id);
        _clock.UtcNow = new DateTimeOffset(2026, 3, 20, 15, 0, 0, TimeSpan.Zero);

        var rows = new List<object>();
        for (var month = 1; month <= 5; month++)
        {
            var date = new DateOnly(2026, month, 5);
            rows.Add(NewEntry(EntryKind.Income, "Salário", 300000 + month * 100, date, azul.Id, EntryStatus.Settled));
            rows.Add(NewEntry(EntryKind.Expense, "Aluguel", 120000, date.AddDays(5), azul.Id, month <= 3 ? EntryStatus.Settled : EntryStatus.Planned));
            rows.Add(NewEntry(EntryKind.Expense, "Léo Casa", 7700 + month, date.AddDays(10), verde.Id, EntryStatus.Planned));
            rows.Add(NewEntry(EntryKind.Income, "Duda", 1827, date.AddDays(15), verde.Id, month == 1 ? EntryStatus.Planned : EntryStatus.Settled));
        }

        // Lançamento que cai em outro ano, mas pesa no saldo inicial de janeiro; e outro em 2027, que não pode vazar para 2026.
        rows.Add(NewEntry(EntryKind.Income, "Bruna 2025", 50000, new DateOnly(2025, 12, 20), azul.Id, EntryStatus.Settled));
        rows.Add(NewEntry(EntryKind.Expense, "Carla 2027", 99900, new DateOnly(2027, 1, 20), azul.Id, EntryStatus.Planned));
        rows.Add(NewTransfer(azul.Id, verde.Id, 25000, new DateOnly(2026, 2, 3)));
        rows.Add(NewTransfer(verde.Id, azul.Id, 4000, new DateOnly(2026, 4, 9)));
        await Factory.SeedAsync(account, [.. rows]);

        foreach (var (description, amount, date) in new[]
        {
            ("Uber", "12,95", "2026-01-10"), ("Casa China", "301,40", "2026-02-12"), ("Shopee", "44", "2026-03-12"),
            ("Estorno Óculos", "-99,90", "2026-03-14"), ("Lanche", "209,62", "2026-03-16"), ("Mercado", "180", "2026-04-10"),
        })
        {
            var response = await http.PostAsJsonAsync("/api/v1/card-purchases", new { description, amount, date, creditCardId = card.Id });
            response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        }

        // Janeiro paga inteira, fevereiro parcialmente com a Verde; março fica aberta.
        (await http.PostAsJsonAsync($"{CardsUrl}/{card.Id}/invoices/2026-01/payments", new { })).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await http.PostAsJsonAsync($"{CardsUrl}/{card.Id}/invoices/2026-02/payments", new { amount = "100", walletId = verde.Id }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        return http;
    }

    [Fact]
    public async Task SC_001_cada_mes_do_painel_e_igual_a_visao_do_mes_sem_filtro()
    {
        var http = await SeedSpaceAsync();

        var panel = await http.GetFromJsonAsync<JsonElement>("/api/v1/years/2026/panel");

        panel.GetProperty("year").GetInt32().ShouldBe(2026);
        panel.GetProperty("today").GetString().ShouldBe("2026-03-20");
        var months = panel.GetProperty("months").EnumerateArray().ToList();
        months.Count.ShouldBe(12);

        for (var m = 1; m <= 12; m++)
        {
            var key = $"2026-{m:00}";
            var month = await http.GetFromJsonAsync<JsonElement>($"/api/v1/months/{key}");
            var item = months[m - 1];
            item.GetProperty("month").GetString().ShouldBe(key);
            foreach (var column in new[] { "planned", "actual" })
            {
                foreach (var field in Columns)
                {
                    item.GetProperty(column).GetProperty(field).GetInt64()
                        .ShouldBe(month.GetProperty(column).GetProperty(field).GetInt64(), $"{key} {column}.{field}");
                }
            }
        }
    }

    [Fact]
    public async Task Totais_somam_entradas_saidas_e_resultado_e_o_saldo_final_e_o_de_dezembro()
    {
        var http = await SeedSpaceAsync();

        var panel = await http.GetFromJsonAsync<JsonElement>("/api/v1/years/2026/panel");

        var months = panel.GetProperty("months").EnumerateArray().ToList();
        foreach (var column in new[] { "planned", "actual" })
        {
            var totals = panel.GetProperty("totals").GetProperty(column);
            long Sum(string field) => months.Sum(x => x.GetProperty(column).GetProperty(field).GetInt64());
            totals.GetProperty("incomeCents").GetInt64().ShouldBe(Sum("incomeCents"));
            totals.GetProperty("expenseCents").GetInt64().ShouldBe(Sum("expenseCents"));
            totals.GetProperty("resultCents").GetInt64().ShouldBe(Sum("resultCents"));
            totals.GetProperty("resultCents").GetInt64()
                .ShouldBe(totals.GetProperty("incomeCents").GetInt64() - totals.GetProperty("expenseCents").GetInt64());
            totals.GetProperty("finalBalanceCents").GetInt64().ShouldBe(months[11].GetProperty(column).GetProperty("finalBalanceCents").GetInt64());
        }

        // Dados com valor conhecido: janeiro do Previsto tem Salário 3.001,00 + Duda 18,27 e Aluguel 1.200,00 + Léo Casa 77,01 + fatura 12,95.
        var january = months[0].GetProperty("planned");
        january.GetProperty("incomeCents").GetInt64().ShouldBe(300100 + 1827);
        january.GetProperty("expenseCents").GetInt64().ShouldBe(120000 + 7701 + 1295);
    }

    [Fact]
    public async Task Espaco_sem_dados_devolve_12_meses_zerados()
    {
        _clock.UtcNow = DateTimeOffset.UtcNow;
        var (http, _) = await NewSpaceAsync(Factory);

        var panel = await http.GetFromJsonAsync<JsonElement>("/api/v1/years/2026/panel");

        var months = panel.GetProperty("months").EnumerateArray().ToList();
        months.Count.ShouldBe(12);
        months.ShouldAllBe(x => x.GetProperty("planned").GetProperty("finalBalanceCents").GetInt64() == 0
            && x.GetProperty("actual").GetProperty("incomeCents").GetInt64() == 0);
        panel.GetProperty("totals").GetProperty("planned").GetProperty("finalBalanceCents").GetInt64().ShouldBe(0);
    }

    [Theory]
    [InlineData("1999")]
    [InlineData("2101")]
    [InlineData("abcd")]
    public async Task Ano_fora_de_2000_a_2100_ou_invalido_devolve_422(string year)
    {
        _clock.UtcNow = DateTimeOffset.UtcNow;
        var (http, _) = await NewSpaceAsync(Factory);

        var response = await http.GetAsync($"/api/v1/years/{year}/panel");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Sem_login_devolve_401()
    {
        using var anonymous = Factory.CreateClient();

        var response = await anonymous.GetAsync("/api/v1/years/2026/panel");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Painel_nao_enxerga_dados_de_outro_Espaco()
    {
        _clock.UtcNow = DateTimeOffset.UtcNow;
        var (_, lia) = await NewSpaceAsync(Factory, "Lia");
        var azul = NewWallet("Azul", 100000, new DateOnly(2026, 1, 1), primary: true);
        await Factory.SeedAsync(lia, azul, NewEntry(EntryKind.Income, "Salário", 50000, new DateOnly(2026, 2, 5), azul.Id, EntryStatus.Settled));
        var (leo, _) = await NewSpaceAsync(Factory, "Léo");

        var panel = await leo.GetFromJsonAsync<JsonElement>("/api/v1/years/2026/panel");

        panel.GetProperty("totals").GetProperty("actual").GetProperty("incomeCents").GetInt64().ShouldBe(0);
        panel.GetProperty("totals").GetProperty("actual").GetProperty("finalBalanceCents").GetInt64().ShouldBe(0);
    }
}
