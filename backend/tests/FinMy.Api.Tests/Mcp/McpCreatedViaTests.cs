using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Application.Tests.Infrastructure;

namespace FinMy.Api.Tests.Mcp;

/// <summary>
/// 008 T006 / US2 (FR-008, research R5): lançamentos criados pelo Claude voltam com <c>createdVia = "claude"</c> nas respostas REST de
/// entradas, compras e transferências (item, busca, mês e fatura); os do app seguem <c>"app"</c>. Espaço "Lia" (dados fictícios).
/// </summary>
public class McpCreatedViaTests(PostgresFixture postgres) : McpTestBase(postgres)
{
    private static string Via(JsonElement element) => element.GetProperty("createdVia").GetString()!;

    private static JsonElement ById(JsonElement array, Guid id) =>
        array.EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == id);

    [Fact]
    public async Task Lancamentos_criados_pelo_claude_trazem_created_via_claude_e_os_do_app_trazem_app()
    {
        var (space, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var entryId = (await mcp.OkAsync("create_entry", new { kind = "expense", description = "Padaria da Carla", amount = "18,00" }))
            .GetProperty("entries")[0].GetProperty("id").GetGuid();
        var purchaseId = (await mcp.OkAsync("create_card_purchase", new { description = "Cinema do Léo", amount = "40,00", creditCard = "Verde" }))
            .GetProperty("purchases")[0].GetProperty("id").GetGuid();
        var transferId = (await mcp.OkAsync("create_transfer", new { fromWallet = "Azul", toWallet = "Caixa", amount = "10,00" }))
            .GetProperty("transfer").GetProperty("id").GetGuid();

        var http = space.Http;

        // Item: o semeado pelo REST do app continua "app".
        Via(await http.GetFromJsonAsync<JsonElement>($"/api/v1/entries/{entryId}")).ShouldBe("claude");
        Via(await http.GetFromJsonAsync<JsonElement>($"/api/v1/entries/{space.PendingEntry}")).ShouldBe("app");

        // Mês: lista de entradas e de transferências.
        var march = await http.GetFromJsonAsync<JsonElement>("/api/v1/months/2026-03");
        Via(ById(march.GetProperty("entries"), entryId)).ShouldBe("claude");
        Via(ById(march.GetProperty("transfers"), transferId)).ShouldBe("claude");
        march.GetProperty("entries").EnumerateArray().Where(e => e.GetProperty("id").GetGuid() != entryId).ShouldAllBe(e => Via(e) == "app");
        march.GetProperty("transfers").EnumerateArray().Where(t => t.GetProperty("id").GetGuid() != transferId).ShouldAllBe(t => Via(t) == "app");

        // Fatura (compra de hoje vence em abril) e busca do livro-razão.
        var invoice = await http.GetFromJsonAsync<JsonElement>($"/api/v1/credit-cards/{space.Verde}/invoices/2026-04");
        Via(ById(invoice.GetProperty("purchases"), purchaseId)).ShouldBe("claude");
        var marchInvoice = await http.GetFromJsonAsync<JsonElement>($"/api/v1/credit-cards/{space.Verde}/invoices/2026-03");
        marchInvoice.GetProperty("purchases").EnumerateArray().ShouldAllBe(p => Via(p) == "app");

        var search = await http.GetFromJsonAsync<JsonElement>("/api/v1/entries/search?limit=100");
        var vias = search.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("type").GetString() switch
            {
                "transfer" => (i.GetProperty("transfer").GetProperty("id").GetGuid(), Via(i.GetProperty("transfer"))),
                "card_purchase" => (i.GetProperty("purchase").GetProperty("id").GetGuid(), Via(i.GetProperty("purchase"))),
                _ => (i.GetProperty("entry").GetProperty("id").GetGuid(), Via(i.GetProperty("entry"))),
            })
            .ToDictionary(x => x.Item1, x => x.Item2);
        vias[entryId].ShouldBe("claude");
        vias[purchaseId].ShouldBe("claude");
        vias[transferId].ShouldBe("claude");
        vias.Values.Count(v => v == "app").ShouldBeGreaterThan(5);
    }
}
