using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Api.Tests.Cards;
using FinMy.Api.Tests.Entries;
using FinMy.Api.Tests.Support;
using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Cards;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;
using FinMy.Domain.Tags;
using Microsoft.EntityFrameworkCore;
using static FinMy.Api.Tests.Cards.CardTestSupport;

namespace FinMy.Api.Tests.Recurrences;

/// <summary>
/// <c>POST /recurrences/from-item</c> (005 US4.1 e lançamentos não elegíveis). "Hoje" é 05/03/2026 (horizonte: dezembro de 2027);
/// dados fictícios (carteira Azul, cartão Azul fecha dia 18 e vence dia 25).
/// </summary>
public class RecurrenceFromItemTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private const string Url = "/api/v1/recurrences/from-item";

    protected override IClock? Clock => new FixedTodayClock(new DateOnly(2026, 3, 5));

    private sealed record Space(HttpClient Http, TestAccount Account, Guid WalletId, Guid CardId);

    private async Task<Space> NewSpaceAsync(string name = "Lia")
    {
        var (http, account) = await CardTestSupport.NewSpaceAsync(Factory, name);
        var wallet = await CreateWalletAsync(http, "Azul");
        var card = await CreateCardAsync(http, "Azul", wallet.Id);
        return new Space(http, account, wallet.Id, card.Id);
    }

    private static async Task<JsonElement> JsonOkAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    private static async Task<string> CodeOfAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync());
        return (await response.JsonAsync()).GetProperty("code").GetString()!;
    }

    private static async Task<Guid> TagAsync(Space s, string name, bool isSettlement = false)
    {
        var body = await JsonOkAsync(await s.Http.PostAsJsonAsync("/api/v1/tags", new { name, isSettlement }), HttpStatusCode.Created);
        return body.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateEntryAsync(
        Space s, string description, string amount, string date, string kind = "expense", string? referenceMonth = null, string? status = null, Guid[]? tagIds = null)
    {
        var body = await JsonOkAsync(
            await s.Http.PostAsJsonAsync("/api/v1/entries", new { kind, description, amount, date, walletId = s.WalletId, referenceMonth, status, tagIds }),
            HttpStatusCode.Created);
        return body.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreatePurchaseAsync(Space s, string description, string amount, string date, string? nature = null, object? installments = null)
    {
        var body = await JsonOkAsync(
            await s.Http.PostAsJsonAsync("/api/v1/card-purchases", new { description, amount, date, creditCardId = s.CardId, nature, installments }),
            HttpStatusCode.Created);
        return body.GetProperty("purchases")[0].GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> FromItemAsync(Space s, string type, Guid id, object? end = null)
    {
        var body = new Dictionary<string, object?> { ["type"] = type, ["id"] = id };
        foreach (var property in end?.GetType().GetProperties() ?? [])
        {
            body[property.Name] = property.GetValue(end);
        }

        return s.Http.PostAsJsonAsync(Url, body);
    }

    private Task<List<Entry>> EntriesAsync(Space s, Guid recurrenceId) =>
        WithDbAsync(Factory, s.Account, (db, _) =>
            db.Set<Entry>().AsNoTracking().Where(e => e.RecurrenceId == recurrenceId).OrderBy(e => e.RecurrenceMonth).ToListAsync());

    private Task<List<CardPurchase>> PurchasesAsync(Space s, Guid recurrenceId) =>
        WithDbAsync(Factory, s.Account, (db, _) =>
            db.Set<CardPurchase>().AsNoTracking().Where(p => p.RecurrenceId == recurrenceId).OrderBy(p => p.RecurrenceMonth).ToListAsync());

    private Task<Entry> EntryAsync(Space s, Guid id) =>
        WithDbAsync(Factory, s.Account, (db, _) => db.Set<Entry>().AsNoTracking().SingleAsync(e => e.Id == id));

    private Task<CardPurchase> PurchaseAsync(Space s, Guid id) =>
        WithDbAsync(Factory, s.Account, (db, _) => db.Set<CardPurchase>().AsNoTracking().SingleAsync(p => p.Id == id));

    private static string Month(DateOnly? date) => YearMonth.From(date!.Value).ToString();

    [Fact]
    public async Task Saida_vira_a_ocorrencia_do_mes_e_as_seguintes_sao_geradas()
    {
        var s = await NewSpaceAsync();
        var seguro = await CreateEntryAsync(s, "Seguro Vida", "25,99", "2026-03-25");

        var body = await JsonOkAsync(await FromItemAsync(s, "entry", seguro), HttpStatusCode.Created);

        body.GetProperty("kind").GetString().ShouldBe("expense");
        body.GetProperty("description").GetString().ShouldBe("Seguro Vida");
        body.GetProperty("amountCents").GetInt64().ShouldBe(2599);
        body.GetProperty("dayOfMonth").GetInt32().ShouldBe(25);
        body.GetProperty("startMonth").GetString().ShouldBe("2026-03");
        body.GetProperty("walletId").GetGuid().ShouldBe(s.WalletId);
        body.GetProperty("referenceMonthRule").GetString().ShouldBe("same_month");
        body.GetProperty("endType").GetString().ShouldBe("none");
        body.GetProperty("generatedUntilMonth").GetString().ShouldBe("2027-12");
        body.GetProperty("status").GetString().ShouldBe("active");
        body.GetProperty("nextOccurrenceDate").GetString().ShouldBe("2026-03-25");

        var occurrences = await EntriesAsync(s, body.GetProperty("id").GetGuid());
        occurrences.Count.ShouldBe(22); // 2026-03 a 2027-12
        occurrences.Select(e => Month(e.RecurrenceMonth)).Distinct().Count().ShouldBe(22);

        var first = occurrences[0];
        first.Id.ShouldBe(seguro);
        Month(first.RecurrenceMonth).ShouldBe("2026-03");
        first.RecurrenceModified.ShouldBeFalse();
        first.EntryDate.ShouldBe(new DateOnly(2026, 3, 25));

        var april = occurrences[1];
        april.Id.ShouldNotBe(seguro);
        april.EntryDate.ShouldBe(new DateOnly(2026, 4, 25));
        april.Status.ShouldBe(EntryStatus.Planned);
        april.AmountCents.ShouldBe(2599);

        var all = await WithDbAsync(Factory, s.Account, (db, _) =>
            db.Set<Entry>().AsNoTracking().CountAsync(e => e.Description == "Seguro Vida" && e.EntryDate.Month == 3 && e.EntryDate.Year == 2026));
        all.ShouldBe(1); // o lançamento não foi duplicado
    }

    [Fact]
    public async Task Referencia_no_mes_seguinte_vira_regra_next_month_e_as_ocorrencias_a_seguem()
    {
        var s = await NewSpaceAsync();
        var cartao = await CreateEntryAsync(s, "Fatura Verde", "300,00", "2026-03-28", referenceMonth: "2026-04");

        var body = await JsonOkAsync(await FromItemAsync(s, "entry", cartao), HttpStatusCode.Created);

        body.GetProperty("referenceMonthRule").GetString().ShouldBe("next_month");
        var occurrences = await EntriesAsync(s, body.GetProperty("id").GetGuid());
        occurrences[0].ReferenceMonth.ShouldBe(new DateOnly(2026, 4, 1)); // o original não mudou
        occurrences[1].EntryDate.ShouldBe(new DateOnly(2026, 4, 28));
        occurrences[1].ReferenceMonth.ShouldBe(new DateOnly(2026, 5, 1));
    }

    [Fact]
    public async Task Entrada_paga_continua_paga_e_as_tags_passam_para_a_recorrencia_e_as_ocorrencias()
    {
        var s = await NewSpaceAsync();
        var casa = await TagAsync(s, "Casa");
        var acerto = await TagAsync(s, "Acerto Bruna", isSettlement: true);
        var salario = await CreateEntryAsync(s, "Salário", "3000,00", "2026-03-01", kind: "income", status: "settled", tagIds: [casa, acerto]);

        var body = await JsonOkAsync(await FromItemAsync(s, "entry", salario), HttpStatusCode.Created);

        body.GetProperty("kind").GetString().ShouldBe("income");
        body.GetProperty("tagIds").EnumerateArray().Select(e => e.GetGuid()).ShouldBe([acerto, casa]);
        var original = await EntryAsync(s, salario);
        original.Status.ShouldBe(EntryStatus.Settled);
        original.RecurrenceId.ShouldBe(body.GetProperty("id").GetGuid());

        var next = (await EntriesAsync(s, original.RecurrenceId!.Value))[1];
        next.Status.ShouldBe(EntryStatus.Planned);
        next.SettlementTagId.ShouldBe(acerto);
        var links = await WithDbAsync(Factory, s.Account, (db, _) =>
            db.Set<EntryTag>().AsNoTracking().Where(l => l.EntryId == next.Id).Select(l => l.TagId).ToListAsync());
        links.ShouldBe([casa]);
    }

    [Fact]
    public async Task Termino_informado_conta_o_lancamento_como_a_primeira_ocorrencia()
    {
        var s = await NewSpaceAsync();
        var parcial = await CreateEntryAsync(s, "Academia", "99,90", "2026-03-10");
        var ate = await CreateEntryAsync(s, "Revista", "10,00", "2026-03-12");

        var porContagem = await JsonOkAsync(await FromItemAsync(s, "entry", parcial, new { endType = "after_count", occurrenceLimit = 3 }), HttpStatusCode.Created);
        var porMes = await JsonOkAsync(await FromItemAsync(s, "entry", ate, new { endType = "until_month", endMonth = "2026-05" }), HttpStatusCode.Created);

        porContagem.GetProperty("endMonth").GetString().ShouldBe("2026-05");
        (await EntriesAsync(s, porContagem.GetProperty("id").GetGuid())).Select(e => Month(e.RecurrenceMonth)).ShouldBe(["2026-03", "2026-04", "2026-05"]);
        (await EntriesAsync(s, porMes.GetProperty("id").GetGuid())).Select(e => Month(e.RecurrenceMonth)).ShouldBe(["2026-03", "2026-04", "2026-05"]);
    }

    [Fact]
    public async Task Compra_a_vista_vira_a_ocorrencia_do_mes_e_as_seguintes_caem_nas_faturas_do_ciclo()
    {
        var s = await NewSpaceAsync();
        var uber = await CreatePurchaseAsync(s, "Prime Video", "19,90", "2026-03-10");

        var body = await JsonOkAsync(await FromItemAsync(s, "card_purchase", uber, new { endType = "after_count", occurrenceLimit = 3 }), HttpStatusCode.Created);

        body.GetProperty("kind").GetString().ShouldBe("card_purchase");
        body.GetProperty("creditCardId").GetGuid().ShouldBe(s.CardId);
        body.GetProperty("walletId").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("referenceMonthRule").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("dayOfMonth").GetInt32().ShouldBe(10);

        var purchases = await PurchasesAsync(s, body.GetProperty("id").GetGuid());
        purchases.Select(p => Month(p.RecurrenceMonth)).ShouldBe(["2026-03", "2026-04", "2026-05"]);
        purchases[0].Id.ShouldBe(uber);
        purchases.ShouldAllBe(p => p.Origin == PurchaseOrigin.Recurrence && !p.RecurrenceModified && p.AmountCents == 1990);
        purchases[1].PurchaseDate.ShouldBe(new DateOnly(2026, 4, 10));
        (await PurchaseAsync(s, uber)).PurchaseDate.ShouldBe(new DateOnly(2026, 3, 10));
    }

    [Fact]
    public async Task Compra_parcelada_ou_de_credito_nao_e_elegivel()
    {
        var s = await NewSpaceAsync();
        var parcela = await CreatePurchaseAsync(s, "Casa China", "100,00", "2026-03-02", installments: new { total = 3, mode = "total_amount" });
        var estorno = await CreatePurchaseAsync(s, "Estorno", "10,00", "2026-03-03", nature: "credit");

        (await CodeOfAsync(await FromItemAsync(s, "card_purchase", parcela), HttpStatusCode.UnprocessableEntity)).ShouldBe("recurrence.item_not_eligible");
        (await CodeOfAsync(await FromItemAsync(s, "card_purchase", estorno), HttpStatusCode.UnprocessableEntity)).ShouldBe("recurrence.item_not_eligible");

        (await PurchaseAsync(s, parcela)).RecurrenceId.ShouldBeNull();
        (await PurchaseAsync(s, estorno)).RecurrenceId.ShouldBeNull();
        (await s.Http.GetFromJsonAsync<JsonElement>("/api/v1/recurrences?status=all")).GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Lancamento_que_ja_e_ocorrencia_ou_ja_foi_repetido_nao_e_elegivel()
    {
        var s = await NewSpaceAsync();
        var seguro = await CreateEntryAsync(s, "Seguro Vida", "25,99", "2026-03-25");
        var prime = await CreatePurchaseAsync(s, "Prime Video", "19,90", "2026-03-10");
        await JsonOkAsync(await FromItemAsync(s, "entry", seguro), HttpStatusCode.Created);
        await JsonOkAsync(await FromItemAsync(s, "card_purchase", prime), HttpStatusCode.Created);

        // O próprio lançamento já repetido e uma ocorrência gerada.
        var generatedEntry = (await WithDbAsync(Factory, s.Account, (db, _) =>
            db.Set<Entry>().AsNoTracking().Where(e => e.Description == "Seguro Vida" && e.Id != seguro).Select(e => e.Id).FirstAsync()));
        var generatedPurchase = (await WithDbAsync(Factory, s.Account, (db, _) =>
            db.Set<CardPurchase>().AsNoTracking().Where(p => p.Description == "Prime Video" && p.Id != prime).Select(p => p.Id).FirstAsync()));

        foreach (var (type, id) in new[] { ("entry", seguro), ("entry", generatedEntry), ("card_purchase", prime), ("card_purchase", generatedPurchase) })
        {
            (await CodeOfAsync(await FromItemAsync(s, type, id), HttpStatusCode.UnprocessableEntity)).ShouldBe("recurrence.item_not_eligible");
        }

        (await s.Http.GetFromJsonAsync<JsonElement>("/api/v1/recurrences?status=all")).GetProperty("items").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task Erros_de_entrada_nao_deixam_nada_para_tras()
    {
        var s = await NewSpaceAsync();
        var seguro = await CreateEntryAsync(s, "Seguro Vida", "25,99", "2026-03-25");

        (await CodeOfAsync(await FromItemAsync(s, "entry", Guid.NewGuid()), HttpStatusCode.NotFound)).ShouldBe("not_found");
        (await CodeOfAsync(await FromItemAsync(s, "card_purchase", Guid.NewGuid()), HttpStatusCode.NotFound)).ShouldBe("not_found");
        (await CodeOfAsync(await FromItemAsync(s, "recurrence", seguro), HttpStatusCode.UnprocessableEntity)).ShouldBe("type.invalid");
        (await CodeOfAsync(await s.Http.PostAsJsonAsync(Url, new { type = "entry" }), HttpStatusCode.UnprocessableEntity)).ShouldBe("id.required");
        (await CodeOfAsync(await FromItemAsync(s, "entry", seguro, new { endType = "until_month", endMonth = "2026-02" }), HttpStatusCode.UnprocessableEntity))
            .ShouldBe("recurrence.end_before_start");
        (await CodeOfAsync(await FromItemAsync(s, "entry", seguro, new { endType = "after_count" }), HttpStatusCode.UnprocessableEntity))
            .ShouldBe("recurrence.limit_invalid");

        (await EntryAsync(s, seguro)).RecurrenceId.ShouldBeNull();
        (await s.Http.GetFromJsonAsync<JsonElement>("/api/v1/recurrences?status=all")).GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Lancamento_de_outro_espaco_nao_e_encontrado()
    {
        var lia = await NewSpaceAsync("Lia");
        var leo = await NewSpaceAsync("Léo");
        var seguro = await CreateEntryAsync(lia, "Seguro Vida", "25,99", "2026-03-25");

        (await CodeOfAsync(await FromItemAsync(leo, "entry", seguro), HttpStatusCode.NotFound)).ShouldBe("not_found");
        (await EntryAsync(lia, seguro)).RecurrenceId.ShouldBeNull();
    }

    [Fact]
    public async Task Carteira_arquivada_recusa_e_nada_muda()
    {
        var azul = await NewSpaceAsync();
        var verde = await CreateWalletAsync(azul.Http, "Verde");
        var s = azul with { WalletId = verde.Id };
        var seguro = await CreateEntryAsync(s, "Seguro Vida", "25,99", "2026-03-25");
        (await s.Http.PostAsync($"/api/v1/wallets/{verde.Id}/archive", null)).EnsureSuccessStatusCode();

        (await CodeOfAsync(await FromItemAsync(s, "entry", seguro), HttpStatusCode.UnprocessableEntity)).ShouldBe("wallet.archived");
        (await EntryAsync(s, seguro)).RecurrenceId.ShouldBeNull();
    }
}
