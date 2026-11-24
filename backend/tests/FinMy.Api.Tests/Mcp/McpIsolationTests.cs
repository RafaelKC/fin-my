#pragma warning disable CA1861 // arrays literais como argumentos dos testes

using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Api.Tests.Cards;
using FinMy.Api.Tests.OAuth;
using FinMy.Application.Tests.Infrastructure;

namespace FinMy.Api.Tests.Mcp;

/// <summary>
/// 008 T008 / constituição §I: o token do Claude carrega o Espaço da pessoa (<c>tid</c>), então nada de outro Espaço aparece nem muda
/// pelo MCP. Espaço A = "Lia" (dados fictícios); B = "Léo", com uma carteira e um cartão próprios.
/// </summary>
public class McpIsolationTests(PostgresFixture postgres) : McpTestBase(postgres)
{
    private static List<JsonElement> Items(JsonElement body, string name) => body.GetProperty(name).EnumerateArray().ToList();

    private static string Code(JsonElement error) => error.GetProperty("code").GetString()!;

    private static string[] Names(JsonElement body, string name) => Items(body, name).Select(i => i.GetProperty("name").GetString()!).ToArray();

    /// <summary>A conecta e semeia o Espaço A; B conecta com carteira "Rosa" e cartão "Roxo" e não vê nada de A.</summary>
    private async Task<(LiaSpace A, McpSession McpA, McpSession McpB, HttpClient HttpB)> TwoSpacesAsync()
    {
        var (a, mcpA) = await ConnectLiaAsync();
        var (httpB, accountB) = await CardTestSupport.NewSpaceAsync(Factory, "Léo");
        var rosa = await CardTestSupport.CreateWalletAsync(httpB, "Rosa");
        await CardTestSupport.CreateCardAsync(httpB, "Roxo", rosa.Id);
        var (_, tokens) = await new OAuthFlow(Factory).ConnectAsync(accountB.AccessToken);
        return (a, mcpA, await McpSession.ConnectAsync(Factory, tokens.AccessToken), httpB);
    }

