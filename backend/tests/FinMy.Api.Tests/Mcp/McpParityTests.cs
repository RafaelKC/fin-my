#pragma warning disable CA1861 // arrays literais como argumentos dos testes

using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Api.Tests.Entries;
using FinMy.Application.Tests.Infrastructure;

namespace FinMy.Api.Tests.Mcp;

/// <summary>
/// 008 SC-002: os números das ferramentas de consulta são os mesmos que o app mostra — mesmo Espaço, mesmos handlers, só muda a
/// borda. Cada teste compara a resposta MCP com a da API REST usada pelas telas (saldos, mês, pendências, faturas, acertos, busca).
/// </summary>
public class McpParityTests(PostgresFixture postgres) : McpTestBase(postgres)
{
    private static readonly string[] Columns = ["planned", "actual"];

    private static readonly string[] ColumnFields =
        ["openingBalance", "walletsInitialBalance", "income", "expense", "result", "transfersIn", "transfersOut", "finalBalance"];

    private static async Task<JsonElement> GetAsync(HttpClient http, string url)
    {
        var response = await http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.JsonAsync();
    }

    private static string Brl(long cents)
    {
        var sign = cents < 0 ? "-" : string.Empty;
        var abs = Math.Abs(cents);
        return $"{sign}R$ {(abs / 100).ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))},{abs % 100:00}";
    }

    private static void SameMoney(JsonElement mcp, string name, long expectedCents)
    {
        mcp.GetProperty(name + "Cents").GetInt64().ShouldBe(expectedCents, name);
        mcp.GetProperty(name + "Text").GetString().ShouldBe(Brl(expectedCents), name);
    }

