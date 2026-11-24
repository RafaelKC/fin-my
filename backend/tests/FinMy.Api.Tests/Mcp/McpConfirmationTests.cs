#pragma warning disable CA1861 // arrays literais como argumentos dos testes

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Application.Tests.Infrastructure;

namespace FinMy.Api.Tests.Mcp;

/// <summary>
/// 008 T005 / US4 e SC-003: ferramentas com confirmação (<c>set_entry_status</c>, <c>pay_invoice</c>, <c>generate_settlement_charge</c>,
/// <c>delete_entry</c>). Sem <c>confirmed: true</c> nada muda e vem o <c>preview</c>; com ele, o mesmo efeito do app.
/// Espaço "Lia" (dados fictícios), "hoje" é 2026-03-20; a fatura Verde de 03/2026 tem total R$ 220,00 e R$ 50,00 pagos.
/// </summary>
public class McpConfirmationTests(PostgresFixture postgres) : McpTestBase(postgres)
{
    private static readonly string[] ConfirmationTools = ["set_entry_status", "pay_invoice", "generate_settlement_charge", "delete_entry"];

    private static void Money(JsonElement element, string name, long cents, string text)
    {
        element.GetProperty(name + "Cents").GetInt64().ShouldBe(cents);
        element.GetProperty(name + "Text").GetString().ShouldBe(text);
    }

    private static void ShouldBePreview(JsonElement body, string action)
    {
        body.GetProperty("requiresConfirmation").GetBoolean().ShouldBeTrue();
        body.GetProperty("confirmed").GetBoolean().ShouldBeFalse();
        body.GetProperty("action").GetString().ShouldBe(action);
    }

    private static async Task<JsonElement> GetAsync(HttpClient http, string url) => await http.GetFromJsonAsync<JsonElement>(url);

    private static Task<JsonElement> InvoiceAsync(LiaSpace space) => GetAsync(space.Http, $"/api/v1/credit-cards/{space.Verde}/invoices/2026-03");

    private static async Task<HttpStatusCode> StatusOfAsync(HttpClient http, string url) => (await http.GetAsync(url)).StatusCode;