    [Fact]
    public async Task Consultas_so_enxergam_o_espaco_do_token()
    {
        var (_, mcpA, mcpB, _) = await TwoSpacesAsync();
        await using var sessionA = mcpA;
        await using var sessionB = mcpB;

        var catalog = await mcpB.OkAsync("list_catalog");
        Names(catalog, "wallets").ShouldBe(["Rosa"]);
        Names(catalog, "creditCards").ShouldBe(["Roxo"]);
        Items(catalog, "tags").ShouldBeEmpty();
        Names(await mcpB.OkAsync("get_wallet_balances"), "wallets").ShouldBe(["Rosa"]);

        (await mcpB.OkAsync("search_entries", new { limit = 50 })).GetProperty("totalCount").GetInt32().ShouldBe(0);
        Items(await mcpB.OkAsync("list_pending"), "items").ShouldBeEmpty();
        var month = await mcpB.OkAsync("get_month_summary", new { month = "2026-03" });
        month.GetProperty("actual").GetProperty("incomeCents").GetInt64().ShouldBe(0);
        month.GetProperty("planned").GetProperty("incomeCents").GetInt64().ShouldBe(0);

        // Controle: o Espaço A tem os dados.
        (await mcpA.OkAsync("search_entries", new { limit = 50 })).GetProperty("totalCount").GetInt32().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Nomes_do_outro_espaco_nao_resolvem_e_as_opcoes_sao_so_as_do_proprio()
    {
        var (_, mcpA, mcpB, _) = await TwoSpacesAsync();
        await using var sessionA = mcpA;
        await using var sessionB = mcpB;

        var before = (await mcpA.OkAsync("search_entries", new { limit = 50 })).GetProperty("totalCount").GetInt32();
        var wallet = await mcpB.ErrorAsync("create_entry", new { kind = "expense", description = "Intruso", amount = "10,00", wallet = "Azul" });
        Code(wallet).ShouldBe("ambiguous_or_not_found");
        Items(wallet, "options").Select(o => o.GetString()).ShouldBe(["Rosa"]);

        var card = await mcpB.ErrorAsync("create_card_purchase", new { description = "Intruso", amount = "10,00", creditCard = "Verde" });
        Code(card).ShouldBe("ambiguous_or_not_found");
        Items(card, "options").Select(o => o.GetString()).ShouldBe(["Roxo"]);

        Code(await mcpB.ErrorAsync("create_entry", new
        {
            kind = "expense",
            description = "Intruso",
            amount = "10,00",
            split = new { mode = "equal", parts = new object[] { new { tag = (string?)null }, new { tag = "Bruna" } } },
        }))
            .ShouldBe("ambiguous_or_not_found");
        // Tag comum nova é criada no Espaço de quem pediu (004 US1.2), nunca ligada à tag de A.
        await mcpB.OkAsync("create_entry", new { kind = "expense", description = "Café", amount = "4,00", tags = new[] { "Lazer" } });
        Names(await mcpB.OkAsync("list_catalog"), "tags").ShouldBe(["Lazer"]);
        Code(await mcpB.ErrorAsync("get_invoice", new { creditCard = "Verde" })).ShouldBe("ambiguous_or_not_found");
        Code(await mcpB.ErrorAsync("pay_invoice", new { creditCard = "Verde", confirmed = true })).ShouldBe("ambiguous_or_not_found");
        Code(await mcpB.ErrorAsync("get_settlements", new { tag = "Bruna" })).ShouldBe("ambiguous_or_not_found");
        Code(await mcpB.ErrorAsync("create_transfer", new { fromWallet = "Azul", toWallet = "Rosa", amount = "5,00" })).ShouldBe("ambiguous_or_not_found");

        (await mcpB.OkAsync("search_entries", new { limit = 50 })).GetProperty("totalCount").GetInt32().ShouldBe(1);
        (await mcpA.OkAsync("search_entries", new { limit = 50 })).GetProperty("totalCount").GetInt32().ShouldBe(before);
    }

    [Fact]
    public async Task Ids_de_outro_espaco_dao_nao_encontrado_e_nada_muda_em_A()
    {
        var (a, mcpA, mcpB, _) = await TwoSpacesAsync();
        await using var sessionA = mcpA;
        await using var sessionB = mcpB;

        var all = Items(await mcpA.OkAsync("search_entries", new { limit = 50 }), "items");
        string IdOf(string type) => all.First(i => i.GetProperty("type").GetString() == type).GetProperty("id").GetString()!;
        var entryId = a.PendingEntry.ToString();
        var purchaseId = IdOf("card_purchase");
        var transferId = IdOf("transfer");
        var before = (await mcpA.OkAsync("search_entries", new { limit = 50 })).GetProperty("totalCount").GetInt32();

        Code(await mcpB.ErrorAsync("set_entry_status", new { id = entryId, status = "settled", confirmed = true })).ShouldBe("not_found");
        Code(await mcpB.ErrorAsync("delete_entry", new { id = entryId, type = "entry", confirmed = true })).ShouldBe("not_found");
        Code(await mcpB.ErrorAsync("delete_entry", new { id = purchaseId, type = "card_purchase", confirmed = true })).ShouldBe("not_found");
        Code(await mcpB.ErrorAsync("delete_entry", new { id = transferId, type = "transfer", confirmed = true })).ShouldBe("not_found");
        Code(await mcpB.ErrorAsync("delete_entry", new { id = entryId, type = "entry" })).ShouldBe("not_found");
        foreach (var id in new[] { entryId, purchaseId, transferId })
        {
            Code(await mcpB.ErrorAsync("undo_creation", new { id })).ShouldBe("not_found");
        }

        // A segue intacta: mesma contagem e a pendência continua prevista.
        (await mcpA.OkAsync("search_entries", new { limit = 50 })).GetProperty("totalCount").GetInt32().ShouldBe(before);
        (await a.Http.GetFromJsonAsync<JsonElement>($"/api/v1/entries/{entryId}")).GetProperty("status").GetString().ShouldBe("planned");
    }
}
