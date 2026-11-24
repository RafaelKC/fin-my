#pragma warning disable CA1861 // arrays literais como argumentos dos testes

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Api.Tests.Cards;
using FinMy.Api.Tests.OAuth;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinMy.Api.Tests.Mcp;

/// <summary>
/// 008 T004 / US2: contrato das ferramentas de lançamento (<c>create_entry</c>, <c>create_card_purchase</c>, <c>create_transfer</c>,
/// <c>undo_creation</c>) — padrões da FR-005, tags e divisão, fatura na resposta, <c>requestId</c>, desfazer e o erro
/// <c>ambiguous_or_not_found</c>. Espaço "Lia" (dados fictícios); "hoje" é 2026-03-20 (a fatura de hoje fecha dia 18, vence dia 25).
/// </summary>
public class McpCreationToolsTests(PostgresFixture postgres) : McpTestBase(postgres)
{
    private static readonly string[] CreationTools = ["create_entry", "create_card_purchase", "create_transfer", "undo_creation"];

    private static void Money(JsonElement element, string name, long cents, string text)
    {
        element.GetProperty(name + "Cents").GetInt64().ShouldBe(cents);
        element.GetProperty(name + "Text").GetString().ShouldBe(text);
    }

    private static List<JsonElement> Items(JsonElement body, string name) => body.GetProperty(name).EnumerateArray().ToList();

    private static string[] Strings(JsonElement array) => array.EnumerateArray().Select(e => e.GetString()!).ToArray();

