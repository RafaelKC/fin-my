using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Api.Tests.Cards;
using FinMy.Api.Tests.Entries;
using FinMy.Api.Tests.Support;
using FinMy.Application.Tests.Infrastructure;
using static FinMy.Api.Tests.Cards.CardTestSupport;

namespace FinMy.Api.Tests.Settlements;

/// <summary>
/// <c>GET /settlements/{month}</c> e <c>GET /settlements/{month}/tags/{tagId}/items</c> (004 US2.1–US2.3, SC-002; research R3).
/// Exemplo numérico da US2 com a Tag de acerto "Bruna" (compras de Débito e Crédito na fatura Verde de 03/2026,
/// Saída e Entradas pelo Mês de referência). Cartão Verde fecha dia 18 e vence dia 25; dados fictícios.
/// </summary>
public class SettlementEndpointsTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?> { ["RateLimiting:Auth:PermitLimit"] = "100000" };

    private sealed record Space(HttpClient Http, Guid WalletId, Guid CardId, Guid Bruna);

    private async Task<Space> NewSpaceAsync(string name = "Lia")
    {
        var (http, _) = await CardTestSupport.NewSpaceAsync(Factory, name);
        var wallet = await CreateWalletAsync(http, "Azul");
        var card = await CreateCardAsync(http, "Verde", wallet.Id);
        return new Space(http, wallet.Id, card.Id, await TagAsync(http, "Bruna", isSettlement: true));
    }

    private static async Task<Guid> TagAsync(HttpClient http, string name, bool isSettlement = false)
    {
        var response = await http.PostAsJsonAsync("/api/v1/tags", new { name, isSettlement });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    private static async Task<Guid> EntryAsync(
        Space s, string kind, string description, string amount, string date, string referenceMonth, string status, params Guid[] tagIds)
    {
        var body = await OkAsync(
            await s.Http.PostAsJsonAsync("/api/v1/entries", new { kind, description, amount, date, walletId = s.WalletId, status, referenceMonth, tagIds }),
            HttpStatusCode.Created);
        return body.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> PurchaseAsync(
        Space s, string description, string amount, string date, string nature, Guid[] tagIds, object? installments = null)
    {
        var body = await OkAsync(
            await s.Http.PostAsJsonAsync("/api/v1/card-purchases", new { description, amount, date, nature, creditCardId = s.CardId, tagIds, installments }),
            HttpStatusCode.Created);
        return body.GetProperty("purchases")[0];
    }

    /// <summary>O exemplo da US2: Bruna em março de 2026 (retorna o id da Entrada "Bruna" de 150,00).</summary>
    private static async Task<Guid> SeedExampleAsync(Space s)
    {
        await PurchaseAsync(s, "Ifood", "156,81", "2026-03-05", "debit", [s.Bruna]);
        await PurchaseAsync(s, "Uber", "12,72", "2026-03-06", "debit", [s.Bruna]);
        await PurchaseAsync(s, "Estorno", "9,53", "2026-03-07", "credit", [s.Bruna]);
        await EntryAsync(s, "expense", "Presente Bruna", "40,00", "2026-03-08", "2026-03", "settled", s.Bruna);
        var planned = await EntryAsync(s, "income", "Bruna", "150,00", "2026-03-20", "2026-03", "planned", s.Bruna);
        await EntryAsync(s, "income", "Bruna Pix", "30,00", "2026-03-09", "2026-03", "settled", s.Bruna);
        return planned;
    }

    private static async Task<JsonElement> RowAsync(Space s, string month, Guid tagId)
    {
        var rows = await OkAsync(await s.Http.GetAsync($"/api/v1/settlements/{month}"));
        return rows.EnumerateArray().Single(r => r.GetProperty("tagId").GetGuid() == tagId);
    }

    private static void Figures(JsonElement row, long spent, long charged, long received, long toCharge, long toReceive, long accumulated)
    {
        row.GetProperty("spentCents").GetInt64().ShouldBe(spent);
        row.GetProperty("chargedCents").GetInt64().ShouldBe(charged);
        row.GetProperty("receivedCents").GetInt64().ShouldBe(received);
        row.GetProperty("toChargeCents").GetInt64().ShouldBe(toCharge);
        row.GetProperty("toReceiveCents").GetInt64().ShouldBe(toReceive);
        row.GetProperty("accumulatedCents").GetInt64().ShouldBe(accumulated);
    }

    [Fact]
    public async Task US2_1_exemplo_da_Bruna_em_marco_inclui_compras_de_fatura_e_credito()
    {
        var s = await NewSpaceAsync();
        await SeedExampleAsync(s);

        var row = await RowAsync(s, "2026-03", s.Bruna);

        row.GetProperty("tagName").GetString().ShouldBe("Bruna");
        row.GetProperty("archived").GetBoolean().ShouldBeFalse();
        Figures(row, spent: 20000, charged: 18000, received: 3000, toCharge: 2000, toReceive: 17000, accumulated: 17000);
    }

    [Fact]
    public async Task US2_2_entrada_marcada_como_recebida_passa_para_recebido_e_reduz_o_a_receber_e_o_saldo()
    {
        var s = await NewSpaceAsync();
        var planned = await SeedExampleAsync(s);

        await OkAsync(await s.Http.PatchAsJsonAsync($"/api/v1/entries/{planned}/status", new { status = "settled" }));

        Figures(await RowAsync(s, "2026-03", s.Bruna), spent: 20000, charged: 18000, received: 18000, toCharge: 2000, toReceive: 2000, accumulated: 2000);
    }

    [Fact]
    public async Task US2_3_saldo_acumulado_soma_os_meses_anteriores_e_fica_negativo_quando_a_pessoa_pagou_a_mais()
    {
        var s = await NewSpaceAsync();
        await SeedExampleAsync(s);
        await EntryAsync(s, "expense", "Cinema Bruna", "100,00", "2026-02-10", "2026-02", "settled", s.Bruna);
        await EntryAsync(s, "income", "Bruna Pix", "500,00", "2026-04-10", "2026-04", "settled", s.Bruna);

        var february = await RowAsync(s, "2026-02", s.Bruna);
        var march = await RowAsync(s, "2026-03", s.Bruna);
        var april = await RowAsync(s, "2026-04", s.Bruna);
        var january = await RowAsync(s, "2026-01", s.Bruna);

        Figures(january, 0, 0, 0, 0, 0, 0);
        Figures(february, 10000, 0, 0, 10000, 10000, 10000);
        Figures(march, 20000, 18000, 3000, 2000, 17000, 27000);
        Figures(april, 0, 50000, 50000, -50000, -50000, -23000);
    }

    [Fact]
    public async Task Parcela_conta_como_gasto_no_mes_da_fatura_e_nao_no_mes_da_compra()
    {
        var s = await NewSpaceAsync();
        var carla = await TagAsync(s.Http, "Carla", isSettlement: true);
        // Comprada depois do fechamento de fevereiro: a 1ª parcela cai na fatura de 03/2026.
        await PurchaseAsync(s, "Geladeira", "300,00", "2026-02-20", "debit", [carla], new { total = 3, mode = "total_amount" });

        Figures(await RowAsync(s, "2026-02", carla), 0, 0, 0, 0, 0, 0);
        Figures(await RowAsync(s, "2026-03", carla), 10000, 0, 0, 10000, 10000, 10000);
        Figures(await RowAsync(s, "2026-04", carla), 10000, 0, 0, 10000, 10000, 20000);

        var items = await OkAsync(await s.Http.GetAsync($"/api/v1/settlements/2026-03/tags/{carla}/items"));
        var item = items.EnumerateArray().Single();
        item.GetProperty("description").GetString().ShouldBe("Geladeira 1/3");
        item.GetProperty("signedCents").GetInt64().ShouldBe(10000);
    }

    [Fact]
    public async Task Itens_listam_os_lancamentos_que_compoem_Gasto_e_Cobrado_com_credito_negativo_e_recebida()
    {
        var s = await NewSpaceAsync();
        await SeedExampleAsync(s);
        await EntryAsync(s, "expense", "Outro mês", "5,00", "2026-04-02", "2026-04", "settled", s.Bruna);

        var items = (await OkAsync(await s.Http.GetAsync($"/api/v1/settlements/2026-03/tags/{s.Bruna}/items"))).EnumerateArray().ToList();

        items.Select(i => i.GetProperty("description").GetString()).ShouldBe(
            ["Ifood", "Uber", "Estorno", "Presente Bruna", "Bruna Pix", "Bruna"]);
        items.Select(i => i.GetProperty("group").GetString()).ShouldBe(["spent", "spent", "spent", "spent", "charged", "charged"]);
        items.Select(i => i.GetProperty("type").GetString()).ShouldBe(["card_purchase", "card_purchase", "card_purchase", "expense", "income", "income"]);
        items.Select(i => i.GetProperty("signedCents").GetInt64()).ShouldBe([15681, 1272, -953, 4000, 3000, 15000]);
        items.Select(i => i.GetProperty("date").GetString()).ShouldBe(
            ["2026-03-05", "2026-03-06", "2026-03-07", "2026-03-08", "2026-03-09", "2026-03-20"]);
        items.Take(3).ShouldAllBe(i => i.GetProperty("creditCardId").GetGuid() == s.CardId);
        items[3].GetProperty("creditCardId").ValueKind.ShouldBe(JsonValueKind.Null);
        items[3].GetProperty("received").ValueKind.ShouldBe(JsonValueKind.Null);
        items[4].GetProperty("received").GetBoolean().ShouldBeTrue();
        items[5].GetProperty("received").GetBoolean().ShouldBeFalse();
        items.Sum(i => i.GetProperty("group").GetString() == "spent" ? i.GetProperty("signedCents").GetInt64() : 0).ShouldBe(20000);
    }

    [Fact]
    public async Task Lista_so_tags_de_acerto_ativas_e_arquivadas_apenas_quando_tem_valor_no_mes()
    {
        var s = await NewSpaceAsync();
        var carla = await TagAsync(s.Http, "Carla", isSettlement: true);
        var duda = await TagAsync(s.Http, "Duda", isSettlement: true);
        var mercado = await TagAsync(s.Http, "Mercado");
        await EntryAsync(s, "expense", "Pizza", "60,00", "2026-03-12", "2026-03", "settled", carla, mercado);
        await EntryAsync(s, "expense", "Sem tag", "9,00", "2026-03-12", "2026-03", "settled");
        foreach (var id in new[] { carla, duda })
        {
            await OkAsync(await s.Http.PostAsync($"/api/v1/tags/{id}/archive", null));
        }

        var march = (await OkAsync(await s.Http.GetAsync("/api/v1/settlements/2026-03"))).EnumerateArray().ToList();
        var april = (await OkAsync(await s.Http.GetAsync("/api/v1/settlements/2026-04"))).EnumerateArray().ToList();

        march.Select(r => r.GetProperty("tagName").GetString()).ShouldBe(["Bruna", "Carla"]);
        march[1].GetProperty("archived").GetBoolean().ShouldBeTrue();
        march[1].GetProperty("spentCents").GetInt64().ShouldBe(6000);
        april.Select(r => r.GetProperty("tagName").GetString()).ShouldBe(["Bruna"]);
    }

    [Fact]
    public async Task Mes_invalido_da_422_tag_inexistente_ou_de_outro_espaco_da_404_e_sem_login_da_401()
    {
        var s = await NewSpaceAsync();
        await SeedExampleAsync(s);
        var (other, _) = await CardTestSupport.NewSpaceAsync(Factory, "Léo");

        var invalid = await s.Http.GetAsync("/api/v1/settlements/2026-13");
        var invalidItems = await s.Http.GetAsync($"/api/v1/settlements/2026-13/tags/{s.Bruna}/items");
        var unknown = await s.Http.GetAsync($"/api/v1/settlements/2026-03/tags/{Guid.NewGuid()}/items");
        var foreign = await other.GetAsync($"/api/v1/settlements/2026-03/tags/{s.Bruna}/items");
        var foreignRows = await OkAsync(await other.GetAsync("/api/v1/settlements/2026-03"));
        using var anonymous = Factory.CreateClient();
        var unauthorized = await anonymous.GetAsync("/api/v1/settlements/2026-03");

        invalid.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await invalid.CodeAsync()).ShouldBe("month.invalid");
        invalidItems.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        foreignRows.GetArrayLength().ShouldBe(0);
        unauthorized.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
