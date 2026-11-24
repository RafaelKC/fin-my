#pragma warning disable CA1861 // arrays literais como argumentos dos testes

using System.Net;
using System.Text;
using System.Text.Json;
using FinMy.Application.Tests.Infrastructure;

namespace FinMy.Api.Tests.Mcp;

/// <summary>
/// 008 T003 / US3: contrato das ferramentas de consulta do servidor MCP (contracts/mcp-tools.md): autenticação, descoberta das
/// ferramentas, parâmetros, formato das respostas (centavos e texto) e erros de ferramenta. "Hoje" é 2026-03-20.
/// </summary>
public class McpToolContractTests(PostgresFixture postgres) : McpTestBase(postgres)
{
    private static readonly string[] QueryTools =
    [
        "list_catalog", "get_wallet_balances", "get_month_summary", "list_pending",
        "list_invoices", "get_invoice", "get_settlements", "search_entries",
    ];

    private static void Money(JsonElement element, string name, long cents, string text)
    {
        element.GetProperty(name + "Cents").GetInt64().ShouldBe(cents);
        element.GetProperty(name + "Text").GetString().ShouldBe(text);
    }

    // ---- Servidor e autenticação ----

    [Fact]
    public async Task Sem_token_o_mcp_responde_401_com_resource_metadata()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");