    [Fact]
    public async Task Saldos_das_carteiras_batem_com_GET_wallets()
    {
        var (space, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var rest = (await GetAsync(space.Http, "/api/v1/wallets")).EnumerateArray().ToList();
        var body = await mcp.OkAsync("get_wallet_balances");

        var wallets = body.GetProperty("wallets").EnumerateArray().ToList();
        wallets.Count.ShouldBe(rest.Count);
        foreach (var wallet in rest)
        {
            var name = wallet.GetProperty("name").GetString();
            SameMoney(wallets.Single(w => w.GetProperty("name").GetString() == name), "currentBalance", wallet.GetProperty("currentBalanceCents").GetInt64());
        }

        SameMoney(body, "total", rest.Sum(w => w.GetProperty("currentBalanceCents").GetInt64()));
    }

    [Theory]
    [InlineData("2026-02")]
    [InlineData("2026-03")]
    [InlineData("2026-04")]
    public async Task Resumo_do_mes_bate_com_GET_months(string month)
    {
        var (space, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var rest = await GetAsync(space.Http, $"/api/v1/months/{month}");
        var body = await mcp.OkAsync("get_month_summary", new { month });

        foreach (var column in Columns)
        {
            foreach (var field in ColumnFields)
            {
                SameMoney(body.GetProperty(column), field, rest.GetProperty(column).GetProperty(field + "Cents").GetInt64());
            }
        }

        body.GetProperty("pending").GetProperty("count").GetInt32().ShouldBe(rest.GetProperty("pending").GetProperty("count").GetInt32());
        SameMoney(body.GetProperty("pending"), "toPay", rest.GetProperty("pending").GetProperty("toPayCents").GetInt64());
        SameMoney(body.GetProperty("pending"), "toReceive", rest.GetProperty("pending").GetProperty("toReceiveCents").GetInt64());
    }

    [Fact]
    public async Task Resumo_do_mes_filtrado_por_carteira_bate_com_o_filtro_walletIds_do_app()
    {
        var (space, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var rest = await GetAsync(space.Http, $"/api/v1/months/2026-03?walletIds={space.Azul}");
        var body = await mcp.OkAsync("get_month_summary", new { month = "2026-03", wallets = new[] { "Azul" } });

        foreach (var column in Columns)
        {
            foreach (var field in ColumnFields)
            {
                SameMoney(body.GetProperty(column), field, rest.GetProperty(column).GetProperty(field + "Cents").GetInt64());
            }
        }
    }

    [Fact]
    public async Task Pendencias_batem_com_GET_entries_pending()
    {
        var (space, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var rest = (await GetAsync(space.Http, "/api/v1/entries/pending")).EnumerateArray().ToList();
        var body = await mcp.OkAsync("list_pending");

        var items = body.GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("id").GetGuid()).ShouldBe(rest.Select(r => r.GetProperty("id").GetGuid()));
        for (var i = 0; i < rest.Count; i++)
        {
            SameMoney(items[i], "amount", rest[i].GetProperty("amountCents").GetInt64());
            items[i].GetProperty("date").GetString().ShouldBe(rest[i].GetProperty("date").GetString());
        }
    }

    [Fact]
    public async Task Faturas_batem_com_GET_invoices_do_cartao()
    {
        var (space, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var listed = await mcp.OkAsync("list_invoices", new { creditCard = "Verde", from = "2026-02", to = "2026-05" });
        foreach (var invoice in listed.GetProperty("invoices").EnumerateArray())
        {
            var month = invoice.GetProperty("dueMonth").GetString();
            var rest = await GetAsync(space.Http, $"/api/v1/credit-cards/{space.Verde}/invoices/{month}");

            SameMoney(invoice, "total", rest.GetProperty("totalCents").GetInt64());
            SameMoney(invoice, "paid", rest.GetProperty("paidCents").GetInt64());
            SameMoney(invoice, "remaining", rest.GetProperty("remainingCents").GetInt64());
            invoice.GetProperty("status").GetString().ShouldBe(rest.GetProperty("status").GetString());
            invoice.GetProperty("closingDate").GetString().ShouldBe(rest.GetProperty("closingDate").GetString());
            invoice.GetProperty("dueDate").GetString().ShouldBe(rest.GetProperty("dueDate").GetString());
        }
    }

    [Fact]
    public async Task Fatura_com_compras_bate_com_GET_invoice()
    {
        var (space, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var rest = await GetAsync(space.Http, $"/api/v1/credit-cards/{space.Verde}/invoices/2026-03");
        var body = await mcp.OkAsync("get_invoice", new { creditCard = "Verde", month = "2026-03", includePurchases = true });

        SameMoney(body, "total", rest.GetProperty("totalCents").GetInt64());
        SameMoney(body, "paid", rest.GetProperty("paidCents").GetInt64());
        SameMoney(body, "remaining", rest.GetProperty("remainingCents").GetInt64());
        SameMoney(body, "reconciled", rest.GetProperty("reconciledCents").GetInt64());

        var purchases = body.GetProperty("purchases").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).Order();
        purchases.ShouldBe(rest.GetProperty("purchases").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).Order());

        var subtotals = body.GetProperty("settlementSubtotals").EnumerateArray().Select(s => s.GetProperty("amountCents").GetInt64()).Order();
        subtotals.ShouldBe(rest.GetProperty("settlementSubtotals").EnumerateArray().Select(s => s.GetProperty("cents").GetInt64()).Order());
    }

    [Fact]
    public async Task Acertos_batem_com_GET_settlements()
    {
        var (space, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var rest = (await GetAsync(space.Http, "/api/v1/settlements/2026-03")).EnumerateArray().Single();
        var row = (await mcp.OkAsync("get_settlements", new { month = "2026-03" })).GetProperty("settlements").EnumerateArray().Single();

        SameMoney(row, "spent", rest.GetProperty("spentCents").GetInt64());
        SameMoney(row, "charged", rest.GetProperty("chargedCents").GetInt64());
        SameMoney(row, "received", rest.GetProperty("receivedCents").GetInt64());
        SameMoney(row, "toCharge", rest.GetProperty("toChargeCents").GetInt64());
        SameMoney(row, "toReceive", rest.GetProperty("toReceiveCents").GetInt64());
        SameMoney(row, "accumulated", rest.GetProperty("accumulatedCents").GetInt64());

        var restItems = (await GetAsync(space.Http, $"/api/v1/settlements/2026-03/tags/{space.Bruna}/items")).EnumerateArray().ToList();
        var items = (await mcp.OkAsync("get_settlements", new { month = "2026-03", tag = "Bruna" })).GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("id").GetGuid()).ShouldBe(restItems.Select(i => i.GetProperty("id").GetGuid()));
        items.Select(i => i.GetProperty("signedCents").GetInt64()).ShouldBe(restItems.Select(i => i.GetProperty("signedCents").GetInt64()));
    }

    [Fact]
    public async Task Busca_bate_com_GET_entries_search()
    {
        var (space, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var rest = await GetAsync(space.Http, $"/api/v1/entries/search?kinds=expense,card_purchase&walletIds={space.Azul}&from=2026-03-01&to=2026-03-31&limit=50");
        var body = await mcp.OkAsync("search_entries", new { kinds = new[] { "expense", "card_purchase" }, wallets = new[] { "Azul" }, from = "2026-03-01", to = "2026-03-31" });

        SameMoney(body, "total", rest.GetProperty("totalCents").GetInt64());
        body.GetProperty("totalCount").GetInt32().ShouldBe(rest.GetProperty("totalCount").GetInt32());
        body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid())
            .ShouldBe(rest.GetProperty("items").EnumerateArray().Select(i => (i.TryGetProperty("entry", out var e) && e.ValueKind == JsonValueKind.Object ? e : i.GetProperty("purchase")).GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task Formatacao_em_reais_do_texto_segue_o_padrao_do_app_para_negativos_e_milhares()
    {
        var (space, mcp) = await ConnectLiaAsync();
        await using var session = mcp;
        // Uma saída grande leva o saldo da carteira Caixa ao negativo, cobrindo "-R$ 1.234,56".
        var response = await space.Http.PostAsJsonAsync("/api/v1/entries", new
        {
            kind = "expense",
            description = "Reforma",
            amount = "1434,56",
            date = "2026-03-15",
            walletId = space.Caixa,
            status = "settled",
            referenceMonth = "2026-03",
        });
        response.EnsureSuccessStatusCode();

        var body = await mcp.OkAsync("get_wallet_balances");

        var caixa = body.GetProperty("wallets").EnumerateArray().Single(w => w.GetProperty("name").GetString() == "Caixa");
        SameMoney(caixa, "currentBalance", 20000 - 143456);
        caixa.GetProperty("currentBalanceText").GetString().ShouldBe("-R$ 1.234,56");
    }
}
