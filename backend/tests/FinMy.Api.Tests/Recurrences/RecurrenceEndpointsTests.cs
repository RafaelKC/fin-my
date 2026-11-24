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
using Microsoft.EntityFrameworkCore;
using static FinMy.Api.Tests.Cards.CardTestSupport;

namespace FinMy.Api.Tests.Recurrences;

/// <summary>
/// <c>POST|GET /recurrences</c> e <c>GET /recurrences/{id}</c> (005 US1.1, US1.3–US1.6, US2.1 e o Edge Case de
/// carteira/cartão/tag arquivados). "Hoje" é 05/03/2026; dados fictícios (carteira Azul, cartão Azul fecha dia 18 e vence dia 25).
/// </summary>
public class RecurrenceEndpointsTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private const string Url = "/api/v1/recurrences";

    private static readonly DateOnly Today = new(2026, 3, 5);

    protected override IClock? Clock => new FixedTodayClock(Today);

    private sealed record Space(HttpClient Http, TestAccount Account, Guid WalletId, Guid CardId);

    private async Task<Space> NewSpaceAsync(string name = "Lia")
    {
        var (http, account) = await CardTestSupport.NewSpaceAsync(Factory, name);
        var wallet = await CreateWalletAsync(http, "Azul");
        var card = await CreateCardAsync(http, "Azul", wallet.Id);
        return new Space(http, account, wallet.Id, card.Id);
    }

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    private static Task<HttpResponseMessage> PostAsync(Space s, object body) => s.Http.PostAsJsonAsync(Url, body);

    private static Dictionary<string, object?> Saida(Space s, string description, string amount, int day, string startMonth, object? extra = null, Guid[]? tagIds = null) =>
        Merge(new Dictionary<string, object?>
        {
            ["kind"] = "expense",
            ["description"] = description,
            ["amount"] = amount,
            ["dayOfMonth"] = day,
            ["walletId"] = s.WalletId,
            ["startMonth"] = startMonth,
            ["tagIds"] = tagIds,
        }, extra);

    private static Dictionary<string, object?> Merge(Dictionary<string, object?> body, object? extra)
    {
        if (extra is not null)
        {
            foreach (var property in extra.GetType().GetProperties())
            {
                body[property.Name] = property.GetValue(extra);
            }
        }

        return body;
    }

    private async Task<List<Entry>> EntriesAsync(Space s, Guid recurrenceId) =>
        await WithDbAsync(Factory, s.Account, (db, _) =>
            db.Set<Entry>().AsNoTracking().Where(e => e.RecurrenceId == recurrenceId).OrderBy(e => e.RecurrenceMonth).ToListAsync());

    private async Task<List<(CardPurchase Purchase, DateOnly DueMonth)>> PurchasesAsync(Space s, Guid recurrenceId) =>
        await WithDbAsync(Factory, s.Account, async (db, _) =>
        {
            var rows = await db.Set<CardPurchase>().AsNoTracking()
                .Where(p => p.RecurrenceId == recurrenceId)
                .Join(db.Set<Invoice>().AsNoTracking(), p => p.InvoiceId, i => i.Id, (p, i) => new { Purchase = p, i.DueMonth })
                .OrderBy(r => r.Purchase.RecurrenceMonth)
                .ToListAsync();
            return rows.ConvertAll(r => (r.Purchase, r.DueMonth));
        });

    [Fact]
    public async Task Cria_saida_mensal_e_gera_22_ocorrencias_previstas_ate_dezembro_do_ano_seguinte()
    {
        var s = await NewSpaceAsync();

        var body = await OkAsync(await PostAsync(s, Saida(s, "Tim", "44,99", 10, "2026-03")), HttpStatusCode.Created);

        body.GetProperty("kind").GetString().ShouldBe("expense");
        body.GetProperty("amountCents").GetInt64().ShouldBe(4499);
        body.GetProperty("dayOfMonth").GetInt32().ShouldBe(10);
        body.GetProperty("walletId").GetGuid().ShouldBe(s.WalletId);
        body.GetProperty("startMonth").GetString().ShouldBe("2026-03");
        body.GetProperty("endType").GetString().ShouldBe("none");
        body.GetProperty("endMonth").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("referenceMonthRule").GetString().ShouldBe("same_month");
        body.GetProperty("generatedUntilMonth").GetString().ShouldBe("2027-12");
        body.GetProperty("status").GetString().ShouldBe("active");
        body.GetProperty("nextOccurrenceDate").GetString().ShouldBe("2026-03-10");
        body.GetProperty("needsReview").GetBoolean().ShouldBeFalse();
        body.GetProperty("reviewReason").ValueKind.ShouldBe(JsonValueKind.Null);

        var entries = await EntriesAsync(s, body.GetProperty("id").GetGuid());
        entries.Count.ShouldBe(22);
        entries.ShouldAllBe(e => e.Description == "Tim" && e.AmountCents == 4499 && e.Status == EntryStatus.Planned
            && e.Kind == EntryKind.Expense && e.WalletId == s.WalletId && !e.RecurrenceModified);
        entries[0].EntryDate.ShouldBe(new DateOnly(2026, 3, 10));
        entries[^1].EntryDate.ShouldBe(new DateOnly(2027, 12, 10));
        entries.Select(e => e.RecurrenceMonth).Distinct().Count().ShouldBe(22);
        entries.ShouldAllBe(e => e.ReferenceMonth == new DateOnly(e.EntryDate.Year, e.EntryDate.Month, 1));
    }

    [Fact]
    public async Task Compra_recorrente_gera_compras_a_vista_na_fatura_do_ciclo_do_cartao()
    {
        var s = await NewSpaceAsync();

        var body = await OkAsync(
            await PostAsync(s, new
            {
                kind = "card_purchase",
                description = "Prime Video",
                amount = "19,90",
                dayOfMonth = 2,
                creditCardId = s.CardId,
                startMonth = "2026-07",
            }),
            HttpStatusCode.Created);

        body.GetProperty("creditCardId").GetGuid().ShouldBe(s.CardId);
        body.GetProperty("walletId").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("referenceMonthRule").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("generatedUntilMonth").GetString().ShouldBe("2027-12");

        var purchases = await PurchasesAsync(s, body.GetProperty("id").GetGuid());
        purchases.Count.ShouldBe(18);
        purchases.ShouldAllBe(p => p.Purchase.Origin == PurchaseOrigin.Recurrence && p.Purchase.Nature == PurchaseNature.Debit
            && p.Purchase.AmountCents == 1990 && p.Purchase.InstallmentPlanId == null && p.Purchase.CreditCardId == s.CardId);
        purchases[0].Purchase.PurchaseDate.ShouldBe(new DateOnly(2026, 7, 2));
        purchases[0].DueMonth.ShouldBe(new DateOnly(2026, 7, 1));
        purchases[^1].Purchase.PurchaseDate.ShouldBe(new DateOnly(2027, 12, 2));
        purchases.ShouldAllBe(p => p.DueMonth == new DateOnly(p.Purchase.PurchaseDate.Year, p.Purchase.PurchaseDate.Month, 1));
    }

    [Fact]
    public async Task Compra_depois_do_fechamento_cai_na_fatura_seguinte()
    {
        var s = await NewSpaceAsync();

        var body = await OkAsync(
            await PostAsync(s, new
            {
                kind = "card_purchase",
                description = "WellHub",
                amount = "99,90",
                dayOfMonth = 20,
                creditCardId = s.CardId,
                startMonth = "2026-07",
                endType = "after_count",
                occurrenceLimit = 2,
            }),
            HttpStatusCode.Created);

        var purchases = await PurchasesAsync(s, body.GetProperty("id").GetGuid());
        purchases.Select(p => p.DueMonth).ShouldBe([new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1)]);
    }

    [Fact]
    public async Task Dia_31_vira_o_ultimo_dia_do_mes_mais_curto()
    {
        var s = await NewSpaceAsync();

        var body = await OkAsync(await PostAsync(s, Saida(s, "Seguro Vida", "25,99", 31, "2027-01")), HttpStatusCode.Created);

        var dates = (await EntriesAsync(s, body.GetProperty("id").GetGuid())).Select(e => e.EntryDate).ToList();
        dates.Count.ShouldBe(12);
        dates[0].ShouldBe(new DateOnly(2027, 1, 31));
        dates[1].ShouldBe(new DateOnly(2027, 2, 28));
        dates[3].ShouldBe(new DateOnly(2027, 4, 30));
    }

    [Fact]
    public async Task Termino_apos_6_ocorrencias_gera_exatamente_6()
    {
        var s = await NewSpaceAsync();

        var body = await OkAsync(
            await PostAsync(s, Saida(s, "Curso", "120", 8, "2026-03", new { endType = "after_count", occurrenceLimit = 6 })),
            HttpStatusCode.Created);

        body.GetProperty("endType").GetString().ShouldBe("after_count");
        body.GetProperty("occurrenceLimit").GetInt32().ShouldBe(6);
        body.GetProperty("endMonth").GetString().ShouldBe("2026-08");
        body.GetProperty("generatedUntilMonth").GetString().ShouldBe("2026-08");

        var entries = await EntriesAsync(s, body.GetProperty("id").GetGuid());
        entries.Select(e => e.RecurrenceMonth).ShouldBe(Enumerable.Range(3, 6).Select(m => (DateOnly?)new DateOnly(2026, m, 1)));
    }

    [Fact]
    public async Task Termino_ate_um_mes_nao_gera_depois_dele()
    {
        var s = await NewSpaceAsync();

        var body = await OkAsync(
            await PostAsync(s, Saida(s, "Seguro", "10", 5, "2026-03", new { endType = "until_month", endMonth = "2026-05" })),
            HttpStatusCode.Created);

        (await EntriesAsync(s, body.GetProperty("id").GetGuid())).Count.ShouldBe(3);
    }

    [Fact]
    public async Task Dia_ja_passado_no_mes_inicial_cria_a_ocorrencia_prevista_sem_marcar_como_paga()
    {
        var s = await NewSpaceAsync();

        // Hoje é 05/03: a ocorrência de 02/03 já passou, mas continua Prevista (aparece como Atrasada).
        var body = await OkAsync(await PostAsync(s, Saida(s, "Aluguel", "1500", 2, "2026-03")), HttpStatusCode.Created);

        var first = (await EntriesAsync(s, body.GetProperty("id").GetGuid()))[0];
        first.EntryDate.ShouldBe(new DateOnly(2026, 3, 2));
        first.Status.ShouldBe(EntryStatus.Planned);
        body.GetProperty("nextOccurrenceDate").GetString().ShouldBe("2026-04-02");
    }

    [Fact]
    public async Task Salario_com_mes_seguinte_tem_mes_de_referencia_um_mes_depois_da_data()
    {
        var s = await NewSpaceAsync();

        var body = await OkAsync(
            await PostAsync(s, new
            {
                kind = "income",
                description = "Salário",
                amount = "1200",
                dayOfMonth = 25,
                walletId = s.WalletId,
                startMonth = "2026-09",
                referenceMonthRule = "next_month",
            }),
            HttpStatusCode.Created);

        body.GetProperty("kind").GetString().ShouldBe("income");
        body.GetProperty("referenceMonthRule").GetString().ShouldBe("next_month");

        var entries = await EntriesAsync(s, body.GetProperty("id").GetGuid());
        entries[0].EntryDate.ShouldBe(new DateOnly(2026, 9, 25));
        entries[0].ReferenceMonth.ShouldBe(new DateOnly(2026, 10, 1));
        entries[1].EntryDate.ShouldBe(new DateOnly(2026, 10, 25));
        entries[1].ReferenceMonth.ShouldBe(new DateOnly(2026, 11, 1));
        entries[^1].EntryDate.ShouldBe(new DateOnly(2027, 12, 25));
        entries[^1].ReferenceMonth.ShouldBe(new DateOnly(2028, 1, 1));
        entries.ShouldAllBe(e => e.Kind == EntryKind.Income);
    }

    [Fact]
    public async Task Tags_da_recorrencia_vao_para_as_ocorrencias_com_a_de_acerto_na_coluna_propria()
    {
        var s = await NewSpaceAsync();
        var bruna = await TagAsync(s, "Bruna", isSettlement: true);
        var casa = await TagAsync(s, "Casa");

        var body = await OkAsync(
            await PostAsync(s, Saida(s, "Internet", "99", 12, "2026-03", new { endType = "after_count", occurrenceLimit = 2 }, [casa, bruna])),
            HttpStatusCode.Created);

        body.GetProperty("tagIds").EnumerateArray().Select(e => e.GetGuid()).ShouldBe([bruna, casa]);

        var id = body.GetProperty("id").GetGuid();
        var entries = await EntriesAsync(s, id);
        entries.ShouldAllBe(e => e.SettlementTagId == bruna);

        var links = await WithDbAsync(Factory, s.Account, (db, _) =>
            db.Set<FinMy.Domain.Tags.EntryTag>().AsNoTracking().Where(l => entries.Select(e => e.Id).Contains(l.EntryId)).Select(l => l.TagId).ToListAsync());
        links.ShouldBe([casa, casa]);
    }

    [Fact]
    public async Task Obtem_por_id_e_lista_com_filtro_de_situacao_e_total_mensal()
    {
        var s = await NewSpaceAsync();
        var tim = await OkAsync(await PostAsync(s, Saida(s, "Tim", "44,99", 10, "2026-03")), HttpStatusCode.Created);
        await OkAsync(
            await PostAsync(s, new
            {
                kind = "income",
                description = "Salário",
                amount = "1200",
                dayOfMonth = 25,
                walletId = s.WalletId,
                startMonth = "2026-03",
            }),
            HttpStatusCode.Created);
        await OkAsync(
            await PostAsync(s, new
            {
                kind = "card_purchase",
                description = "Prime Video",
                amount = "19,90",
                dayOfMonth = 2,
                creditCardId = s.CardId,
                startMonth = "2026-03",
            }),
            HttpStatusCode.Created);
        var antiga = await OkAsync(
            await PostAsync(s, Saida(s, "Curso antigo", "50", 1, "2025-10", new { endType = "until_month", endMonth = "2026-01" })),
            HttpStatusCode.Created);

        var one = await OkAsync(await s.Http.GetAsync($"{Url}/{tim.GetProperty("id").GetGuid()}"));
        one.GetProperty("description").GetString().ShouldBe("Tim");
        one.GetProperty("nextOccurrenceDate").GetString().ShouldBe("2026-03-10");

        var active = await OkAsync(await s.Http.GetAsync(Url));
        active.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("description").GetString()).ShouldBe(["Prime Video", "Salário", "Tim"]);
        active.GetProperty("monthlyIncomeCents").GetInt64().ShouldBe(120_000);
        active.GetProperty("monthlyExpenseCents").GetInt64().ShouldBe(4499 + 1990);

        var ended = await OkAsync(await s.Http.GetAsync($"{Url}?status=ended"));
        var endedItem = ended.GetProperty("items").EnumerateArray().Single();
        endedItem.GetProperty("id").GetGuid().ShouldBe(antiga.GetProperty("id").GetGuid());
        endedItem.GetProperty("status").GetString().ShouldBe("ended");
        endedItem.GetProperty("nextOccurrenceDate").ValueKind.ShouldBe(JsonValueKind.Null);
        ended.GetProperty("monthlyExpenseCents").GetInt64().ShouldBe(4499 + 1990);

        (await OkAsync(await s.Http.GetAsync($"{Url}?status=all"))).GetProperty("items").GetArrayLength().ShouldBe(4);
        (await s.Http.GetAsync($"{Url}?status=talvez")).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Recorrencia_de_outro_Espaco_nao_e_visivel()
    {
        var lia = await NewSpaceAsync();
        var leo = await NewSpaceAsync("Léo");
        var tim = await OkAsync(await PostAsync(lia, Saida(lia, "Tim", "44,99", 10, "2026-03")), HttpStatusCode.Created);

        (await leo.Http.GetAsync($"{Url}/{tim.GetProperty("id").GetGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await OkAsync(await leo.Http.GetAsync(Url))).GetProperty("items").GetArrayLength().ShouldBe(0);
        (await lia.Http.GetAsync($"{Url}/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Carteira_arquivada_no_cadastro_e_recusada_e_depois_aparece_para_revisao_sem_gerar_mais()
    {
        var s = await NewSpaceAsync();
        var cinza = await Factory.SeedWalletAsync(s.Account, "Cinza", archived: true);
        var recusada = await PostAsync(s, new Dictionary<string, object?>
        {
            ["kind"] = "expense",
            ["description"] = "Tim",
            ["amount"] = "44,99",
            ["dayOfMonth"] = 10,
            ["walletId"] = cinza,
            ["startMonth"] = "2026-03",
        });
        recusada.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await recusada.CodeAsync()).ShouldBe("wallet.archived");

        var verde = await CreateWalletAsync(s.Http, "Verde");
        var body = await OkAsync(await PostAsync(s, Saida(s, "Tim", "44,99", 10, "2026-03", new { walletId = verde.Id })), HttpStatusCode.Created);
        var id = body.GetProperty("id").GetGuid();
        (await s.Http.PostAsync($"/api/v1/wallets/{verde.Id}/archive", null)).EnsureSuccessStatusCode();

        var after = await OkAsync(await s.Http.GetAsync($"{Url}/{id}"));
        after.GetProperty("needsReview").GetBoolean().ShouldBeTrue();
        after.GetProperty("reviewReason").GetString().ShouldBe("wallet_archived");
        (await EntriesAsync(s, id)).Count.ShouldBe(22);
    }

    [Fact]
    public async Task Cartao_arquivado_no_cadastro_e_recusado_e_depois_aparece_para_revisao()
    {
        var s = await NewSpaceAsync();
        var body = await OkAsync(
            await PostAsync(s, new
            {
                kind = "card_purchase",
                description = "Prime Video",
                amount = "19,90",
                dayOfMonth = 2,
                creditCardId = s.CardId,
                startMonth = "2026-07",
            }),
            HttpStatusCode.Created);
        (await s.Http.PostAsync($"{CardsUrl}/{s.CardId}/archive", null)).EnsureSuccessStatusCode();

        var after = await OkAsync(await s.Http.GetAsync($"{Url}/{body.GetProperty("id").GetGuid()}"));
        after.GetProperty("needsReview").GetBoolean().ShouldBeTrue();
        after.GetProperty("reviewReason").GetString().ShouldBe("card_archived");

        var recusada = await PostAsync(s, new
        {
            kind = "card_purchase",
            description = "WellHub",
            amount = "99",
            dayOfMonth = 3,
            creditCardId = s.CardId,
            startMonth = "2026-07",
        });
        recusada.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await recusada.CodeAsync()).ShouldBe("credit_card.archived");
    }

    [Fact]
    public async Task Tag_arquivada_aparece_para_revisao()
    {
        var s = await NewSpaceAsync();
        var casa = await TagAsync(s, "Casa");
        var body = await OkAsync(await PostAsync(s, Saida(s, "Internet", "99", 12, "2026-03", tagIds: [casa])), HttpStatusCode.Created);
        (await s.Http.PostAsync($"/api/v1/tags/{casa}/archive", null)).EnsureSuccessStatusCode();

        var after = await OkAsync(await s.Http.GetAsync($"{Url}/{body.GetProperty("id").GetGuid()}"));
        after.GetProperty("needsReview").GetBoolean().ShouldBeTrue();
        after.GetProperty("reviewReason").GetString().ShouldBe("tag_archived");

        var recusada = await PostAsync(s, Saida(s, "Água", "50", 12, "2026-03", tagIds: [casa]));
        recusada.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Theory]
    [InlineData("kind.invalid", "talvez", "Tim", "44,99", 10, "2026-03", null)]
    [InlineData("validation_failed", "expense", "", "44,99", 10, "2026-03", null)]
    [InlineData("recurrence.day_invalid", "expense", "Tim", "44,99", 32, "2026-03", null)]
    [InlineData("recurrence.day_invalid", "expense", "Tim", "44,99", 0, "2026-03", null)]
    [InlineData("startMonth.invalid", "expense", "Tim", "44,99", 10, "março", null)]
    [InlineData("recurrence.end_month_required", "expense", "Tim", "44,99", 10, "2026-03", "until_month")]
    [InlineData("recurrence.limit_invalid", "expense", "Tim", "44,99", 10, "2026-03", "after_count")]
    public async Task Recusa_entrada_invalida_com_codigo_estavel(string code, string kind, string description, string amount, int day, string start, string? endType)
    {
        var s = await NewSpaceAsync();

        var response = await PostAsync(s, new
        {
            kind,
            description,
            amount,
            dayOfMonth = day,
            walletId = s.WalletId,
            startMonth = start,
            endType,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, await response.Content.ReadAsStringAsync());
        var actual = await response.CodeAsync();
        if (code == "validation_failed")
        {
            actual.ShouldNotBeNull();
        }
        else
        {
            actual.ShouldBe(code);
        }
    }

    [Fact]
    public async Task Recusa_combinacoes_invalidas_de_carteira_cartao_e_regra_de_mes()
    {
        var s = await NewSpaceAsync();

        var semCarteira = await PostAsync(s, new { kind = "expense", description = "Tim", amount = "44,99", dayOfMonth = 10, startMonth = "2026-03" });
        (await semCarteira.CodeAsync()).ShouldBe("recurrence.wallet_required");

        var compraComCarteira = await PostAsync(s, new
        {
            kind = "card_purchase",
            description = "Tim",
            amount = "44,99",
            dayOfMonth = 10,
            creditCardId = s.CardId,
            walletId = s.WalletId,
            startMonth = "2026-03",
        });
        (await compraComCarteira.CodeAsync()).ShouldBe("recurrence.wallet_not_allowed");

        var compraComRegra = await PostAsync(s, new
        {
            kind = "card_purchase",
            description = "Tim",
            amount = "44,99",
            dayOfMonth = 10,
            creditCardId = s.CardId,
            startMonth = "2026-03",
            referenceMonthRule = "next_month",
        });
        (await compraComRegra.CodeAsync()).ShouldBe("recurrence.reference_rule_not_allowed");

        var saidaComCartao = await PostAsync(s, new
        {
            kind = "expense",
            description = "Tim",
            amount = "44,99",
            dayOfMonth = 10,
            creditCardId = s.CardId,
            walletId = s.WalletId,
            startMonth = "2026-03",
        });
        (await saidaComCartao.CodeAsync()).ShouldBe("recurrence.card_not_allowed");

        var carteiraInexistente = await PostAsync(s, new
        {
            kind = "expense",
            description = "Tim",
            amount = "44,99",
            dayOfMonth = 10,
            walletId = Guid.NewGuid(),
            startMonth = "2026-03",
        });
        carteiraInexistente.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await OkAsync(await s.Http.GetAsync(Url))).GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Repetir_a_mesma_chave_de_idempotencia_nao_cria_outra_recorrencia()
    {
        var s = await NewSpaceAsync();
        var key = Guid.NewGuid().ToString();
        HttpRequestMessage Request() => new(HttpMethod.Post, Url)
        {
            Content = JsonContent.Create(Saida(s, "Tim", "44,99", 10, "2026-03")),
            Headers = { { "Idempotency-Key", key } },
        };

        var first = await s.Http.SendAsync(Request());
        var second = await s.Http.SendAsync(Request());

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await first.JsonAsync()).GetProperty("id").GetGuid().ShouldBe((await second.JsonAsync()).GetProperty("id").GetGuid());
        (await OkAsync(await s.Http.GetAsync(Url))).GetProperty("items").GetArrayLength().ShouldBe(1);
    }

    private static async Task<Guid> TagAsync(Space s, string name, bool isSettlement = false)
    {
        var response = await s.Http.PostAsJsonAsync("/api/v1/tags", new { name, isSettlement });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }
}