        var response = await Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().ShouldContain($"resource_metadata=\"{AppUrl}/.well-known/oauth-protected-resource\"");
        response.Headers.WwwAuthenticate.ToString().ShouldStartWith("Bearer");
    }

    [Fact]
    public async Task Token_do_app_nao_serve_no_mcp()
    {
        var (http, account) = await Cards.CardTestSupport.NewSpaceAsync(Factory);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", account.AccessToken);

        var response = await Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().ShouldContain("resource_metadata=");
        http.Dispose();
    }

    [Fact]
    public async Task Lista_as_ferramentas_de_consulta_somente_leitura_com_descricoes_em_portugues()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var tools = await mcp.Client.ListToolsAsync();

        var queries = tools.Where(t => t.ProtocolTool.Annotations?.ReadOnlyHint == true).ToList();
        queries.Select(t => t.Name).ShouldBe(QueryTools, ignoreOrder: true);
        foreach (var tool in queries)
        {
            tool.ProtocolTool.Annotations!.ReadOnlyHint.ShouldBe(true, tool.Name);
            tool.ProtocolTool.Annotations!.DestructiveHint.ShouldNotBe(true, tool.Name);
            tool.Description.ShouldNotBeNullOrWhiteSpace(tool.Name);
        }

        var invoice = tools.Single(t => t.Name == "get_invoice").ProtocolTool.InputSchema.GetProperty("properties");
        invoice.GetProperty("creditCard").GetProperty("description").GetString().ShouldBe("Nome do cartão.");
        invoice.TryGetProperty("includePurchases", out _).ShouldBeTrue();
        tools.Single(t => t.Name == "get_invoice").ProtocolTool.InputSchema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ShouldBe(["creditCard"]);
    }

    // ---- list_catalog ----

    [Fact]
    public async Task List_catalog_devolve_so_os_ativos_e_a_data_de_hoje()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var body = await mcp.OkAsync("list_catalog");

        body.GetProperty("today").GetString().ShouldBe("2026-03-20");
        body.GetProperty("wallets").EnumerateArray().Select(w => w.GetProperty("name").GetString()).ShouldBe(["Azul", "Caixa"], ignoreOrder: true);
        body.GetProperty("wallets").EnumerateArray().Single(w => w.GetProperty("name").GetString() == "Azul").GetProperty("isPrimary").GetBoolean().ShouldBeTrue();
        var card = body.GetProperty("creditCards").EnumerateArray().Single();
        card.GetProperty("name").GetString().ShouldBe("Verde");
        card.GetProperty("closingDay").GetInt32().ShouldBe(18);
        card.GetProperty("dueDay").GetInt32().ShouldBe(25);
        body.GetProperty("tags").EnumerateArray().Select(t => (t.GetProperty("name").GetString(), t.GetProperty("isSettlement").GetBoolean()))
            .ShouldBe([("Bruna", true), ("Lazer", false)], ignoreOrder: true);
    }

    // ---- get_wallet_balances ----

    [Fact]
    public async Task Get_wallet_balances_devolve_saldo_por_carteira_ativa_e_o_total()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var body = await mcp.OkAsync("get_wallet_balances");

        body.GetProperty("today").GetString().ShouldBe("2026-03-20");
        var wallets = body.GetProperty("wallets").EnumerateArray().ToDictionary(w => w.GetProperty("name").GetString()!);
        wallets.Keys.ShouldBe(["Azul", "Caixa"], ignoreOrder: true);
        // Azul: +5000,00 −1500,00 −40,00 +30,00 −200,00 (a Mercado planejada e a Luz pendente não contam; pagamento da fatura −50,00).
        Money(wallets["Azul"], "currentBalance", 324000, "R$ 3.240,00");
        Money(wallets["Caixa"], "currentBalance", 20000, "R$ 200,00");
        Money(body, "total", 344000, "R$ 3.440,00");
    }

    // ---- get_month_summary ----

    [Fact]
    public async Task Get_month_summary_usa_o_mes_de_hoje_e_traz_previsto_realizado_e_pendencias()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var body = await mcp.OkAsync("get_month_summary");

        body.GetProperty("month").GetString().ShouldBe("2026-03");
        foreach (var column in new[] { "planned", "actual" })
        {
            foreach (var field in new[] { "openingBalance", "walletsInitialBalance", "income", "expense", "result", "transfersIn", "transfersOut", "finalBalance" })
            {
                body.GetProperty(column).GetProperty(field + "Cents").ValueKind.ShouldBe(JsonValueKind.Number, $"{column}.{field}");
                body.GetProperty(column).GetProperty(field + "Text").GetString()!.ShouldContain("R$", Case.Sensitive, $"{column}.{field}");
            }
        }

        body.GetProperty("pending").GetProperty("count").GetInt32().ShouldBe(1);
        Money(body.GetProperty("pending"), "toPay", 12000, "R$ 120,00");
        Money(body.GetProperty("pending"), "toReceive", 0, "R$ 0,00");
        body.GetProperty("actual").GetProperty("incomeText").GetString().ShouldBe("R$ 5.030,00");
    }

    [Fact]
    public async Task Get_month_summary_aceita_mes_e_carteiras_por_nome_sem_diferenciar_caixa_e_acento()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var all = await mcp.OkAsync("get_month_summary", new { month = "2026-03" });
        var caixa = await mcp.OkAsync("get_month_summary", new { month = "2026-03", wallets = new[] { "  CAIXA " } });

        Money(caixa.GetProperty("actual"), "finalBalance", 20000, "R$ 200,00");
        caixa.GetProperty("actual").GetProperty("finalBalanceCents").GetInt64()
            .ShouldNotBe(all.GetProperty("actual").GetProperty("finalBalanceCents").GetInt64());
    }

    [Fact]
    public async Task Carteira_inexistente_ou_arquivada_vira_ambiguous_or_not_found_com_as_opcoes_ativas()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        foreach (var name in new[] { "Roxa", "Velha", "Az" })
        {
            var error = await mcp.ErrorAsync("get_month_summary", new { wallets = new[] { name } });

            error.GetProperty("code").GetString().ShouldBe("ambiguous_or_not_found");
            error.GetProperty("field").GetString().ShouldBe("wallets");
            error.GetProperty("message").GetString()!.ShouldContain(name);
            error.GetProperty("options").EnumerateArray().Select(o => o.GetString()).ShouldBe(["Azul", "Caixa"]);
        }
    }

    [Fact]
    public async Task Mes_invalido_vira_erro_de_ferramenta_com_o_codigo_da_api()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var error = await mcp.ErrorAsync("get_month_summary", new { month = "03/2026" });

        error.GetProperty("code").GetString().ShouldBe("month.invalid");
        error.GetProperty("field").GetString().ShouldBe("month");
        error.GetProperty("message").GetString()!.ShouldContain("AAAA-MM");
    }

    // ---- list_pending ----

    [Fact]
    public async Task List_pending_devolve_as_previstas_de_meses_anteriores()
    {
        var (space, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var body = await mcp.OkAsync("list_pending");

        body.GetProperty("today").GetString().ShouldBe("2026-03-20");
        body.GetProperty("count").GetInt32().ShouldBe(1);
        var item = body.GetProperty("items").EnumerateArray().Single();
        item.GetProperty("id").GetGuid().ShouldBe(space.PendingEntry);
        item.GetProperty("kind").GetString().ShouldBe("expense");
        item.GetProperty("description").GetString().ShouldBe("Luz");
        item.GetProperty("date").GetString().ShouldBe("2026-02-10");
        item.GetProperty("referenceMonth").GetString().ShouldBe("2026-02");
        Money(item, "amount", 12000, "R$ 120,00");
    }

    // ---- list_invoices ----

    [Fact]
    public async Task List_invoices_padrao_traz_tres_meses_antes_e_depois_da_fatura_atual()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var body = await mcp.OkAsync("list_invoices", new { creditCard = "verde" });

        body.GetProperty("creditCard").GetString().ShouldBe("Verde");
        var invoices = body.GetProperty("invoices").EnumerateArray().ToList();
        invoices.Count.ShouldBe(7);
        invoices.Select(i => i.GetProperty("dueMonth").GetString()).ShouldBe(
            ["2026-01", "2026-02", "2026-03", "2026-04", "2026-05", "2026-06", "2026-07"]);
        var march = invoices.Single(i => i.GetProperty("dueMonth").GetString() == "2026-03");
        march.GetProperty("closingDate").GetString().ShouldBe("2026-03-18");
        march.GetProperty("dueDate").GetString().ShouldBe("2026-03-25");
        // Débitos 156,81 + 12,72 + 60,00 − crédito 9,53 = 220,00; pago 50,00.
        Money(march, "total", 22000, "R$ 220,00");
        Money(march, "paid", 5000, "R$ 50,00");
        Money(march, "remaining", 17000, "R$ 170,00");
        march.GetProperty("status").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task List_invoices_respeita_o_intervalo_e_recusa_mais_de_24_meses()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var range = await mcp.OkAsync("list_invoices", new { creditCard = "Verde", from = "2026-02", to = "2026-04" });
        range.GetProperty("invoices").EnumerateArray().Select(i => i.GetProperty("dueMonth").GetString()).ShouldBe(["2026-02", "2026-03", "2026-04"]);

        var tooLong = await mcp.ErrorAsync("list_invoices", new { creditCard = "Verde", from = "2024-01", to = "2026-04" });
        tooLong.GetProperty("code").GetString().ShouldBe("period.too_long");

        var backwards = await mcp.ErrorAsync("list_invoices", new { creditCard = "Verde", from = "2026-05", to = "2026-04" });
        backwards.GetProperty("code").GetString().ShouldBe("period.invalid");
    }

    [Fact]
    public async Task Cartao_inexistente_vira_ambiguous_or_not_found_com_os_cartoes_ativos()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var error = await mcp.ErrorAsync("list_invoices", new { creditCard = "Roxo" });

        error.GetProperty("code").GetString().ShouldBe("ambiguous_or_not_found");
        error.GetProperty("field").GetString().ShouldBe("creditCard");
        error.GetProperty("options").EnumerateArray().Select(o => o.GetString()).ShouldBe(["Verde"]);
    }

    // ---- get_invoice ----

    [Fact]
    public async Task Get_invoice_sem_mes_devolve_a_fatura_atual_sem_compras_por_padrao()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var body = await mcp.OkAsync("get_invoice", new { creditCard = "Verde" });

        // Hoje (20/03) já passou do fechamento (18/03): a fatura atual é a que vence em abril, ainda vazia.
        body.GetProperty("creditCard").GetString().ShouldBe("Verde");
        body.TryGetProperty("purchases", out _).ShouldBeFalse();
        body.GetProperty("payments").GetArrayLength().ShouldBe(0);
        body.GetProperty("dueMonth").GetString().ShouldBe("2026-04");
        Money(body, "total", 0, "R$ 0,00");
    }

    [Fact]
    public async Task Get_invoice_com_compras_traz_pagamentos_tags_e_subtotais_por_tag_de_acerto()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var body = await mcp.OkAsync("get_invoice", new { creditCard = "Verde", month = "2026-03", includePurchases = true });

        body.GetProperty("dueMonth").GetString().ShouldBe("2026-03");
        Money(body, "total", 22000, "R$ 220,00");
        Money(body, "paid", 5000, "R$ 50,00");
        Money(body, "remaining", 17000, "R$ 170,00");
        body.GetProperty("previousMonth").GetString().ShouldBe("2026-02");
        body.GetProperty("nextMonth").GetString().ShouldBe("2026-04");

        var payment = body.GetProperty("payments").EnumerateArray().Single();
        payment.GetProperty("wallet").GetString().ShouldBe("Azul");
        payment.GetProperty("date").GetString().ShouldBe("2026-03-19");
        Money(payment, "amount", 5000, "R$ 50,00");

        var purchases = body.GetProperty("purchases").EnumerateArray().ToDictionary(p => p.GetProperty("description").GetString()!);
        purchases.Keys.ShouldBe(["Ifood", "Uber", "Estorno", "Cinema"], ignoreOrder: true);
        purchases["Ifood"].GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ShouldBe(["Bruna"]);
        purchases["Estorno"].GetProperty("nature").GetString().ShouldBe("credit");
        Money(purchases["Ifood"], "amount", 15681, "R$ 156,81");

        // Bruna: 156,81 + 12,72 − 9,53 = 160,00; sem tag de acerto: 60,00.
        var subtotals = body.GetProperty("settlementSubtotals").EnumerateArray().ToList();
        subtotals.Count.ShouldBe(2);
        subtotals.Single(s => s.TryGetProperty("tag", out var t) && t.GetString() == "Bruna").GetProperty("amountText").GetString().ShouldBe("R$ 160,00");
        subtotals.Single(s => s.GetProperty("tag").ValueKind == JsonValueKind.Null).GetProperty("amountText").GetString().ShouldBe("R$ 60,00");
    }

    [Fact]
    public async Task Get_invoice_com_mes_invalido_devolve_o_erro_da_api()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var error = await mcp.ErrorAsync("get_invoice", new { creditCard = "Verde", month = "amanhã" });

        error.GetProperty("code").GetString().ShouldBe("month.invalid");
    }

    // ---- get_settlements ----

    [Fact]
    public async Task Get_settlements_lista_as_tags_de_acerto_do_mes()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var body = await mcp.OkAsync("get_settlements", new { month = "2026-03" });

        body.GetProperty("month").GetString().ShouldBe("2026-03");
        body.TryGetProperty("items", out _).ShouldBeFalse();
        var row = body.GetProperty("settlements").EnumerateArray().Single();
        row.GetProperty("tag").GetString().ShouldBe("Bruna");
        // Gasto: compras 160,00 + saída 40,00; cobrado: entrada recebida 30,00.
        Money(row, "spent", 20000, "R$ 200,00");
        Money(row, "received", 3000, "R$ 30,00");
        Money(row, "toReceive", 17000, "R$ 170,00");
    }

    [Fact]
    public async Task Get_settlements_com_tag_traz_os_lancamentos_que_compoem_os_numeros()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var body = await mcp.OkAsync("get_settlements", new { tag = "bruna" });

        body.GetProperty("month").GetString().ShouldBe("2026-03");
        var items = body.GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("description").GetString()).ShouldBe(["Ifood", "Uber", "Estorno", "Presente Bruna", "Bruna Pix"], ignoreOrder: true);
        items.Single(i => i.GetProperty("description").GetString() == "Ifood").GetProperty("creditCard").GetString().ShouldBe("Verde");
        items.Single(i => i.GetProperty("description").GetString() == "Estorno").GetProperty("signedCents").GetInt64().ShouldBe(-953);
    }

    [Fact]
    public async Task Get_settlements_so_aceita_tag_de_acerto()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var error = await mcp.ErrorAsync("get_settlements", new { tag = "Lazer" });

        error.GetProperty("code").GetString().ShouldBe("ambiguous_or_not_found");
        error.GetProperty("field").GetString().ShouldBe("tag");
        error.GetProperty("options").EnumerateArray().Select(o => o.GetString()).ShouldBe(["Bruna"]);
    }

    // ---- search_entries ----

    [Fact]
    public async Task Search_entries_sem_filtros_devolve_tudo_com_total_e_contagem()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var body = await mcp.OkAsync("search_entries");

        body.GetProperty("totalCount").GetInt32().ShouldBe(11);
        body.GetProperty("items").GetArrayLength().ShouldBe(11);
        body.GetProperty("hasMore").GetBoolean().ShouldBeFalse();
        body.GetProperty("totalText").GetString()!.ShouldStartWith("R$ ");
    }

    [Fact]
    public async Task Search_entries_filtra_por_texto_tipo_carteira_tag_e_periodo()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var byText = await mcp.OkAsync("search_entries", new { text = "MERCADO" });
        var mercado = byText.GetProperty("items").EnumerateArray().Single();
        mercado.GetProperty("type").GetString().ShouldBe("expense");
        mercado.GetProperty("status").GetString().ShouldBe("planned");
        mercado.GetProperty("wallet").GetString().ShouldBe("Azul");
        mercado.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ShouldBe(["Lazer"]);
        Money(mercado, "amount", 32050, "R$ 320,50");

        var transfers = await mcp.OkAsync("search_entries", new { kinds = new[] { "transfer" } });
        var transfer = transfers.GetProperty("items").EnumerateArray().Single();
        transfer.GetProperty("fromWallet").GetString().ShouldBe("Azul");
        transfer.GetProperty("toWallet").GetString().ShouldBe("Caixa");

        var purchases = await mcp.OkAsync("search_entries", new { kinds = new[] { "card_purchase" }, tags = new[] { "Bruna" } });
        purchases.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("description").GetString()).ShouldBe(["Ifood", "Uber", "Estorno"], ignoreOrder: true);
        purchases.GetProperty("items").EnumerateArray().First().GetProperty("creditCard").GetString().ShouldBe("Verde");

        var period = await mcp.OkAsync("search_entries", new { from = "2026-03-10", to = "2026-03-12", wallets = new[] { "Azul" } });
        period.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("description").GetString()).ShouldBe(["Reserva", "Aluguel"], ignoreOrder: true);
    }

    [Fact]
    public async Task Search_entries_respeita_o_limite_de_50_e_informa_que_ha_mais()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var limited = await mcp.OkAsync("search_entries", new { limit = 3 });
        limited.GetProperty("items").GetArrayLength().ShouldBe(3);
        limited.GetProperty("totalCount").GetInt32().ShouldBe(11);
        limited.GetProperty("hasMore").GetBoolean().ShouldBeTrue();

        var tooMany = await mcp.ErrorAsync("search_entries", new { limit = 51 });
        tooMany.GetProperty("code").GetString().ShouldBe("limit.invalid");
        tooMany.GetProperty("field").GetString().ShouldBe("limit");
    }

    [Fact]
    public async Task Search_entries_valida_datas_tipos_e_nomes()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        (await mcp.ErrorAsync("search_entries", new { from = "20/03/2026" })).GetProperty("code").GetString().ShouldBe("date.invalid");
        (await mcp.ErrorAsync("search_entries", new { kinds = new[] { "pizza" } })).GetProperty("code").GetString().ShouldBe("kinds.invalid");
        var tag = await mcp.ErrorAsync("search_entries", new { tags = new[] { "Antiga" } });
        tag.GetProperty("code").GetString().ShouldBe("ambiguous_or_not_found");
        tag.GetProperty("options").EnumerateArray().Select(o => o.GetString()).ShouldBe(["Bruna", "Lazer"]);
    }
}
