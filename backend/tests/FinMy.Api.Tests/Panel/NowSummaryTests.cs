using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Api.Tests.Support;
using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;
using static FinMy.Api.Tests.Cards.CardTestSupport;

namespace FinMy.Api.Tests.Panel;

/// <summary>
/// <c>GET /api/v1/now</c> (006 T002, research R2): cada bloco do Resumo de agora confere com a tela de origem
/// (US2.1 saldos, US2.2 faturas da janela de 30 dias, US2.3 pendências, US2.4 acertos) e as carteiras arquivadas.
/// </summary>
public class NowSummaryTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private static readonly DateTimeOffset Today = new(2026, 3, 20, 15, 0, 0, TimeSpan.Zero);

    private readonly FakeClock _clock = new(DateTimeOffset.UtcNow);

    protected override IClock? Clock => _clock;

    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?> { ["RateLimiting:Auth:PermitLimit"] = "100000" };

    private static async Task<Guid> PostAsync(HttpClient http, string url, object body)
    {
        var response = await http.PostAsJsonAsync(url, body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static Task<Guid> WalletAsync(HttpClient http, string name, string initial) =>
        PostAsync(http, "/api/v1/wallets", new { name, type = "checking", color = "#1E88E5", initialBalance = initial, initialBalanceDate = "2026-01-01" });

    private static async Task PurchaseAsync(HttpClient http, Guid cardId, string amount, string date)
    {
        var response = await http.PostAsJsonAsync("/api/v1/card-purchases", new { description = "Mercado", amount, date, creditCardId = cardId });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private static Task<Guid> EntryAsync(HttpClient http, Guid walletId, string kind, string amount, string date, string status, params Guid[] tagIds) =>
        PostAsync(http, "/api/v1/entries", new { kind, description = "Lançamento", amount, date, walletId, status, tagIds });

    // O cadastro emite o token com o relógio real; só depois o relógio vai para "hoje" (20/03/2026).
    private async Task<HttpClient> NewSpaceAtTodayAsync(string name = "Lia")
    {
        _clock.UtcNow = DateTimeOffset.UtcNow;
        var (http, _) = await NewSpaceAsync(Factory, name);
        _clock.UtcNow = Today;
        return http;
    }

    private async Task<(HttpClient Http, Guid Azul, Guid Verde)> SeedAsync()
    {
        var http = await NewSpaceAtTodayAsync();
        var azul = await WalletAsync(http, "Azul", "1800");
        var verde = await WalletAsync(http, "Verde", "150");
        return (http, azul, verde);
    }

    private static Task<JsonElement> NowAsync(HttpClient http) => http.GetFromJsonAsync<JsonElement>("/api/v1/now");

    [Fact]
    public async Task US2_1_mostra_o_saldo_atual_de_cada_carteira_e_o_total()
    {
        var (http, _, _) = await SeedAsync();

        var now = await NowAsync(http);

        now.GetProperty("today").GetString().ShouldBe("2026-03-20");
        var wallets = now.GetProperty("wallets").EnumerateArray().ToList();
        wallets.Select(w => w.GetProperty("name").GetString()).ShouldBe(["Azul", "Verde"]);
        wallets.Select(w => w.GetProperty("currentBalanceCents").GetInt64()).ShouldBe([180000L, 15000L]);
        wallets[0].GetProperty("color").GetString().ShouldBe("#1E88E5");
        now.GetProperty("archivedWalletsBalanceCents").GetInt64().ShouldBe(0);
        now.GetProperty("totalBalanceCents").GetInt64().ShouldBe(195000);
    }

    [Fact]
    public async Task Saldo_atual_acompanha_lancamentos_pagos_e_ignora_os_previstos()
    {
        var (http, azul, _) = await SeedAsync();
        await EntryAsync(http, azul, "income", "200", "2026-03-05", "settled");
        await EntryAsync(http, azul, "expense", "50", "2026-03-06", "settled");
        await EntryAsync(http, azul, "expense", "999", "2026-03-30", "planned");

        var now = await NowAsync(http);

        now.GetProperty("wallets")[0].GetProperty("currentBalanceCents").GetInt64().ShouldBe(180000 + 20000 - 5000);
        now.GetProperty("totalBalanceCents").GetInt64().ShouldBe(210000);
    }

    [Fact]
    public async Task Carteira_arquivada_com_saldo_sai_da_lista_e_entra_na_linha_Arquivadas_e_no_total()
    {
        var (http, _, _) = await SeedAsync();
        var cinza = await WalletAsync(http, "Cinza", "500");
        var vazia = await WalletAsync(http, "Roxa", "0");
        (await http.PostAsync($"/api/v1/wallets/{cinza}/archive", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await http.PostAsync($"/api/v1/wallets/{vazia}/archive", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var now = await NowAsync(http);

        now.GetProperty("wallets").EnumerateArray().Select(w => w.GetProperty("name").GetString()).ShouldBe(["Azul", "Verde"]);
        now.GetProperty("archivedWalletsBalanceCents").GetInt64().ShouldBe(50000);
        now.GetProperty("totalBalanceCents").GetInt64().ShouldBe(245000);
    }

    [Fact]
    public async Task US2_2_lista_faturas_da_janela_de_30_dias_em_ordem_de_vencimento()
    {
        var (http, azul, verde) = await SeedAsync();
        var cartaoAzul = await CreateCardAsync(http, "Azul", azul);
        var cartaoVerde = await CreateCardAsync(http, "Verde", verde, closingDay: 5, dueDay: 10);
        await PurchaseAsync(http, cartaoAzul.Id, "200", "2026-03-10"); // vence 25/03
        await PurchaseAsync(http, cartaoVerde.Id, "300", "2026-03-10"); // vence 10/04
        await PurchaseAsync(http, cartaoAzul.Id, "80", "2026-03-19"); // vence 25/04: mais de 30 dias

        var now = await NowAsync(http);

        var invoices = now.GetProperty("invoices").EnumerateArray().ToList();
        invoices.Select(i => i.GetProperty("creditCardName").GetString()).ShouldBe(["Azul", "Verde"]);
        invoices.Select(i => i.GetProperty("dueMonth").GetString()).ShouldBe(["2026-03", "2026-04"]);
        invoices.Select(i => i.GetProperty("dueDate").GetString()).ShouldBe(["2026-03-25", "2026-04-10"]);
        invoices.Select(i => i.GetProperty("remainingCents").GetInt64()).ShouldBe([20000L, 30000L]);
        invoices[0].GetProperty("creditCardId").GetGuid().ShouldBe(cartaoAzul.Id);

        // A situação e o Restante são os da visão do mês.
        var march = await http.GetFromJsonAsync<JsonElement>("/api/v1/months/2026-03");
        var line = march.GetProperty("invoices").EnumerateArray().Single(i => i.GetProperty("creditCardId").GetGuid() == cartaoAzul.Id);
        invoices[0].GetProperty("status").GetString().ShouldBe(line.GetProperty("status").GetString());
        invoices[0].GetProperty("remainingCents").GetInt64().ShouldBe(line.GetProperty("remainingCents").GetInt64());
    }

    [Fact]
    public async Task Fatura_vencida_ha_ate_30_dias_aparece_e_mais_antiga_nao()
    {
        var (http, azul, _) = await SeedAsync();
        var cartao = await CreateCardAsync(http, "Azul", azul);
        await PurchaseAsync(http, cartao.Id, "60", "2026-01-10"); // vence 25/01: fora (54 dias)
        await PurchaseAsync(http, cartao.Id, "70", "2026-01-25"); // vence 25/02: vencida há 23 dias

        var invoices = (await NowAsync(http)).GetProperty("invoices").EnumerateArray().ToList();

        invoices.Count.ShouldBe(1);
        invoices[0].GetProperty("dueDate").GetString().ShouldBe("2026-02-25");
        invoices[0].GetProperty("remainingCents").GetInt64().ShouldBe(7000);
    }

    [Fact]
    public async Task Fatura_quitada_nao_aparece_e_pagamento_parcial_mostra_o_restante()
    {
        var (http, azul, verde) = await SeedAsync();
        var cartaoAzul = await CreateCardAsync(http, "Azul", azul);
        var cartaoVerde = await CreateCardAsync(http, "Verde", verde, closingDay: 5, dueDay: 10);
        await PurchaseAsync(http, cartaoAzul.Id, "200", "2026-03-10");
        await PurchaseAsync(http, cartaoVerde.Id, "300", "2026-03-10");
        (await http.PostAsJsonAsync($"{CardsUrl}/{cartaoAzul.Id}/invoices/2026-03/payments", new { })).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await http.PostAsJsonAsync($"{CardsUrl}/{cartaoVerde.Id}/invoices/2026-04/payments", new { amount = "100", walletId = verde }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var invoices = (await NowAsync(http)).GetProperty("invoices").EnumerateArray().ToList();

        invoices.Count.ShouldBe(1);
        invoices[0].GetProperty("creditCardName").GetString().ShouldBe("Verde");
        invoices[0].GetProperty("remainingCents").GetInt64().ShouldBe(20000);
    }

    [Fact]
    public async Task US2_3_pendencias_contam_previstas_de_meses_anteriores()
    {
        var (http, azul, _) = await SeedAsync();
        await EntryAsync(http, azul, "expense", "100", "2026-02-10", "planned");
        await EntryAsync(http, azul, "expense", "25", "2026-01-10", "planned");
        await EntryAsync(http, azul, "income", "40", "2026-01-15", "planned");
        await EntryAsync(http, azul, "expense", "10", "2026-02-11", "settled"); // pago: não é pendência
        await EntryAsync(http, azul, "expense", "500", "2026-03-25", "planned"); // mês de hoje: não é pendência

        var now = await NowAsync(http);
        var month = await http.GetFromJsonAsync<JsonElement>("/api/v1/months/2026-03");

        var pending = now.GetProperty("pending");
        pending.GetProperty("count").GetInt32().ShouldBe(3);
        pending.GetProperty("toReceiveCents").GetInt64().ShouldBe(4000);
        pending.GetProperty("toPayCents").GetInt64().ShouldBe(12500);
        pending.GetRawText().ShouldBe(month.GetProperty("pending").GetRawText());
    }

    [Fact]
    public async Task US2_4_acertos_listam_o_saldo_acumulado_das_tags_e_omitem_as_zeradas()
    {
        var (http, azul, _) = await SeedAsync();
        var bruna = await PostAsync(http, "/api/v1/tags", new { name = "Bruna", isSettlement = true });
        var carla = await PostAsync(http, "/api/v1/tags", new { name = "Carla", isSettlement = true });
        var duda = await PostAsync(http, "/api/v1/tags", new { name = "Duda", isSettlement = true });
        await PostAsync(http, "/api/v1/tags", new { name = "Léo", isSettlement = true });
        await EntryAsync(http, azul, "expense", "100", "2026-02-10", "planned", bruna);
        await EntryAsync(http, azul, "expense", "70", "2026-03-10", "settled", bruna);
        await EntryAsync(http, azul, "expense", "50", "2026-03-11", "settled", carla);
        await EntryAsync(http, azul, "income", "70", "2026-03-12", "settled", carla);
        await EntryAsync(http, azul, "expense", "30", "2026-03-13", "settled", duda);
        await EntryAsync(http, azul, "income", "30", "2026-03-14", "settled", duda);
        await EntryAsync(http, azul, "expense", "900", "2026-04-10", "planned", bruna); // depois do mês de hoje

        var now = await NowAsync(http);
        var settlements = await http.GetFromJsonAsync<JsonElement>("/api/v1/settlements/2026-03");

        var rows = now.GetProperty("settlements").EnumerateArray().ToList();
        rows.Select(r => r.GetProperty("tagName").GetString()).ShouldBe(["Bruna", "Carla"]);
        rows.Select(r => r.GetProperty("accumulatedCents").GetInt64()).ShouldBe([17000L, -2000L]);
        rows[0].GetProperty("tagId").GetGuid().ShouldBe(bruna);
        foreach (var row in rows)
        {
            var source = settlements.EnumerateArray().Single(s => s.GetProperty("tagId").GetGuid() == row.GetProperty("tagId").GetGuid());
            row.GetProperty("accumulatedCents").GetInt64().ShouldBe(source.GetProperty("accumulatedCents").GetInt64());
        }
    }

    [Fact]
    public async Task Espaco_sem_dados_devolve_blocos_vazios()
    {
        var http = await NewSpaceAtTodayAsync();

        var now = await NowAsync(http);

        now.GetProperty("wallets").GetArrayLength().ShouldBe(0);
        now.GetProperty("invoices").GetArrayLength().ShouldBe(0);
        now.GetProperty("settlements").GetArrayLength().ShouldBe(0);
        now.GetProperty("totalBalanceCents").GetInt64().ShouldBe(0);
        now.GetProperty("archivedWalletsBalanceCents").GetInt64().ShouldBe(0);
        now.GetProperty("pending").GetProperty("count").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Sem_login_devolve_401()
    {
        using var anonymous = Factory.CreateClient();

        (await anonymous.GetAsync("/api/v1/now")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Resumo_nao_enxerga_dados_de_outro_Espaco()
    {
        var (lia, azulDeLia, _) = await SeedAsync();
        var cartao = await CreateCardAsync(lia, "Azul", azulDeLia);
        await PurchaseAsync(lia, cartao.Id, "200", "2026-03-10");
        await EntryAsync(lia, azulDeLia, "expense", "100", "2026-02-10", "planned");
        var leo = await NewSpaceAtTodayAsync("Léo");

        var now = await NowAsync(leo);

        now.GetProperty("wallets").GetArrayLength().ShouldBe(0);
        now.GetProperty("invoices").GetArrayLength().ShouldBe(0);
        now.GetProperty("pending").GetProperty("count").GetInt32().ShouldBe(0);
        now.GetProperty("totalBalanceCents").GetInt64().ShouldBe(0);
    }
}
