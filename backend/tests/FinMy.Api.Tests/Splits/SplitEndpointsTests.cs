using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Api.Tests.Cards;
using FinMy.Api.Tests.Entries;
using FinMy.Api.Tests.Support;
using FinMy.Application.Tests.Infrastructure;
using static FinMy.Api.Tests.Cards.CardTestSupport;

namespace FinMy.Api.Tests.Splits;

/// <summary>
/// <c>POST /entries/split</c> e <c>POST /card-purchases/split</c> (004 US4.1–US4.5; research R5). Cartão Verde fecha dia 18 e
/// vence dia 25; pessoas e bancos fictícios (Lia, Bruna, Carla; Azul).
/// </summary>
public class SplitEndpointsTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private const string EntriesSplit = "/api/v1/entries/split";
    private const string PurchasesSplit = "/api/v1/card-purchases/split";

    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?> { ["RateLimiting:Auth:PermitLimit"] = "100000" };

    private sealed record Space(HttpClient Http, Guid WalletId, Guid CardId, Guid Bruna, Guid Carla);

    private async Task<Space> NewSpaceAsync(string name = "Lia")
    {
        var (http, _) = await CardTestSupport.NewSpaceAsync(Factory, name);
        var wallet = await CreateWalletAsync(http, "Azul");
        var card = await CreateCardAsync(http, "Verde", wallet.Id);
        return new Space(http, wallet.Id, card.Id, await TagAsync(http, "Bruna", isSettlement: true), await TagAsync(http, "Carla", isSettlement: true));
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

    private static object Equal(params Guid?[] tags) => new { mode = "equal", parts = tags.Select(t => new { settlementTagId = t }).ToArray() };

    private static object Amounts(params (Guid? Tag, string Amount)[] parts) =>
        new { mode = "amounts", parts = parts.Select(p => new { settlementTagId = p.Tag, amount = p.Amount }).ToArray() };

    private static object ExpenseBody(Space s, string amount, object split, Guid[]? tagIds = null, string kind = "expense") =>
        new { kind, description = "Jacomar Niver Carla", amount, date = "2026-03-10", walletId = s.WalletId, notes = "presente", tagIds, split };

    private static object PurchaseBody(
        Space s, string amount, object split, object? installments = null, string? nature = null, Guid[]? tagIds = null) =>
        new { description = "MYNO", amount, date = "2026-03-10", nature, creditCardId = s.CardId, installments, tagIds, split };

    private static Task<HttpResponseMessage> PostAsync(HttpClient http, string url, object body, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        return http.SendAsync(request);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.UnprocessableEntity)
    {
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync());
        return (await response.JsonAsync()).GetProperty("code").GetString();
    }

    private static List<Guid> Ids(JsonElement items) => items.EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();

    private static List<long> Cents(JsonElement items) => items.EnumerateArray().Select(i => i.GetProperty("amountCents").GetInt64()).ToList();

    private static async Task<int> EntriesInMonthAsync(Space s) =>
        (await OkAsync(await s.Http.GetAsync("/api/v1/months/2026-03"))).GetProperty("entries").GetArrayLength();

    private static async Task<int> PurchasesInInvoicesAsync(Space s)
    {
        var march = await OkAsync(await s.Http.GetAsync($"/api/v1/credit-cards/{s.CardId}/invoices/2026-03"));
        var april = await OkAsync(await s.Http.GetAsync($"/api/v1/credit-cards/{s.CardId}/invoices/2026-04"));
        return march.GetProperty("purchases").GetArrayLength() + april.GetProperty("purchases").GetArrayLength();
    }

    // --- Saídas (US4.1, US4.3, US4.4, US4.5) ---

    [Fact]
    public async Task Saida_dividida_em_partes_iguais_cria_uma_saida_por_parte()
    {
        var s = await NewSpaceAsync();

        var parts = await OkAsync(await PostAsync(s.Http, EntriesSplit, ExpenseBody(s, "239,80", Equal(null, s.Bruna))), HttpStatusCode.Created);

        Cents(parts).ShouldBe([11990L, 11990L]);
        parts.EnumerateArray().Select(p => p.GetProperty("description").GetString()).ShouldAllBe(d => d == "Jacomar Niver Carla");
        parts.EnumerateArray().Select(p => p.GetProperty("date").GetString()).ShouldAllBe(d => d == "2026-03-10");
        parts.EnumerateArray().Select(p => p.GetProperty("walletId").GetGuid()).ShouldAllBe(w => w == s.WalletId);
        parts.EnumerateArray().Select(p => p.GetProperty("notes").GetString()).ShouldAllBe(n => n == "presente");
        parts[0].GetProperty("settlementTagId").ValueKind.ShouldBe(JsonValueKind.Null);
        parts[1].GetProperty("settlementTagId").GetGuid().ShouldBe(s.Bruna);
        parts[1].GetProperty("tagIds").EnumerateArray().Select(t => t.GetGuid()).ShouldBe([s.Bruna]);

        var group = parts[0].GetProperty("splitGroupId").GetGuid();
        parts[1].GetProperty("splitGroupId").GetGuid().ShouldBe(group);
        parts.EnumerateArray().Select(p => p.GetProperty("splitCount").GetInt32()).ShouldAllBe(n => n == 2);

        var again = await OkAsync(await s.Http.GetAsync($"/api/v1/entries/{parts[1].GetProperty("id").GetGuid()}"));
        again.GetProperty("splitCount").GetInt32().ShouldBe(2);
        (await EntriesInMonthAsync(s)).ShouldBe(2);

        var month = await OkAsync(await s.Http.GetAsync("/api/v1/months/2026-03"));
        month.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("splitCount").GetInt32()).ShouldAllBe(n => n == 2);
    }

    [Fact]
    public async Task Partes_iguais_de_100_em_3_dao_3334_3333_3333_na_ordem_pedida()
    {
        var s = await NewSpaceAsync();

        var parts = await OkAsync(await PostAsync(s.Http, EntriesSplit, ExpenseBody(s, "100,00", Equal(s.Carla, null, s.Bruna))), HttpStatusCode.Created);

        Cents(parts).ShouldBe([3334L, 3333L, 3333L]);
        parts[0].GetProperty("settlementTagId").GetGuid().ShouldBe(s.Carla);
        parts[1].GetProperty("settlementTagId").ValueKind.ShouldBe(JsonValueKind.Null);
        parts[2].GetProperty("settlementTagId").GetGuid().ShouldBe(s.Bruna);
    }

    [Fact]
    public async Task Valores_informados_criam_as_partes_e_mantem_as_tags_comuns_em_todas()
    {
        var s = await NewSpaceAsync();
        var presente = await TagAsync(s.Http, "Presente");

        var parts = await OkAsync(
            await PostAsync(s.Http, EntriesSplit, ExpenseBody(s, "100,00", Amounts((null, "60,00"), (s.Bruna, "25,50"), (s.Carla, "14,50")), [presente])),
            HttpStatusCode.Created);

        Cents(parts).ShouldBe([6000L, 2550L, 1450L]);
        foreach (var part in parts.EnumerateArray())
        {
            part.GetProperty("tagIds").EnumerateArray().Select(t => t.GetGuid()).ShouldContain(presente);
        }
    }

    [Fact]
    public async Task Soma_diferente_do_total_devolve_a_diferenca_e_nao_salva()
    {
        var s = await NewSpaceAsync();

        var response = await PostAsync(s.Http, EntriesSplit, ExpenseBody(s, "100,00", Amounts((null, "60,00"), (s.Bruna, "30,00"))));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await response.JsonAsync();
        problem.GetProperty("code").GetString().ShouldBe("split.sum_mismatch");
        problem.GetProperty("differenceCents").GetInt64().ShouldBe(1000);
        (await EntriesInMonthAsync(s)).ShouldBe(0);
    }

    [Fact]
    public async Task Soma_acima_do_total_tem_diferenca_negativa()
    {
        var s = await NewSpaceAsync();

        var response = await PostAsync(s.Http, EntriesSplit, ExpenseBody(s, "100,00", Amounts((null, "60,00"), (s.Bruna, "45,00"))));

        (await CodeAsync(response)).ShouldBe("split.sum_mismatch");
        (await response.JsonAsync()).GetProperty("differenceCents").GetInt64().ShouldBe(-500);
    }

    [Fact]
    public async Task Entrada_nao_pode_ser_dividida()
    {
        var s = await NewSpaceAsync();

        (await CodeAsync(await PostAsync(s.Http, EntriesSplit, ExpenseBody(s, "100,00", Equal(null, s.Bruna), kind: "income"))))
            .ShouldBe("split.expense_only");
        (await EntriesInMonthAsync(s)).ShouldBe(0);
    }

    [Fact]
    public async Task Divisao_invalida_e_recusada_sem_criar_nada()
    {
        var s = await NewSpaceAsync();
        var comum = await TagAsync(s.Http, "Presente");

        async Task<string?> CodeOfAsync(object body) => await CodeAsync(await PostAsync(s.Http, EntriesSplit, body));

        (await CodeOfAsync(ExpenseBody(s, "10,00", Equal(s.Bruna)))).ShouldBe("split.parts_count");
        (await CodeOfAsync(ExpenseBody(s, "10,00", Equal(Enumerable.Range(0, 11).Select(_ => (Guid?)Guid.NewGuid()).ToArray()))))
            .ShouldBe("split.parts_count");
        (await CodeOfAsync(ExpenseBody(s, "10,00", Equal(s.Bruna, s.Bruna)))).ShouldBe("split.duplicate_part");
        (await CodeOfAsync(ExpenseBody(s, "10,00", Equal(null, null)))).ShouldBe("split.duplicate_part");
        (await CodeOfAsync(ExpenseBody(s, "10,00", Equal(null, comum)))).ShouldBe("split.part_not_settlement");
        (await CodeOfAsync(ExpenseBody(s, "10,00", new { mode = "metade", parts = new[] { new { settlementTagId = (Guid?)null }, new { settlementTagId = (Guid?)s.Bruna } } })))
            .ShouldBe("split.mode_invalid");
        (await CodeOfAsync(ExpenseBody(s, "10,00", Amounts((null, "5,00"), (s.Bruna, "0"))))).ShouldBe("amount.must_be_positive");
        (await CodeOfAsync(ExpenseBody(s, "10,00", Equal(null, s.Bruna), [s.Carla]))).ShouldBe("split.settlement_tag_in_tag_ids");

        var semDivisao = await PostAsync(s.Http, EntriesSplit, new { kind = "expense", description = "X", amount = "10,00", date = "2026-03-10", walletId = s.WalletId });
        (await CodeAsync(semDivisao)).ShouldBe("split.required");

        (await EntriesInMonthAsync(s)).ShouldBe(0);
    }

    [Fact]
    public async Task Tag_de_acerto_arquivada_ou_de_outro_espaco_nao_pode_ser_parte()
    {
        var s = await NewSpaceAsync();
        var outro = await NewSpaceAsync("Léo");

        (await PostAsync(s.Http, EntriesSplit, ExpenseBody(s, "10,00", Equal(null, outro.Bruna)))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await s.Http.PostAsync($"/api/v1/tags/{s.Carla}/archive", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await CodeAsync(await PostAsync(s.Http, EntriesSplit, ExpenseBody(s, "10,00", Equal(null, s.Carla))))).ShouldBe("tag.archived");
        (await EntriesInMonthAsync(s)).ShouldBe(0);
    }

    [Fact]
    public async Task Repetir_a_mesma_chave_de_idempotencia_nao_duplica_as_partes()
    {
        var s = await NewSpaceAsync();
        var key = Guid.NewGuid().ToString();
        var body = ExpenseBody(s, "239,80", Equal(null, s.Bruna));

        var first = await OkAsync(await PostAsync(s.Http, EntriesSplit, body, key), HttpStatusCode.Created);
        var second = await OkAsync(await PostAsync(s.Http, EntriesSplit, body, key), HttpStatusCode.Created);

        Ids(second).ShouldBe(Ids(first));
        (await EntriesInMonthAsync(s)).ShouldBe(2);
    }

    [Fact]
    public async Task Editar_ou_excluir_uma_parte_nao_mexe_nas_outras()
    {
        var s = await NewSpaceAsync();
        var parts = await OkAsync(await PostAsync(s.Http, EntriesSplit, ExpenseBody(s, "239,80", Equal(null, s.Bruna))), HttpStatusCode.Created);
        var mine = parts[0];
        var brunas = parts[1];

        var edited = await OkAsync(await s.Http.PutAsJsonAsync($"/api/v1/entries/{mine.GetProperty("id").GetGuid()}", new
        {
            kind = "expense",
            description = "Jacomar",
            amount = "100,00",
            date = "2026-03-10",
            walletId = s.WalletId,
            status = mine.GetProperty("status").GetString(),
            version = mine.GetProperty("version").GetInt64(),
        }));
        edited.GetProperty("amountCents").GetInt64().ShouldBe(10000);
        edited.GetProperty("splitGroupId").GetGuid().ShouldBe(mine.GetProperty("splitGroupId").GetGuid());

        var untouched = await OkAsync(await s.Http.GetAsync($"/api/v1/entries/{brunas.GetProperty("id").GetGuid()}"));
        untouched.GetProperty("amountCents").GetInt64().ShouldBe(11990);
        untouched.GetProperty("description").GetString().ShouldBe("Jacomar Niver Carla");

        (await s.Http.DeleteAsync($"/api/v1/entries/{mine.GetProperty("id").GetGuid()}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var remaining = await OkAsync(await s.Http.GetAsync($"/api/v1/entries/{brunas.GetProperty("id").GetGuid()}"));
        remaining.GetProperty("amountCents").GetInt64().ShouldBe(11990);
        remaining.GetProperty("splitCount").GetInt32().ShouldBe(1);
    }

    // --- Compras no cartão (US4.1, US4.2, US4.5) ---

    [Fact]
    public async Task Compra_dividida_em_partes_iguais_cria_uma_compra_por_parte_no_mesmo_cartao()
    {
        var s = await NewSpaceAsync();

        var result = await OkAsync(await PostAsync(s.Http, PurchasesSplit, PurchaseBody(s, "239,80", Equal(null, s.Bruna))), HttpStatusCode.Created);

        var parts = result.GetProperty("purchases");
        Cents(parts).ShouldBe([11990L, 11990L]);
        result.GetProperty("warnings").GetArrayLength().ShouldBe(0);
        parts.EnumerateArray().Select(p => p.GetProperty("description").GetString()).ShouldAllBe(d => d == "MYNO");
        parts.EnumerateArray().Select(p => p.GetProperty("creditCardId").GetGuid()).ShouldAllBe(c => c == s.CardId);
        parts.EnumerateArray().Select(p => p.GetProperty("date").GetString()).ShouldAllBe(d => d == "2026-03-10");
        parts[0].GetProperty("settlementTagId").ValueKind.ShouldBe(JsonValueKind.Null);
        parts[1].GetProperty("settlementTagId").GetGuid().ShouldBe(s.Bruna);
        parts[1].GetProperty("splitGroupId").GetGuid().ShouldBe(parts[0].GetProperty("splitGroupId").GetGuid());
        parts.EnumerateArray().Select(p => p.GetProperty("splitCount").GetInt32()).ShouldAllBe(n => n == 2);
    }

    [Fact]
    public async Task Compra_parcelada_dividida_vira_um_parcelamento_por_parte()
    {
        var s = await NewSpaceAsync();
        var installments = new { total = 4, mode = "total_amount" };

        var result = await OkAsync(
            await PostAsync(s.Http, PurchasesSplit, PurchaseBody(s, "207,90", Amounts((null, "110,94"), (s.Carla, "11,99"), (s.Bruna, "84,97")), installments)),
            HttpStatusCode.Created);

        var purchases = result.GetProperty("purchases").EnumerateArray().ToList();
        purchases.Count.ShouldBe(12);

        var plans = purchases.GroupBy(p => p.GetProperty("installmentPlanId").GetGuid()).ToList();
        plans.Count.ShouldBe(3);
        plans.ShouldAllBe(g => g.Count() == 4);
        plans.Select(g => g.Select(p => p.GetProperty("installmentTotal").GetInt32()).Distinct().Single()).ShouldAllBe(t => t == 4);

        // A ordem das partes se mantém; dentro de cada parte, as parcelas em ordem.
        plans.Select(g => g.Select(p => p.GetProperty("amountCents").GetInt64()).ToList()).ToList().ShouldBe(
        [
            [2775L, 2773L, 2773L, 2773L],
            [302L, 299L, 299L, 299L],
            [2125L, 2124L, 2124L, 2124L],
        ]);
        plans.Select(g => g.First().GetProperty("settlementTagId").ValueKind == JsonValueKind.Null
            ? (Guid?)null
            : g.First().GetProperty("settlementTagId").GetGuid()).ToList().ShouldBe([null, s.Carla, s.Bruna]);
        purchases.Select(p => p.GetProperty("splitCount").GetInt32()).ShouldAllBe(n => n == 3);
        purchases.Select(p => p.GetProperty("splitGroupId").GetGuid()).Distinct().Count().ShouldBe(1);
    }

    [Fact]
    public async Task Valor_da_parcela_vira_total_antes_de_dividir()
    {
        var s = await NewSpaceAsync();
        var installments = new { total = 3, mode = "installment_amount" };

        // 3 × 10,00 = 30,00 → 15,00 por parte, em 3 parcelas de 5,00.
        var result = await OkAsync(await PostAsync(s.Http, PurchasesSplit, PurchaseBody(s, "10,00", Equal(null, s.Bruna), installments)), HttpStatusCode.Created);

        var purchases = result.GetProperty("purchases").EnumerateArray().ToList();
        purchases.Count.ShouldBe(6);
        purchases.Select(p => p.GetProperty("amountCents").GetInt64()).ShouldAllBe(c => c == 500);
        purchases.Select(p => p.GetProperty("installmentPlanId").GetGuid()).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public async Task Compra_dividida_com_soma_diferente_nao_salva_nada()
    {
        var s = await NewSpaceAsync();

        var response = await PostAsync(s.Http, PurchasesSplit, PurchaseBody(s, "207,90", Amounts((null, "110,94"), (s.Bruna, "84,97")), new { total = 4, mode = "total_amount" }));

        (await CodeAsync(response)).ShouldBe("split.sum_mismatch");
        (await response.JsonAsync()).GetProperty("differenceCents").GetInt64().ShouldBe(1199);
        (await PurchasesInInvoicesAsync(s)).ShouldBe(0);
    }

    [Fact]
    public async Task Credito_e_parcelamento_em_andamento_nao_podem_ser_divididos()
    {
        var s = await NewSpaceAsync();

        (await CodeAsync(await PostAsync(s.Http, PurchasesSplit, PurchaseBody(s, "50,00", Equal(null, s.Bruna), nature: "credit"))))
            .ShouldBe("split.credit_not_allowed");
        (await CodeAsync(await PostAsync(s.Http, PurchasesSplit, PurchaseBody(s, "-50,00", Equal(null, s.Bruna)))))
            .ShouldBe("split.credit_not_allowed");
        (await CodeAsync(await PostAsync(
            s.Http, PurchasesSplit, PurchaseBody(s, "50,00", Equal(null, s.Bruna), new { total = 4, mode = "in_progress", nextNumber = 2 }))))
            .ShouldBe("split.in_progress_not_allowed");

        (await PurchasesInInvoicesAsync(s)).ShouldBe(0);
    }

    [Fact]
    public async Task Compra_dividida_com_tag_de_acerto_em_tag_ids_ou_parte_invalida_e_recusada()
    {
        var s = await NewSpaceAsync();
        var comum = await TagAsync(s.Http, "Presente");

        (await CodeAsync(await PostAsync(s.Http, PurchasesSplit, PurchaseBody(s, "50,00", Equal(null, s.Bruna), tagIds: [s.Carla]))))
            .ShouldBe("split.settlement_tag_in_tag_ids");
        (await CodeAsync(await PostAsync(s.Http, PurchasesSplit, PurchaseBody(s, "50,00", Equal(null, comum)))))
            .ShouldBe("split.part_not_settlement");
        (await CodeAsync(await PostAsync(s.Http, PurchasesSplit, PurchaseBody(s, "50,00", Equal(s.Bruna)))))
            .ShouldBe("split.parts_count");
        (await PostAsync(s.Http, PurchasesSplit, PurchaseBody(s, "50,00", Equal(null, Guid.NewGuid())))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await PurchasesInInvoicesAsync(s)).ShouldBe(0);
    }

    [Fact]
    public async Task Compra_dividida_leva_as_tags_comuns_em_todas_as_partes_e_respeita_a_idempotencia()
    {
        var s = await NewSpaceAsync();
        var presente = await TagAsync(s.Http, "Presente");
        var key = Guid.NewGuid().ToString();
        var body = PurchaseBody(s, "239,80", Equal(null, s.Bruna), tagIds: [presente]);

        var first = await OkAsync(await PostAsync(s.Http, PurchasesSplit, body, key), HttpStatusCode.Created);
        var second = await OkAsync(await PostAsync(s.Http, PurchasesSplit, body, key), HttpStatusCode.Created);

        Ids(second.GetProperty("purchases")).ShouldBe(Ids(first.GetProperty("purchases")));
        foreach (var part in first.GetProperty("purchases").EnumerateArray())
        {
            part.GetProperty("tagIds").EnumerateArray().Select(t => t.GetGuid()).ShouldContain(presente);
        }

        (await PurchasesInInvoicesAsync(s)).ShouldBe(2);
    }

    [Fact]
    public async Task Excluir_ou_editar_uma_parte_do_parcelamento_nao_mexe_nos_outros()
    {
        var s = await NewSpaceAsync();
        var result = await OkAsync(
            await PostAsync(s.Http, PurchasesSplit, PurchaseBody(s, "207,90", Equal(null, s.Bruna), new { total = 4, mode = "total_amount" })),
            HttpStatusCode.Created);
        var purchases = result.GetProperty("purchases").EnumerateArray().ToList();
        var mine = purchases.Where(p => p.GetProperty("settlementTagId").ValueKind == JsonValueKind.Null).ToList();
        var brunas = purchases.Where(p => p.GetProperty("settlementTagId").ValueKind != JsonValueKind.Null).ToList();

        var first = mine[0];
        var edited = await OkAsync(await s.Http.PutAsJsonAsync($"/api/v1/card-purchases/{first.GetProperty("id").GetGuid()}", new
        {
            description = "MYNO (minha parte)",
            amount = "26,00",
            date = "2026-03-10",
            creditCardId = s.CardId,
            scope = "all",
            version = first.GetProperty("version").GetInt64(),
        }));
        edited.GetProperty("description").GetString().ShouldBe("MYNO (minha parte)");

        foreach (var part in brunas)
        {
            var current = await OkAsync(await s.Http.GetAsync($"/api/v1/card-purchases/{part.GetProperty("id").GetGuid()}"));
            current.GetProperty("description").GetString().ShouldBe("MYNO");
            current.GetProperty("amountCents").GetInt64().ShouldBe(part.GetProperty("amountCents").GetInt64());
        }

        (await s.Http.DeleteAsync($"/api/v1/card-purchases/{first.GetProperty("id").GetGuid()}?scope=all")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var remaining = await OkAsync(await s.Http.GetAsync($"/api/v1/card-purchases/{brunas[0].GetProperty("id").GetGuid()}"));
        remaining.GetProperty("splitCount").GetInt32().ShouldBe(1);
        (await s.Http.GetAsync($"/api/v1/card-purchases/{first.GetProperty("id").GetGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        foreach (var part in brunas)
        {
            (await s.Http.GetAsync($"/api/v1/card-purchases/{part.GetProperty("id").GetGuid()}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }
}