    private async Task<string> ViaAsync(string table, Guid id)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMyDbContext>();
        var rows = table switch
        {
            "entries" => db.Database.SqlQuery<string>($"select created_via as \"Value\" from entries where id = {id}"),
            "transfers" => db.Database.SqlQuery<string>($"select created_via as \"Value\" from transfers where id = {id}"),
            _ => throw new ArgumentOutOfRangeException(nameof(table)),
        };
        return (await rows.ToListAsync()).Single();
    }

    private async Task AgeEntryAsync(Guid id, int minutes)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMyDbContext>();
        await db.Database.ExecuteSqlAsync($"update entries set created_at = now() - make_interval(mins => {minutes}) where id = {id}");
    }

    private static async Task<HttpStatusCode> StatusOfAsync(HttpClient http, string url) => (await http.GetAsync(url)).StatusCode;

    // ---- Descoberta ----

    [Fact]
    public async Task Lista_as_ferramentas_de_criacao_sem_destructive_hint_e_com_descricoes_em_portugues()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var tools = (await mcp.Client.ListToolsAsync()).Where(t => CreationTools.Contains(t.Name)).ToList();

        tools.Select(t => t.Name).ShouldBe(CreationTools, ignoreOrder: true);
        foreach (var tool in tools)
        {
            tool.ProtocolTool.Annotations!.ReadOnlyHint.ShouldBe(false, tool.Name);
            tool.ProtocolTool.Annotations!.DestructiveHint.ShouldBe(false, tool.Name);
            tool.Description.ShouldNotBeNullOrWhiteSpace(tool.Name);
        }

        var entry = tools.Single(t => t.Name == "create_entry").ProtocolTool.InputSchema;
        entry.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ShouldBe(["kind", "description", "amount"], ignoreOrder: true);
        entry.GetProperty("properties").TryGetProperty("requestId", out _).ShouldBeTrue();
    }

    // ---- US2.3: padrões (FR-005) na entrada ----

    [Fact]
    public async Task Entrada_sem_carteira_usa_a_principal_e_diz_o_que_usou()
    {
        var (space, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var body = await mcp.OkAsync("create_entry", new { kind = "income", description = "Pix da Carla", amount = "150,00" });

        var entry = Items(body, "entries").Single();
        entry.GetProperty("kind").GetString().ShouldBe("income");
        entry.GetProperty("date").GetString().ShouldBe("2026-03-20");
        entry.GetProperty("referenceMonth").GetString().ShouldBe("2026-03");
        entry.GetProperty("status").GetString().ShouldBe("settled");
        entry.GetProperty("wallet").GetString().ShouldBe("Azul");
        Money(entry, "amount", 15000, "R$ 150,00");
        body.GetProperty("today").GetString().ShouldBe("2026-03-20");
        body.GetProperty("defaultsUsed").EnumerateArray()
            .Select(d => (d.GetProperty("field").GetString(), d.GetProperty("source").GetString()))
            .ShouldBe([("date", "today"), ("wallet", "primary_wallet"), ("status", "default_rule")], ignoreOrder: true);

        var rest = await space.Http.GetFromJsonAsync<JsonElement>($"/api/v1/entries/{entry.GetProperty("id").GetGuid()}");
        rest.GetProperty("amountCents").GetInt64().ShouldBe(15000);
        rest.GetProperty("walletId").GetGuid().ShouldBe(space.Azul);
        rest.GetProperty("status").GetString().ShouldBe("settled");
    }

    [Fact]
    public async Task Carteira_padrao_e_a_do_lancamento_mais_recente_com_a_mesma_descricao_sem_diferenciar_acentos()
    {
        var (space, mcp) = await ConnectLiaAsync();
        await using var session = mcp;
        (await space.Http.PostAsJsonAsync("/api/v1/entries", new
        {
            kind = "expense",
            description = "Padaria Pão Quente",
            amount = "12,00",
            date = "2026-03-10",
            walletId = space.Caixa,
            status = "settled",
        })).EnsureSuccessStatusCode();

        var body = await mcp.OkAsync("create_entry", new { kind = "expense", description = "PADARIA PAO QUENTE", amount = "9,50" });

        Items(body, "entries").Single().GetProperty("wallet").GetString().ShouldBe("Caixa");
        body.GetProperty("defaultsUsed").EnumerateArray()
            .Single(d => d.GetProperty("field").GetString() == "wallet").GetProperty("source").GetString().ShouldBe("same_description");
    }

    [Fact]
    public async Task Informar_carteira_data_situacao_e_mes_nao_gera_padroes_e_o_canal_fica_como_claude()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var body = await mcp.OkAsync("create_entry", new
        {
            kind = "expense",
            description = "Luz de abril",
            amount = "130,10",
            date = "2026-04-05",
            wallet = "caixa",
            status = "planned",
            referenceMonth = "2026-04",
            notes = "Conta fictícia",
        });

        var entry = Items(body, "entries").Single();
        entry.GetProperty("wallet").GetString().ShouldBe("Caixa");
        entry.GetProperty("status").GetString().ShouldBe("planned");
        entry.GetProperty("referenceMonth").GetString().ShouldBe("2026-04");
        entry.GetProperty("notes").GetString().ShouldBe("Conta fictícia");
        body.GetProperty("defaultsUsed").GetArrayLength().ShouldBe(0);
        (await ViaAsync("entries", entry.GetProperty("id").GetGuid())).ShouldBe("claude");
    }

    // ---- Tags e divisão (004) ----

    [Fact]
    public async Task Tags_existentes_sao_usadas_e_as_novas_sao_criadas_como_comuns()
    {
        var (space, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var body = await mcp.OkAsync("create_entry", new { kind = "expense", description = "Cinema do Léo", amount = "40,00", tags = new[] { "lazer", "Viagem" } });

        var entry = Items(body, "entries").Single();
        Strings(entry.GetProperty("tags")).ShouldBe(["Lazer", "Viagem"], ignoreOrder: true);
        entry.GetProperty("settlementTag").ValueKind.ShouldBe(JsonValueKind.Null);

        var catalog = await mcp.OkAsync("list_catalog");
        var viagem = Items(catalog, "tags").Single(t => t.GetProperty("name").GetString() == "Viagem");
        viagem.GetProperty("isSettlement").GetBoolean().ShouldBeFalse();
        Items(catalog, "tags").Count(t => t.GetProperty("name").GetString() == "Lazer").ShouldBe(1);
        (await space.Http.GetFromJsonAsync<JsonElement>("/api/v1/tags")).EnumerateArray()
            .Select(t => t.GetProperty("name").GetString()).ShouldContain("Viagem");
    }

    [Fact]
    public async Task Tag_de_acerto_em_tags_vai_para_a_coluna_de_acerto()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var body = await mcp.OkAsync("create_entry", new { kind = "expense", description = "Presente da Bruna", amount = "55,00", tags = new[] { "Bruna" } });

        Items(body, "entries").Single().GetProperty("settlementTag").GetString().ShouldBe("Bruna");
    }

    [Fact]
    public async Task Saida_dividida_cria_uma_saida_por_parte_com_o_mesmo_grupo()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var body = await mcp.OkAsync("create_entry", new
        {
            kind = "expense",
            description = "Jantar",
            amount = "100,00",
            split = new { mode = "equal", parts = new object[] { new { tag = "Bruna" }, new { tag = (string?)null } } },
        });

        var entries = Items(body, "entries");
        entries.Count.ShouldBe(2);
        entries.ForEach(e => Money(e, "amount", 5000, "R$ 50,00"));
        entries.Select(e => e.GetProperty("settlementTag").GetString()).ShouldBe(["Bruna", null], ignoreOrder: true);
        entries.Select(e => e.GetProperty("splitGroupId").GetGuid()).Distinct().Count().ShouldBe(1);
    }

    [Fact]
    public async Task Divisao_so_vale_para_saida_e_o_erro_da_regra_chega_como_erro_de_ferramenta()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var error = await mcp.ErrorAsync("create_entry", new
        {
            kind = "income",
            description = "Rateio",
            amount = "100,00",
            split = new { mode = "equal", parts = new object[] { new { tag = "Bruna" }, new { tag = (string?)null } } },
        });

        error.GetProperty("code").GetString().ShouldNotBeNullOrWhiteSpace();
        error.GetProperty("message").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    // ---- US2.1: compra dividida e fatura na resposta ----

    [Fact]
    public async Task Compra_dividida_com_a_Bruna_cria_duas_compras_de_16_45_e_informa_a_fatura()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var body = await mcp.OkAsync("create_card_purchase", new
        {
            description = "Ifood",
            amount = "32,90",
            creditCard = "verde",
            split = new { mode = "equal", parts = new object[] { new { tag = (string?)null }, new { tag = "Bruna" } } },
        });

        var purchases = Items(body, "purchases");
        purchases.Count.ShouldBe(2);
        foreach (var p in purchases)
        {
            Money(p, "amount", 1645, "R$ 16,45");
            p.GetProperty("creditCard").GetString().ShouldBe("Verde");
            p.GetProperty("date").GetString().ShouldBe("2026-03-20");
            p.GetProperty("invoice").GetProperty("dueMonth").GetString().ShouldBe("2026-04");
            p.GetProperty("invoice").GetProperty("dueDate").GetString().ShouldBe("2026-04-25");
            p.GetProperty("invoice").GetProperty("closingDate").GetString().ShouldBe("2026-04-18");
        }

        purchases.Select(p => p.GetProperty("settlementTag").GetString()).ShouldBe([null, "Bruna"], ignoreOrder: true);
        body.GetProperty("warnings").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Cartao_padrao_e_o_da_compra_mais_recente_com_a_mesma_descricao()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var body = await mcp.OkAsync("create_card_purchase", new { description = "ifood", amount = "20,00" });

        Items(body, "purchases").Single().GetProperty("creditCard").GetString().ShouldBe("Verde");
        var used = Items(body, "defaultsUsed").Single(d => d.GetProperty("field").GetString() == "creditCard");
        used.GetProperty("source").GetString().ShouldBe("same_description");
    }

    // ---- US2.2: parcelamento ----

    [Fact]
    public async Task Parcelamento_em_6x_cria_as_parcelas_e_informa_a_primeira_fatura()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var body = await mcp.OkAsync("create_card_purchase", new
        {
            description = "Tênis",
            amount = "293,70",
            installments = new { total = 6, mode = "total_amount" },
        });

        var purchases = Items(body, "purchases");
        purchases.Count.ShouldBe(6);
        purchases.Select(p => p.GetProperty("installment").GetString()).ShouldBe(["1/6", "2/6", "3/6", "4/6", "5/6", "6/6"]);
        purchases.ForEach(p => Money(p, "amount", 4895, "R$ 48,95"));
        purchases.Select(p => p.GetProperty("invoice").GetProperty("dueMonth").GetString())
            .ShouldBe(["2026-04", "2026-05", "2026-06", "2026-07", "2026-08", "2026-09"]);
        purchases[0].GetProperty("invoice").GetProperty("dueDate").GetString().ShouldBe("2026-04-25");

        // Sem cartão informado e sem compra com esta descrição: o cartão mais usado nos últimos 30 dias.
        purchases[0].GetProperty("creditCard").GetString().ShouldBe("Verde");
        Items(body, "defaultsUsed").Single(d => d.GetProperty("field").GetString() == "creditCard")
            .GetProperty("source").GetString().ShouldBe("most_used_card_30d");
    }

    [Fact]
    public async Task Valor_negativo_vira_estorno_com_aviso()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var body = await mcp.OkAsync("create_card_purchase", new { description = "Estorno do tênis", amount = "-20,00", creditCard = "Verde" });

        var purchase = Items(body, "purchases").Single();
        purchase.GetProperty("nature").GetString().ShouldBe("credit");
        Money(purchase, "amount", -2000, "-R$ 20,00");
        Items(body, "warnings").Single().GetProperty("code").GetString().ShouldBe("recorded_as_credit");
    }

    [Fact]
    public async Task Sem_compras_recentes_nem_cartao_informado_pede_o_cartao_com_as_opcoes()
    {
        var (http, account) = await CardTestSupport.NewSpaceAsync(Factory, "Léo");
        var azul = await CardTestSupport.CreateWalletAsync(http, "Azul");
        await CardTestSupport.CreateCardAsync(http, "Verde", azul.Id);
        await CardTestSupport.CreateCardAsync(http, "Roxo", azul.Id);
        var (_, tokens) = await new OAuthFlow(Factory).ConnectAsync(account.AccessToken);
        await using var mcp = await McpSession.ConnectAsync(Factory, tokens.AccessToken);

        var error = await mcp.ErrorAsync("create_card_purchase", new { description = "Livro", amount = "49,90" });

        error.GetProperty("code").GetString().ShouldBe("card_required");
        error.GetProperty("field").GetString().ShouldBe("creditCard");
        Strings(error.GetProperty("options")).ShouldBe(["Roxo", "Verde"]);
        (await mcp.OkAsync("search_entries")).GetProperty("totalCount").GetInt32().ShouldBe(0);
    }

    // ---- US2.4: ambiguous_or_not_found ----

    [Fact]
    public async Task Carteira_inexistente_ou_arquivada_nao_cria_nada_e_lista_as_opcoes_ativas()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;
        var before = (await mcp.OkAsync("search_entries", new { text = "Fantasma" })).GetProperty("totalCount").GetInt32();

        foreach (var name in new[] { "Roxa", "Velha" })
        {
            var error = await mcp.ErrorAsync("create_entry", new { kind = "expense", description = "Fantasma", amount = "10,00", wallet = name });

            error.GetProperty("code").GetString().ShouldBe("ambiguous_or_not_found");
            error.GetProperty("field").GetString().ShouldBe("wallet");
            Strings(error.GetProperty("options")).ShouldBe(["Azul", "Caixa"]);
            error.GetProperty("message").GetString()!.ShouldContain(name);
        }

        (await mcp.OkAsync("search_entries", new { text = "Fantasma" })).GetProperty("totalCount").GetInt32().ShouldBe(before);
    }

    [Fact]
    public async Task Cartao_inexistente_nao_cria_compra_e_lista_os_cartoes()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var error = await mcp.ErrorAsync("create_card_purchase", new { description = "Fantasma", amount = "10,00", creditCard = "Cartão do banco" });

        error.GetProperty("code").GetString().ShouldBe("ambiguous_or_not_found");
        error.GetProperty("field").GetString().ShouldBe("creditCard");
        Strings(error.GetProperty("options")).ShouldBe(["Verde"]);
        (await mcp.OkAsync("search_entries", new { text = "Fantasma" })).GetProperty("totalCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Parte_da_divisao_com_tag_que_nao_e_de_acerto_ou_nao_existe_e_recusada_sem_criar_tag()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        foreach (var tag in new[] { "Lazer", "Duda" })
        {
            var error = await mcp.ErrorAsync("create_entry", new
            {
                kind = "expense",
                description = "Fantasma",
                amount = "100,00",
                split = new { mode = "equal", parts = new object[] { new { tag }, new { tag = (string?)null } } },
            });

            error.GetProperty("code").GetString().ShouldBe("ambiguous_or_not_found");
            Strings(error.GetProperty("options")).ShouldBe(["Bruna"]);
        }

        Items(await mcp.OkAsync("list_catalog"), "tags").Select(t => t.GetProperty("name").GetString()).ShouldNotContain("Duda");
    }

    [Fact]
    public async Task Tag_arquivada_nao_e_recriada_nem_reativada()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var error = await mcp.ErrorAsync("create_entry", new { kind = "expense", description = "Fantasma", amount = "10,00", tags = new[] { "Antiga" } });

        error.GetProperty("code").GetString().ShouldBe("ambiguous_or_not_found");
        error.GetProperty("field").GetString().ShouldBe("tags");
        Strings(error.GetProperty("options")).ShouldBe(["Bruna", "Lazer"]);
    }

    // ---- Transferência ----

    [Fact]
    public async Task Transferencia_usa_data_de_hoje_e_descricao_padrao()
    {
        var (space, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var body = await mcp.OkAsync("create_transfer", new { fromWallet = "Azul", toWallet = "caixa", amount = "75,50" });

        var transfer = body.GetProperty("transfer");
        transfer.GetProperty("fromWallet").GetString().ShouldBe("Azul");
        transfer.GetProperty("toWallet").GetString().ShouldBe("Caixa");
        transfer.GetProperty("date").GetString().ShouldBe("2026-03-20");
        transfer.GetProperty("description").GetString().ShouldBe("Transferência");
        Money(transfer, "amount", 7550, "R$ 75,50");
        body.GetProperty("defaultsUsed").EnumerateArray().Single().GetProperty("field").GetString().ShouldBe("date");

        var rest = await space.Http.GetFromJsonAsync<JsonElement>($"/api/v1/transfers/{transfer.GetProperty("id").GetGuid()}");
        rest.GetProperty("amountCents").GetInt64().ShouldBe(7550);
        (await ViaAsync("transfers", transfer.GetProperty("id").GetGuid())).ShouldBe("claude");
    }

    [Fact]
    public async Task Transferencia_com_carteira_desconhecida_ou_a_mesma_carteira_e_recusada()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var unknown = await mcp.ErrorAsync("create_transfer", new { fromWallet = "Azul", toWallet = "Roxa", amount = "10,00" });
        unknown.GetProperty("code").GetString().ShouldBe("ambiguous_or_not_found");
        unknown.GetProperty("field").GetString().ShouldBe("toWallet");

        var same = await mcp.ErrorAsync("create_transfer", new { fromWallet = "Azul", toWallet = "Azul", amount = "10,00" });
        same.GetProperty("code").GetString().ShouldNotBe("ambiguous_or_not_found");
    }

    // ---- requestId (idempotência) ----

    [Fact]
    public async Task Mesmo_requestId_com_os_mesmos_dados_nao_cria_de_novo_e_devolve_a_mesma_resposta()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;
        var args = new { kind = "expense", description = "Almoço do Léo", amount = "35,00", requestId = Guid.NewGuid().ToString() };

        var first = await mcp.OkAsync("create_entry", args);
        var second = await mcp.OkAsync("create_entry", args);

        first.TryGetProperty("replayed", out _).ShouldBeFalse();
        second.GetProperty("replayed").GetBoolean().ShouldBeTrue();
        Items(second, "entries").Single().GetProperty("id").GetGuid().ShouldBe(Items(first, "entries").Single().GetProperty("id").GetGuid());
        (await mcp.OkAsync("search_entries", new { text = "Almoço do Léo" })).GetProperty("totalCount").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Mesmo_requestId_com_dados_diferentes_e_requestId_invalido_sao_recusados()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;
        var key = Guid.NewGuid().ToString();
        await mcp.OkAsync("create_card_purchase", new { description = "Livro", amount = "49,90", creditCard = "Verde", requestId = key });

        var reused = await mcp.ErrorAsync("create_card_purchase", new { description = "Livro", amount = "59,90", creditCard = "Verde", requestId = key });
        var invalid = await mcp.ErrorAsync("create_transfer", new { fromWallet = "Azul", toWallet = "Caixa", amount = "1,00", requestId = "abc" });

        reused.GetProperty("code").GetString().ShouldBe("idempotency.key_reused");
        invalid.GetProperty("code").GetString().ShouldBe("idempotency.invalid_key");
        (await mcp.OkAsync("search_entries", new { text = "Livro" })).GetProperty("totalCount").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Falha_de_regra_libera_o_requestId_para_tentar_de_novo()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;
        var key = Guid.NewGuid().ToString();

        await mcp.ErrorAsync("create_entry", new { kind = "expense", description = "Mesa", amount = "80,00", wallet = "Roxa", requestId = key });
        var ok = await mcp.OkAsync("create_entry", new { kind = "expense", description = "Mesa", amount = "80,00", wallet = "Azul", requestId = key });

        Items(ok, "entries").Single().GetProperty("wallet").GetString().ShouldBe("Azul");
    }

    // ---- US2.5: desfazer ----

    [Fact]
    public async Task Desfaz_o_lancamento_criado_pelo_claude()
    {
        var (space, mcp) = await ConnectLiaAsync();
        await using var session = mcp;
        var id = Items(await mcp.OkAsync("create_entry", new { kind = "expense", description = "Engano", amount = "10,00" }), "entries").Single().GetProperty("id").GetGuid();

        var body = await mcp.OkAsync("undo_creation", new { id = id.ToString() });

        body.GetProperty("undone").GetInt32().ShouldBe(1);
        (await StatusOfAsync(space.Http, $"/api/v1/entries/{id}")).ShouldBe(HttpStatusCode.NotFound);
        (await mcp.OkAsync("search_entries", new { text = "Engano" })).GetProperty("totalCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Desfazer_a_divisao_exclui_todas_as_partes()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;
        var created = await mcp.OkAsync("create_entry", new
        {
            kind = "expense",
            description = "Engano dividido",
            amount = "100,00",
            split = new { mode = "equal", parts = new object[] { new { tag = "Bruna" }, new { tag = (string?)null } } },
        });

        var body = await mcp.OkAsync("undo_creation", new { id = Items(created, "entries")[0].GetProperty("id").GetString() });

        body.GetProperty("undone").GetInt32().ShouldBe(2);
        (await mcp.OkAsync("search_entries", new { text = "Engano dividido" })).GetProperty("totalCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Desfazer_uma_parcela_exclui_o_parcelamento_inteiro()
    {
        var (space, mcp) = await ConnectLiaAsync();
        await using var session = mcp;
        var created = await mcp.OkAsync("create_card_purchase", new
        {
            description = "Engano parcelado",
            amount = "120,00",
            creditCard = "Verde",
            installments = new { total = 4, mode = "total_amount" },
        });

        var body = await mcp.OkAsync("undo_creation", new { id = Items(created, "purchases")[2].GetProperty("id").GetString() });

        body.GetProperty("undone").GetInt32().ShouldBe(4);
        foreach (var p in Items(created, "purchases"))
        {
            (await StatusOfAsync(space.Http, $"/api/v1/card-purchases/{p.GetProperty("id").GetGuid()}")).ShouldBe(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task Desfazer_compra_dividida_e_transferencia()
    {
        var (space, mcp) = await ConnectLiaAsync();
        await using var session = mcp;
        var split = await mcp.OkAsync("create_card_purchase", new
        {
            description = "Engano de cartão",
            amount = "32,90",
            creditCard = "Verde",
            split = new { mode = "equal", parts = new object[] { new { tag = (string?)null }, new { tag = "Bruna" } } },
        });
        var transfer = await mcp.OkAsync("create_transfer", new { fromWallet = "Azul", toWallet = "Caixa", amount = "5,00" });

        (await mcp.OkAsync("undo_creation", new { id = Items(split, "purchases")[1].GetProperty("id").GetString() })).GetProperty("undone").GetInt32().ShouldBe(2);
        (await mcp.OkAsync("undo_creation", new { id = transfer.GetProperty("transfer").GetProperty("id").GetString() })).GetProperty("undone").GetInt32().ShouldBe(1);
        (await StatusOfAsync(space.Http, $"/api/v1/transfers/{transfer.GetProperty("transfer").GetProperty("id").GetGuid()}")).ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Desfazer_de_novo_ou_um_id_que_nao_existe_devolve_nao_encontrado()
    {
        var (_, mcp) = await ConnectLiaAsync();
        await using var session = mcp;
        var id = Items(await mcp.OkAsync("create_entry", new { kind = "expense", description = "Engano", amount = "10,00" }), "entries").Single().GetProperty("id").GetString();
        await mcp.OkAsync("undo_creation", new { id });

        (await mcp.ErrorAsync("undo_creation", new { id })).GetProperty("code").GetString().ShouldBe("not_found");
        (await mcp.ErrorAsync("undo_creation", new { id = Guid.NewGuid().ToString() })).GetProperty("code").GetString().ShouldBe("not_found");
        (await mcp.ErrorAsync("undo_creation", new { id = "não-é-uuid" })).GetProperty("field").GetString().ShouldBe("id");
    }

    [Fact]
    public async Task Nao_desfaz_o_que_o_app_criou()
    {
        var (space, mcp) = await ConnectLiaAsync();
        await using var session = mcp;

        var error = await mcp.ErrorAsync("undo_creation", new { id = space.PendingEntry.ToString() });

        error.GetProperty("code").GetString().ShouldBe("undo_not_allowed");
        (await StatusOfAsync(space.Http, $"/api/v1/entries/{space.PendingEntry}")).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Nao_desfaz_depois_de_10_minutos()
    {
        var (space, mcp) = await ConnectLiaAsync();
        await using var session = mcp;
        var id = Items(await mcp.OkAsync("create_entry", new { kind = "expense", description = "Antigo", amount = "10,00" }), "entries").Single().GetProperty("id").GetGuid();

        await AgeEntryAsync(id, 9);
        (await mcp.CallAsync("undo_creation", new { id = id.ToString() })).IsError.ShouldBeFalse();

        var older = Items(await mcp.OkAsync("create_entry", new { kind = "expense", description = "Mais antigo", amount = "10,00" }), "entries").Single().GetProperty("id").GetGuid();
        await AgeEntryAsync(older, 11);
        (await mcp.ErrorAsync("undo_creation", new { id = older.ToString() })).GetProperty("code").GetString().ShouldBe("undo_not_allowed");
        (await StatusOfAsync(space.Http, $"/api/v1/entries/{older}")).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Nao_desfaz_o_que_foi_criado_por_outro_usuario_nem_de_outro_espaco()
    {
        var (_, lia) = await ConnectLiaAsync();
        await using var liaSession = lia;
        var id = Items(await lia.OkAsync("create_entry", new { kind = "expense", description = "Da Lia", amount = "10,00" }), "entries").Single().GetProperty("id").GetString();

        var (otherHttp, otherAccount) = await CardTestSupport.NewSpaceAsync(Factory, "Léo");
        var (_, tokens) = await new OAuthFlow(Factory).ConnectAsync(otherAccount.AccessToken);
        await using var leo = await McpSession.ConnectAsync(Factory, tokens.AccessToken);

        (await leo.ErrorAsync("undo_creation", new { id })).GetProperty("code").GetString().ShouldBe("not_found");
        (await lia.OkAsync("search_entries", new { text = "Da Lia" })).GetProperty("totalCount").GetInt32().ShouldBe(1);
        otherHttp.Dispose();
    }
}