    private static async Task<Guid> NewEntryAsync(LiaSpace space, string description = "Cinema antigo")
    {
        var response = await space.Http.PostAsJsonAsync("/api/v1/entries", new
        {
            kind = "expense",
            description,
            amount = "30,00",
            date = "2026-01-10",
            walletId = space.Azul,
            status = "settled",
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task<int> EntryCountAsync(McpSession mcp) =>
        (await mcp.OkAsync("search_entries", new { limit = 50 })).GetProperty("totalCount").GetInt32();

    // ---- Descoberta ----

    [Fact]
    public async Task As_ferramentas_de_confirmacao_tem_destructive_hint_e_o_parametro_confirmed()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var tools = (await mcp.Client.ListToolsAsync()).Where(t => ConfirmationTools.Contains(t.Name)).ToList();

        tools.Select(t => t.Name).ShouldBe(ConfirmationTools, ignoreOrder: true);
        foreach (var tool in tools)
        {
            tool.ProtocolTool.Annotations!.DestructiveHint.ShouldBe(true, tool.Name);
            tool.ProtocolTool.Annotations!.ReadOnlyHint.ShouldBe(false, tool.Name);
            tool.Description.ShouldNotBeNullOrWhiteSpace(tool.Name);
            tool.ProtocolTool.InputSchema.GetProperty("properties").TryGetProperty("confirmed", out _).ShouldBeTrue(tool.Name);
            tool.ProtocolTool.InputSchema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ShouldNotContain("confirmed");
        }
    }

    // ---- SC-003: nada é executado sem confirmação ----

    [Fact]
    public async Task Sem_confirmed_ou_com_confirmed_falso_nenhuma_ferramenta_altera_dados()
    {
        var (space, mcp) = await ConnectLiaAsync();
        await using var session = mcp;
        var entryId = await NewEntryAsync(space);
        var transfer = (await mcp.OkAsync("create_transfer", new { fromWallet = "Azul", toWallet = "Caixa", amount = "10,00" })).GetProperty("transfer").GetProperty("id").GetGuid();
        var invoice = await mcp.OkAsync("get_invoice", new { creditCard = "Verde", month = "2026-03", includePurchases = true });
        var purchaseId = invoice.GetProperty("purchases").EnumerateArray().First().GetProperty("id").GetGuid();
        var entriesBefore = await EntryCountAsync(mcp);
        var invoiceBefore = (await InvoiceAsync(space)).ToString();
        var pendingBefore = (await GetAsync(space.Http, $"/api/v1/entries/{space.PendingEntry}")).ToString();

        var calls = new (string Tool, object Args, string Action)[]
        {
            ("set_entry_status", new { id = space.PendingEntry.ToString(), status = "settled" }, "set_entry_status"),
            ("set_entry_status", new { id = space.PendingEntry.ToString(), status = "settled", confirmed = false }, "set_entry_status"),
            ("pay_invoice", new { creditCard = "Verde", month = "2026-03" }, "pay_invoice"),
            ("pay_invoice", new { creditCard = "Verde", month = "2026-03", amount = "10,00", confirmed = false }, "pay_invoice"),
            ("generate_settlement_charge", new { tag = "Bruna", month = "2026-03" }, "generate_settlement_charge"),
            ("generate_settlement_charge", new { tag = "Bruna", month = "2026-03", confirmed = false }, "generate_settlement_charge"),
            ("delete_entry", new { id = entryId.ToString(), type = "entry" }, "delete_entry"),
            ("delete_entry", new { id = purchaseId.ToString(), type = "card_purchase", confirmed = false }, "delete_entry"),
            ("delete_entry", new { id = transfer.ToString(), type = "transfer" }, "delete_entry"),
        };

        foreach (var (tool, args, action) in calls)
        {
            ShouldBePreview(await mcp.OkAsync(tool, args), action);
        }

        (await GetAsync(space.Http, $"/api/v1/entries/{space.PendingEntry}")).ToString().ShouldBe(pendingBefore);
        (await InvoiceAsync(space)).ToString().ShouldBe(invoiceBefore);
        (await EntryCountAsync(mcp)).ShouldBe(entriesBefore);
        (await StatusOfAsync(space.Http, $"/api/v1/entries/{entryId}")).ShouldBe(HttpStatusCode.OK);
        (await StatusOfAsync(space.Http, $"/api/v1/card-purchases/{purchaseId}")).ShouldBe(HttpStatusCode.OK);
        (await StatusOfAsync(space.Http, $"/api/v1/transfers/{transfer}")).ShouldBe(HttpStatusCode.OK);
    }

    // ---- US4.1: pagar fatura ----

    [Fact]
    public async Task Pagar_fatura_mostra_restante_carteira_e_data_e_so_registra_com_confirmacao()
    {
        var (space, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var preview = await mcp.OkAsync("pay_invoice", new { creditCard = "verde" });

        ShouldBePreview(preview, "pay_invoice");
        var details = preview.GetProperty("preview");
        details.GetProperty("creditCard").GetString().ShouldBe("Verde");
        details.GetProperty("dueMonth").GetString().ShouldBe("2026-03");
        details.GetProperty("wallet").GetString().ShouldBe("Azul");
        details.GetProperty("date").GetString().ShouldBe("2026-03-20");
        Money(details, "amount", 17000, "R$ 170,00");
        Money(details, "remainingAfter", 0, "R$ 0,00");
        (await InvoiceAsync(space)).GetProperty("remainingCents").GetInt64().ShouldBe(17000);

        var done = await mcp.OkAsync("pay_invoice", new { creditCard = "verde", confirmed = true });

        done.GetProperty("confirmed").GetBoolean().ShouldBeTrue();
        Money(done.GetProperty("invoice"), "remaining", 0, "R$ 0,00");
        done.GetProperty("invoice").GetProperty("status").GetString().ShouldBe("paid");
        (await InvoiceAsync(space)).GetProperty("payments").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task Pagamento_parcial_em_outra_carteira_e_data_e_valor_acima_do_restante_vira_erro()
    {
        var (space, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var over = await mcp.ErrorAsync("pay_invoice", new { creditCard = "Verde", month = "2026-03", amount = "171,00" });
        over.GetProperty("code").GetString().ShouldBe("invoice.payment_exceeds_remaining");

        var done = await mcp.OkAsync("pay_invoice", new
        {
            creditCard = "Verde",
            month = "2026-03",
            amount = "70,00",
            wallet = "Caixa",
            date = "2026-03-21",
            confirmed = true,
        });

        Money(done.GetProperty("payment"), "amount", 7000, "R$ 70,00");
        Money(done.GetProperty("invoice"), "remaining", 10000, "R$ 100,00");
        var payments = (await InvoiceAsync(space)).GetProperty("payments").EnumerateArray().ToList();
        payments.ShouldContain(p => p.GetProperty("walletId").GetGuid() == space.Caixa && p.GetProperty("date").GetString() == "2026-03-21");
    }

    [Fact]
    public async Task Pagar_fatura_sem_valor_em_aberto_e_cartao_inexistente_viram_erros_de_ferramenta()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        (await mcp.ErrorAsync("pay_invoice", new { creditCard = "Verde", month = "2026-05" })).GetProperty("code").GetString().ShouldBe("invoice.nothing_to_pay");

        var missing = await mcp.ErrorAsync("pay_invoice", new { creditCard = "Roxo" });
        missing.GetProperty("code").GetString().ShouldBe("ambiguous_or_not_found");
        missing.GetProperty("options").EnumerateArray().Select(o => o.GetString()).ShouldBe(["Verde"]);
    }

    // ---- US4.2: marcar como recebida/paga ----

    [Fact]
    public async Task Marcar_como_paga_mostra_a_mudanca_e_so_aplica_com_confirmacao()
    {
        var (space, mcp) = await ConnectLiaAsync();
        await using var session = mcp;
        var id = space.PendingEntry.ToString();

        var preview = await mcp.OkAsync("set_entry_status", new { id, status = "settled" });

        ShouldBePreview(preview, "set_entry_status");
        preview.GetProperty("preview").GetProperty("fromStatus").GetString().ShouldBe("planned");
        preview.GetProperty("preview").GetProperty("toStatus").GetString().ShouldBe("settled");
        preview.GetProperty("preview").GetProperty("entry").GetProperty("description").GetString().ShouldBe("Luz");
        (await GetAsync(space.Http, $"/api/v1/entries/{id}")).GetProperty("status").GetString().ShouldBe("planned");

        var done = await mcp.OkAsync("set_entry_status", new { id, status = "settled", confirmed = true });

        done.GetProperty("confirmed").GetBoolean().ShouldBeTrue();
        done.GetProperty("entry").GetProperty("status").GetString().ShouldBe("settled");
        (await GetAsync(space.Http, $"/api/v1/entries/{id}")).GetProperty("status").GetString().ShouldBe("settled");

        await mcp.OkAsync("set_entry_status", new { id, status = "planned", confirmed = true });
        (await GetAsync(space.Http, $"/api/v1/entries/{id}")).GetProperty("status").GetString().ShouldBe("planned");
    }

    [Fact]
    public async Task Status_invalido_e_lancamento_inexistente_viram_erros_de_ferramenta()
    {
        var (space, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        await mcp.ErrorAsync("set_entry_status", new { id = space.PendingEntry.ToString(), status = "talvez", confirmed = true });
        (await mcp.ErrorAsync("set_entry_status", new { id = Guid.NewGuid().ToString(), status = "settled" })).GetProperty("code").GetString().ShouldBe("not_found");
        (await mcp.ErrorAsync("set_entry_status", new { id = "abc", status = "settled" })).GetProperty("code").GetString().ShouldBe("id.invalid");
    }

    // ---- Cobrança de acerto (004 FR-010) ----

    [Fact]
    public async Task Gerar_cobranca_mostra_os_valores_do_acerto_e_cria_a_entrada_prevista_so_com_confirmacao()
    {
        var (space, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var preview = await mcp.OkAsync("generate_settlement_charge", new { tag = "bruna" });

        ShouldBePreview(preview, "generate_settlement_charge");
        var details = preview.GetProperty("preview");
        details.GetProperty("description").GetString().ShouldBe("Bruna — acerto 03/2026");
        details.GetProperty("kind").GetString().ShouldBe("income");
        details.GetProperty("status").GetString().ShouldBe("planned");
        details.GetProperty("wallet").GetString().ShouldBe("Azul");
        details.GetProperty("date").GetString().ShouldBe("2026-03-20");
        details.GetProperty("referenceMonth").GetString().ShouldBe("2026-03");
        Money(details, "amount", 17000, "R$ 170,00");
        var before = await mcp.OkAsync("get_settlements", new { month = "2026-03", tag = "Bruna" });

        var done = await mcp.OkAsync("generate_settlement_charge", new { tag = "Bruna", confirmed = true });

        var entry = done.GetProperty("entry");
        entry.GetProperty("settlementTag").GetString().ShouldBe("Bruna");
        entry.GetProperty("status").GetString().ShouldBe("planned");
        Money(entry, "amount", 17000, "R$ 170,00");
        var rest = await GetAsync(space.Http, $"/api/v1/entries/{entry.GetProperty("id").GetGuid()}");
        rest.GetProperty("kind").GetString().ShouldBe("income");
        rest.GetProperty("description").GetString().ShouldBe("Bruna — acerto 03/2026");

        var after = await mcp.OkAsync("get_settlements", new { month = "2026-03", tag = "Bruna" });
        after.ToString().ShouldNotBe(before.ToString());
        (await mcp.ErrorAsync("generate_settlement_charge", new { tag = "Bruna" })).GetProperty("code").GetString().ShouldBe("settlement.nothing_to_charge");
    }

    [Fact]
    public async Task Cobranca_de_outro_mes_usa_o_dia_1_e_valor_e_carteira_informados()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var body = await mcp.OkAsync("generate_settlement_charge", new { tag = "Bruna", month = "2026-02", amount = "25,50", wallet = "Caixa" });

        var details = body.GetProperty("preview");
        details.GetProperty("date").GetString().ShouldBe("2026-02-01");
        details.GetProperty("description").GetString().ShouldBe("Bruna — acerto 02/2026");
        details.GetProperty("wallet").GetString().ShouldBe("Caixa");
        Money(details, "amount", 2550, "R$ 25,50");

        (await mcp.ErrorAsync("generate_settlement_charge", new { tag = "Bruna", month = "2026-02" })).GetProperty("code").GetString().ShouldBe("settlement.nothing_to_charge");
        (await mcp.ErrorAsync("generate_settlement_charge", new { tag = "Lazer" })).GetProperty("code").GetString().ShouldBe("ambiguous_or_not_found");
    }

    // ---- US4.3: excluir ----

    [Fact]
    public async Task Excluir_lancamento_mostra_o_item_e_so_exclui_com_confirmacao()
    {
        var (space, mcp) = await ConnectLiaAsync();
        await using var session = mcp;
        var id = await NewEntryAsync(space);

        var preview = await mcp.OkAsync("delete_entry", new { id = id.ToString(), type = "entry" });

        ShouldBePreview(preview, "delete_entry");
        preview.GetProperty("preview").GetProperty("description").GetString().ShouldBe("Cinema antigo");
        preview.GetProperty("preview").GetProperty("willDelete").GetInt32().ShouldBe(1);
        Money(preview.GetProperty("preview"), "amount", 3000, "R$ 30,00");
        (await StatusOfAsync(space.Http, $"/api/v1/entries/{id}")).ShouldBe(HttpStatusCode.OK);

        var done = await mcp.OkAsync("delete_entry", new { id = id.ToString(), type = "entry", confirmed = true });

        done.GetProperty("deleted").GetInt32().ShouldBe(1);
        (await StatusOfAsync(space.Http, $"/api/v1/entries/{id}")).ShouldBe(HttpStatusCode.NotFound);

        var restore = await space.Http.PostAsync($"/api/v1/deletions/{done.GetProperty("deletionId").GetGuid()}/restore", null);
        restore.IsSuccessStatusCode.ShouldBeTrue();
        (await StatusOfAsync(space.Http, $"/api/v1/entries/{id}")).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Excluir_transferencia_exige_confirmacao()
    {
        var (space, mcp) = await ConnectLiaAsync();
        await using var session = mcp;
        var id = (await mcp.OkAsync("create_transfer", new { fromWallet = "Azul", toWallet = "Caixa", amount = "10,00" })).GetProperty("transfer").GetProperty("id").GetString()!;

        var preview = await mcp.OkAsync("delete_entry", new { id, type = "transfer" });
        preview.GetProperty("preview").GetProperty("fromWallet").GetString().ShouldBe("Azul");
        preview.GetProperty("preview").GetProperty("toWallet").GetString().ShouldBe("Caixa");

        (await mcp.OkAsync("delete_entry", new { id, type = "transfer", confirmed = true })).GetProperty("deleted").GetInt32().ShouldBe(1);
        (await StatusOfAsync(space.Http, $"/api/v1/transfers/{id}")).ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Excluir_parcelas_conta_o_que_o_escopo_levaria_e_exclui_so_isso()
    {
        var (space, mcp) = await ConnectLiaAsync();
        await using var session = mcp;
        var created = await mcp.OkAsync("create_card_purchase", new
        {
            description = "Geladeira",
            amount = "400,00",
            creditCard = "Verde",
            installments = new { total = 4, mode = "total_amount" },
        });
        var parcels = created.GetProperty("purchases").EnumerateArray().Select(p => p.GetProperty("id").GetString()!).ToList();
        parcels.Count.ShouldBe(4);

        (await mcp.OkAsync("delete_entry", new { id = parcels[1], type = "card_purchase" })).GetProperty("preview").GetProperty("willDelete").GetInt32().ShouldBe(1);
        (await mcp.OkAsync("delete_entry", new { id = parcels[1], type = "card_purchase", scope = "this_and_following" })).GetProperty("preview").GetProperty("willDelete").GetInt32().ShouldBe(3);
        var all = await mcp.OkAsync("delete_entry", new { id = parcels[1], type = "card_purchase", scope = "all" });
        all.GetProperty("preview").GetProperty("willDelete").GetInt32().ShouldBe(4);
        all.GetProperty("preview").GetProperty("installment").GetString().ShouldBe("2/4");

        (await mcp.OkAsync("delete_entry", new { id = parcels[1], type = "card_purchase", scope = "this_and_following", confirmed = true })).GetProperty("deleted").GetInt32().ShouldBe(3);
        (await StatusOfAsync(space.Http, $"/api/v1/card-purchases/{parcels[0]}")).ShouldBe(HttpStatusCode.OK);
        (await StatusOfAsync(space.Http, $"/api/v1/card-purchases/{parcels[2]}")).ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Excluir_com_tipo_ou_escopo_invalido_ou_item_inexistente_vira_erro_de_ferramenta()
    {
        var (space, mcp) = await ConnectLiaAsync();
        await using var session = mcp;
        var id = space.PendingEntry.ToString();

        (await mcp.ErrorAsync("delete_entry", new { id, type = "wallet", confirmed = true })).GetProperty("code").GetString().ShouldBe("type.invalid");
        (await mcp.ErrorAsync("delete_entry", new { id = Guid.NewGuid().ToString(), type = "entry" })).GetProperty("code").GetString().ShouldBe("not_found");
        (await mcp.ErrorAsync("delete_entry", new { id, type = "card_purchase" })).GetProperty("code").GetString().ShouldBe("not_found");
        (await StatusOfAsync(space.Http, $"/api/v1/entries/{id}")).ShouldBe(HttpStatusCode.OK);
    }
}
