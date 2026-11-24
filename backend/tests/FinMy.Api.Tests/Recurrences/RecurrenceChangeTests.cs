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
using Microsoft.EntityFrameworkCore;
using static FinMy.Api.Tests.Cards.CardTestSupport;

namespace FinMy.Api.Tests.Recurrences;

/// <summary>
/// "Alterada" em <c>PUT /entries</c> e <c>PUT /card-purchases</c>, <c>PUT /recurrences/{id}</c> com <c>fromMonth</c>,
/// <c>POST /recurrences/{id}/end</c> e <c>DELETE /recurrences/{id}</c> (005 US2.2, US3.1–US3.4). "Hoje" é 05/03/2026; dados
/// fictícios (carteira Azul, cartão Azul fecha dia 18 e vence dia 25).
/// </summary>
public class RecurrenceChangeTests(PostgresFixture postgres) : ApiTestBase(postgres)
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

    private static async Task<string> CodeOfAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync());
        return (await response.JsonAsync()).GetProperty("code").GetString()!;
    }

    private static string Brl(long cents) => $"{cents / 100},{cents % 100:00}";

    private static async Task<Guid> TagAsync(Space s, string name, bool isSettlement = false)
    {
        var response = await s.Http.PostAsJsonAsync("/api/v1/tags", new { name, isSettlement });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateSaidaAsync(
        Space s, string description, string amount, int day, string start, object? extra = null, Guid[]? tagIds = null, string kind = "expense")
    {
        var body = new Dictionary<string, object?>
        {
            ["kind"] = kind,
            ["description"] = description,
            ["amount"] = amount,
            ["dayOfMonth"] = day,
            ["walletId"] = s.WalletId,
            ["startMonth"] = start,
            ["tagIds"] = tagIds,
        };
        Apply(body, extra);
        return (await OkAsync(await s.Http.PostAsJsonAsync(Url, body), HttpStatusCode.Created)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateCompraAsync(Space s, string description, string amount, int day, string start, object? extra = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["kind"] = "card_purchase",
            ["description"] = description,
            ["amount"] = amount,
            ["dayOfMonth"] = day,
            ["creditCardId"] = s.CardId,
            ["startMonth"] = start,
        };
        Apply(body, extra);
        return (await OkAsync(await s.Http.PostAsJsonAsync(Url, body), HttpStatusCode.Created)).GetProperty("id").GetGuid();
    }

    private static void Apply(Dictionary<string, object?> body, object? extra)
    {
        if (extra is null)
        {
            return;
        }

        if (extra is IDictionary<string, object?> values)
        {
            foreach (var (key, value) in values)
            {
                body[key] = value;
            }

            return;
        }

        foreach (var property in extra.GetType().GetProperties())
        {
            body[property.Name] = property.GetValue(extra);
        }
    }

    /// <summary>Corpo de <c>PUT /recurrences/{id}</c> a partir do estado atual, com as alterações pedidas.</summary>
    private static async Task<Dictionary<string, object?>> UpdateBodyAsync(Space s, Guid id, object? changes)
    {
        var current = await OkAsync(await s.Http.GetAsync($"{Url}/{id}"));
        var body = new Dictionary<string, object?>
        {
            ["kind"] = current.GetProperty("kind").GetString(),
            ["description"] = current.GetProperty("description").GetString(),
            ["amount"] = Brl(current.GetProperty("amountCents").GetInt64()),
            ["dayOfMonth"] = current.GetProperty("dayOfMonth").GetInt32(),
            ["walletId"] = current.GetProperty("walletId").ValueKind == JsonValueKind.Null ? null : current.GetProperty("walletId").GetGuid(),
            ["creditCardId"] = current.GetProperty("creditCardId").ValueKind == JsonValueKind.Null ? null : current.GetProperty("creditCardId").GetGuid(),
            ["tagIds"] = current.GetProperty("tagIds").EnumerateArray().Select(e => e.GetGuid()).ToArray(),
            ["endType"] = current.GetProperty("endType").GetString(),
            ["endMonth"] = current.GetProperty("endMonth").GetString(),
            ["occurrenceLimit"] = current.GetProperty("occurrenceLimit").ValueKind == JsonValueKind.Null ? null : current.GetProperty("occurrenceLimit").GetInt32(),
            ["referenceMonthRule"] = current.GetProperty("referenceMonthRule").GetString(),
            ["version"] = current.GetProperty("version").GetInt64(),
        };
        Apply(body, changes);
        return body;
    }

    private static async Task<HttpResponseMessage> PutRecurrenceAsync(Space s, Guid id, object? changes) =>
        await s.Http.PutAsJsonAsync($"{Url}/{id}", await UpdateBodyAsync(s, id, changes));

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

    private async Task<List<Entry>> EntriesByDescriptionAsync(Space s, string description) =>
        await WithDbAsync(Factory, s.Account, (db, _) =>
            db.Set<Entry>().AsNoTracking().Where(e => e.Description == description).OrderBy(e => e.EntryDate).ToListAsync());

    private static string Ym(DateOnly? date) => YearMonth.From(date!.Value).ToString();

    private static Entry InMonth(List<Entry> entries, string month) =>
        entries.Single(e => e.RecurrenceMonth == YearMonth.Parse(month).FirstDay);

    private static async Task SettleAsync(Space s, Guid entryId) =>
        (await s.Http.PatchAsJsonAsync($"/api/v1/entries/{entryId}/status", new { status = "settled" })).EnsureSuccessStatusCode();

    private static async Task<HttpResponseMessage> PutEntryAsync(Space s, Guid entryId, object? changes)
    {
        var current = await OkAsync(await s.Http.GetAsync($"/api/v1/entries/{entryId}"));
        var body = new Dictionary<string, object?>
        {
            ["kind"] = current.GetProperty("kind").GetString(),
            ["description"] = current.GetProperty("description").GetString(),
            ["amount"] = Brl(current.GetProperty("amountCents").GetInt64()),
            ["date"] = current.GetProperty("date").GetString(),
            ["walletId"] = current.GetProperty("walletId").GetGuid(),
            ["status"] = current.GetProperty("status").GetString(),
            ["referenceMonth"] = current.GetProperty("referenceMonth").GetString(),
            ["notes"] = current.GetProperty("notes").GetString(),
            ["tagIds"] = current.GetProperty("tagIds").EnumerateArray().Select(e => e.GetGuid()).ToArray(),
            ["version"] = current.GetProperty("version").GetInt64(),
        };
        Apply(body, changes);
        return await s.Http.PutAsJsonAsync($"/api/v1/entries/{entryId}", body);
    }

    private static async Task<HttpResponseMessage> PutPurchaseAsync(Space s, Guid purchaseId, object? changes)
    {
        var current = await OkAsync(await s.Http.GetAsync($"/api/v1/card-purchases/{purchaseId}"));
        var body = new Dictionary<string, object?>
        {
            ["description"] = current.GetProperty("description").GetString(),
            ["amount"] = Brl(current.GetProperty("amountCents").GetInt64()),
            ["nature"] = current.GetProperty("nature").GetString(),
            ["date"] = current.GetProperty("date").GetString(),
            ["creditCardId"] = current.GetProperty("creditCardId").GetGuid(),
            ["notes"] = current.GetProperty("notes").GetString(),
            ["tagIds"] = current.GetProperty("tagIds").EnumerateArray().Select(e => e.GetGuid()).ToArray(),
            ["version"] = current.GetProperty("version").GetInt64(),
        };
        Apply(body, changes);
        return await s.Http.PutAsJsonAsync($"/api/v1/card-purchases/{purchaseId}", body);
    }

    // ---- "alterada" (US2.2, FR-005, research R3) ----

    [Fact]
    public async Task Editar_o_valor_de_uma_ocorrencia_marca_so_ela_como_alterada()
    {
        var s = await NewSpaceAsync();
        var id = await CreateSaidaAsync(s, "Salário", "1200,00", 25, "2026-09", new { referenceMonthRule = "next_month" }, kind: "income");
        var outubro = InMonth(await EntriesAsync(s, id), "2026-10");
        outubro.RecurrenceModified.ShouldBeFalse();

        var body = await OkAsync(await PutEntryAsync(s, outubro.Id, new { amount = "1250,00" }));

        body.GetProperty("amountCents").GetInt64().ShouldBe(125000);
        body.GetProperty("recurrenceId").GetGuid().ShouldBe(id);
        body.GetProperty("recurrenceMonth").GetString().ShouldBe("2026-10");
        body.GetProperty("recurrenceModified").GetBoolean().ShouldBeTrue();

        var entries = await EntriesAsync(s, id);
        entries.Where(e => e.RecurrenceModified).Select(e => e.Id).ShouldBe([outubro.Id]);
        entries.Where(e => e.Id != outubro.Id).ShouldAllBe(e => e.AmountCents == 120000);
    }

    [Theory]
    [InlineData("description", "Salário da Lia")]
    [InlineData("date", "2026-10-26")]
    [InlineData("referenceMonth", "2026-12")]
    [InlineData("notes", "Reajuste")]
    public async Task Qualquer_campo_do_lancamento_alem_da_situacao_marca_como_alterada(string field, string value)
    {
        var s = await NewSpaceAsync();
        var id = await CreateSaidaAsync(s, "Salário", "1200,00", 25, "2026-09", new { referenceMonthRule = "next_month" }, kind: "income");
        var outubro = InMonth(await EntriesAsync(s, id), "2026-10");

        var body = await OkAsync(await PutEntryAsync(s, outubro.Id, new Dictionary<string, object?> { [field] = value }));

        body.GetProperty("recurrenceModified").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Trocar_carteira_ou_tags_da_ocorrencia_marca_como_alterada()
    {
        var s = await NewSpaceAsync();
        var verde = await CreateWalletAsync(s.Http, "Verde");
        var casa = await TagAsync(s, "Casa");
        var id = await CreateSaidaAsync(s, "Tim", "44,99", 10, "2026-03", new { endType = "after_count", occurrenceLimit = 3 });
        var entries = await EntriesAsync(s, id);

        (await OkAsync(await PutEntryAsync(s, InMonth(entries, "2026-03").Id, new { walletId = verde.Id }))).GetProperty("recurrenceModified").GetBoolean().ShouldBeTrue();
        (await OkAsync(await PutEntryAsync(s, InMonth(entries, "2026-04").Id, new { tagIds = new[] { casa } }))).GetProperty("recurrenceModified").GetBoolean().ShouldBeTrue();
        (await OkAsync(await GetEntryAsync(s, InMonth(entries, "2026-05").Id))).GetProperty("recurrenceModified").GetBoolean().ShouldBeFalse();
    }

    private static Task<HttpResponseMessage> GetEntryAsync(Space s, Guid id) => s.Http.GetAsync($"/api/v1/entries/{id}");

    [Fact]
    public async Task Situacao_e_salvar_sem_mudar_nada_nao_marcam_como_alterada()
    {
        var s = await NewSpaceAsync();
        var id = await CreateSaidaAsync(s, "Tim", "44,99", 10, "2026-03", new { endType = "after_count", occurrenceLimit = 3 });
        var entries = await EntriesAsync(s, id);

        // PATCH de situação nunca marca.
        await SettleAsync(s, InMonth(entries, "2026-03").Id);
        // PUT que só muda a situação também não.
        (await OkAsync(await PutEntryAsync(s, InMonth(entries, "2026-04").Id, new { status = "settled" }))).GetProperty("recurrenceModified").GetBoolean().ShouldBeFalse();
        // PUT com os mesmos valores (descrição com espaços, valor reescrito) também não.
        (await OkAsync(await PutEntryAsync(s, InMonth(entries, "2026-05").Id, new { description = "  Tim ", amount = "44,990" }))).GetProperty("recurrenceModified").GetBoolean().ShouldBeFalse();

        (await EntriesAsync(s, id)).ShouldAllBe(e => !e.RecurrenceModified);
    }

    [Fact]
    public async Task Editar_lancamento_comum_nao_ganha_vinculo_nem_marca()
    {
        var s = await NewSpaceAsync();
        var created = await OkAsync(
            await s.Http.PostAsJsonAsync("/api/v1/entries", new { kind = "expense", description = "Mercado", amount = "50,00", date = "2026-03-02", walletId = s.WalletId }),
            HttpStatusCode.Created);

        var body = await OkAsync(await PutEntryAsync(s, created.GetProperty("id").GetGuid(), new { amount = "60,00" }));

        body.GetProperty("recurrenceId").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("recurrenceMonth").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("recurrenceModified").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Compra_recorrente_editada_fica_alterada_mas_conferida_nao_marca()
    {
        var s = await NewSpaceAsync();
        var id = await CreateCompraAsync(s, "Prime Video", "19,90", 2, "2026-03", new { endType = "after_count", occurrenceLimit = 3 });
        var purchases = await PurchasesAsync(s, id);
        purchases.ShouldAllBe(p => p.Purchase.Origin == PurchaseOrigin.Recurrence);

        var conferida = await OkAsync(await s.Http.PatchAsJsonAsync($"/api/v1/card-purchases/{purchases[0].Purchase.Id}/reconciled", new { reconciled = true }));
        conferida.GetProperty("recurrenceModified").GetBoolean().ShouldBeFalse();
        conferida.GetProperty("recurrenceId").GetGuid().ShouldBe(id);
        conferida.GetProperty("recurrenceMonth").GetString().ShouldBe("2026-03");
        conferida.GetProperty("origin").GetString().ShouldBe("recurrence");

        var igual = await OkAsync(await PutPurchaseAsync(s, purchases[1].Purchase.Id, new { description = " Prime Video " }));
        igual.GetProperty("recurrenceModified").GetBoolean().ShouldBeFalse();

        var editada = await OkAsync(await PutPurchaseAsync(s, purchases[2].Purchase.Id, new { amount = "24,90" }));
        editada.GetProperty("recurrenceModified").GetBoolean().ShouldBeTrue();

        (await PurchasesAsync(s, id)).Where(p => p.Purchase.RecurrenceModified).Select(p => p.Purchase.Id).ShouldBe([purchases[2].Purchase.Id]);
    }

    // ---- Alterar a partir de um mês (US3.1, US3.3, US3.4, FR-006) ----

    [Fact]
    public async Task Altera_o_valor_a_partir_de_um_mes_so_nas_ocorrencias_previstas_e_nao_alteradas()
    {
        var s = await NewSpaceAsync();
        var id = await CreateSaidaAsync(s, "Tim", "44,99", 10, "2026-03");
        var inicio = await EntriesAsync(s, id);
        await SettleAsync(s, InMonth(inicio, "2026-08").Id); // Paga
        (await OkAsync(await PutEntryAsync(s, InMonth(inicio, "2026-11").Id, new { amount = "45,50" }))).GetProperty("recurrenceModified").GetBoolean().ShouldBeTrue();

        var body = await OkAsync(await PutRecurrenceAsync(s, id, new { amount = "50,99", fromMonth = "2026-09" }));

        body.GetProperty("amountCents").GetInt64().ShouldBe(5099);
        body.GetProperty("affectedCount").GetInt32().ShouldBe(15); // 2026-09 a 2027-12 (16 meses) menos a alterada
        var entries = await EntriesAsync(s, id);
        entries.Count.ShouldBe(22);
        entries.Where(e => e.RecurrenceMonth < new DateOnly(2026, 9, 1)).ShouldAllBe(e => e.AmountCents == 4499); // inclui a Paga
        InMonth(entries, "2026-08").Status.ShouldBe(EntryStatus.Settled);
        InMonth(entries, "2026-11").AmountCents.ShouldBe(4550);
        InMonth(entries, "2026-11").RecurrenceModified.ShouldBeTrue();
        entries.Where(e => e.RecurrenceMonth >= new DateOnly(2026, 9, 1) && e.Id != InMonth(entries, "2026-11").Id)
            .ShouldAllBe(e => e.AmountCents == 5099 && !e.RecurrenceModified && e.Status == EntryStatus.Planned);
    }

    [Fact]
    public async Task Sem_from_month_altera_a_partir_do_mes_de_hoje()
    {
        var s = await NewSpaceAsync();
        var id = await CreateSaidaAsync(s, "Tim", "44,99", 10, "2026-01");

        var body = await OkAsync(await PutRecurrenceAsync(s, id, new { amount = "50,99" }));

        body.GetProperty("affectedCount").GetInt32().ShouldBe(22); // 2026-03 a 2027-12
        var entries = await EntriesAsync(s, id);
        entries.Where(e => e.RecurrenceMonth < new DateOnly(2026, 3, 1)).ShouldAllBe(e => e.AmountCents == 4499);
        entries.Where(e => e.RecurrenceMonth >= new DateOnly(2026, 3, 1)).ShouldAllBe(e => e.AmountCents == 5099);
    }

    [Fact]
    public async Task Novo_dia_e_nova_regra_de_mes_mudam_data_e_mes_de_referencia_das_elegiveis()
    {
        var s = await NewSpaceAsync();
        var id = await CreateSaidaAsync(s, "Salário", "1200,00", 25, "2026-03", new { endType = "after_count", occurrenceLimit = 4 }, kind: "income");

        await OkAsync(await PutRecurrenceAsync(s, id, new { dayOfMonth = 31, referenceMonthRule = "next_month", fromMonth = "2026-04" }));

        var entries = await EntriesAsync(s, id);
        InMonth(entries, "2026-03").EntryDate.ShouldBe(new DateOnly(2026, 3, 25));
        InMonth(entries, "2026-03").ReferenceMonth.ShouldBe(new DateOnly(2026, 3, 1));
        InMonth(entries, "2026-04").EntryDate.ShouldBe(new DateOnly(2026, 4, 30)); // RT-03: abril tem 30 dias
        InMonth(entries, "2026-04").ReferenceMonth.ShouldBe(new DateOnly(2026, 5, 1));
        InMonth(entries, "2026-06").EntryDate.ShouldBe(new DateOnly(2026, 6, 30));
        InMonth(entries, "2026-06").ReferenceMonth.ShouldBe(new DateOnly(2026, 7, 1));
    }

    [Fact]
    public async Task Altera_carteira_e_tags_das_ocorrencias_elegiveis()
    {
        var s = await NewSpaceAsync();
        var verde = await CreateWalletAsync(s.Http, "Verde");
        var casa = await TagAsync(s, "Casa");
        var leo = await TagAsync(s, "Léo", isSettlement: true);
        var id = await CreateSaidaAsync(s, "Internet", "99,00", 12, "2026-03", new { endType = "after_count", occurrenceLimit = 3 }, [casa]);

        var body = await OkAsync(await PutRecurrenceAsync(s, id, new { walletId = verde.Id, tagIds = new[] { leo }, fromMonth = "2026-04" }));

        body.GetProperty("walletId").GetGuid().ShouldBe(verde.Id);
        body.GetProperty("tagIds").EnumerateArray().Select(e => e.GetGuid()).ShouldBe([leo]);
        var entries = await EntriesAsync(s, id);
        InMonth(entries, "2026-03").WalletId.ShouldBe(s.WalletId);
        InMonth(entries, "2026-03").SettlementTagId.ShouldBeNull();
        entries.Where(e => e.RecurrenceMonth >= new DateOnly(2026, 4, 1)).ShouldAllBe(e => e.WalletId == verde.Id && e.SettlementTagId == leo);

        var links = await WithDbAsync(Factory, s.Account, (db, _) =>
            db.Set<FinMy.Domain.Tags.EntryTag>().AsNoTracking().Where(l => entries.Select(e => e.Id).Contains(l.EntryId)).Select(l => new { l.EntryId, l.TagId }).ToListAsync());
        links.Select(l => l.EntryId).ShouldBe([InMonth(entries, "2026-03").Id]);
        links.ShouldAllBe(l => l.TagId == casa);
    }

    [Fact]
    public async Task Ocorrencia_excluida_isoladamente_nao_volta_ao_alterar_a_recorrencia()
    {
        var s = await NewSpaceAsync();
        var id = await CreateSaidaAsync(s, "Tim", "44,99", 10, "2026-03");
        var dezembro = InMonth(await EntriesAsync(s, id), "2026-12");
        (await s.Http.DeleteAsync($"/api/v1/entries/{dezembro.Id}")).EnsureSuccessStatusCode();

        var body = await OkAsync(await PutRecurrenceAsync(s, id, new { amount = "50,99", fromMonth = "2026-03" }));

        body.GetProperty("affectedCount").GetInt32().ShouldBe(21);
        var entries = await EntriesAsync(s, id);
        entries.Count.ShouldBe(21);
        entries.ShouldNotContain(e => e.RecurrenceMonth == new DateOnly(2026, 12, 1));
        entries.ShouldAllBe(e => e.AmountCents == 5099);
    }

    [Fact]
    public async Task Compra_recorrente_com_dia_novo_recalcula_a_fatura_e_preserva_as_de_fatura_fechada()
    {
        var s = await NewSpaceAsync();
        var id = await CreateCompraAsync(s, "Prime Video", "19,90", 2, "2026-01", new { endType = "after_count", occurrenceLimit = 4 });
        (await PurchasesAsync(s, id)).Select(p => p.DueMonth).ShouldBe(
            [new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 1), new DateOnly(2026, 4, 1)]);

        // Hoje é 05/03: as faturas de janeiro e fevereiro já fecharam (dia 18); a de março fecha em 18/03.
        var body = await OkAsync(await PutRecurrenceAsync(s, id, new { amount = "24,90", dayOfMonth = 20, description = "Prime Video Plus" }));

        body.GetProperty("affectedCount").GetInt32().ShouldBe(2);
        var purchases = await PurchasesAsync(s, id);
        purchases.Take(2).ShouldAllBe(p => p.Purchase.AmountCents == 1990 && p.Purchase.Description == "Prime Video" && p.Purchase.PurchaseDate.Day == 2);
        purchases.Skip(2).ShouldAllBe(p => p.Purchase.AmountCents == 2490 && p.Purchase.Description == "Prime Video Plus" && p.Purchase.PurchaseDate.Day == 20 && !p.Purchase.RecurrenceModified);
        // Dia 20 passou do fechamento (dia 18): cai na fatura do mês seguinte.
        purchases.Skip(2).Select(p => p.DueMonth).ShouldBe([new DateOnly(2026, 4, 1), new DateOnly(2026, 5, 1)]);
    }

    [Fact]
    public async Task Compra_alterada_a_mao_nao_muda_com_a_recorrencia()
    {
        var s = await NewSpaceAsync();
        var id = await CreateCompraAsync(s, "Prime Video", "19,90", 2, "2026-03", new { endType = "after_count", occurrenceLimit = 3 });
        var antes = await PurchasesAsync(s, id);
        await OkAsync(await PutPurchaseAsync(s, antes[1].Purchase.Id, new { amount = "21,00" }));

        var body = await OkAsync(await PutRecurrenceAsync(s, id, new { amount = "25,00", fromMonth = "2026-03" }));

        body.GetProperty("affectedCount").GetInt32().ShouldBe(2);
        var purchases = await PurchasesAsync(s, id);
        purchases.Select(p => p.Purchase.AmountCents).ShouldBe([2500, 2100, 2500]);
    }

    [Fact]
    public async Task Encolher_o_termino_exclui_as_elegiveis_depois_dele_e_estender_gera_as_que_faltam()
    {
        var s = await NewSpaceAsync();
        var id = await CreateSaidaAsync(s, "Tim", "44,99", 10, "2026-03");
        var inicio = await EntriesAsync(s, id);
        await SettleAsync(s, InMonth(inicio, "2026-12").Id); // Paga, depois do novo término: fica

        var encolhida = await OkAsync(await PutRecurrenceAsync(s, id, new { endType = "until_month", endMonth = "2026-10", fromMonth = "2026-03" }));

        encolhida.GetProperty("endMonth").GetString().ShouldBe("2026-10");
        var entries = await EntriesAsync(s, id);
        entries.Select(e => Ym(e.RecurrenceMonth)).ShouldBe(
            ["2026-03", "2026-04", "2026-05", "2026-06", "2026-07", "2026-08", "2026-09", "2026-10", "2026-12"]);
        InMonth(entries, "2026-12").Status.ShouldBe(EntryStatus.Settled);

        // Recorrência que ainda não tinha gerado tudo: remover o término gera os meses que faltam.
        var curta = await CreateSaidaAsync(s, "WellHub", "99,90", 5, "2026-03", new { endType = "until_month", endMonth = "2026-06" });
        (await EntriesAsync(s, curta)).Count.ShouldBe(4);

        var estendida = await OkAsync(await PutRecurrenceAsync(s, curta, new { endType = "none", endMonth = (string?)null }));

        estendida.GetProperty("generatedUntilMonth").GetString().ShouldBe("2027-12");
        (await EntriesAsync(s, curta)).Count.ShouldBe(22);
    }

    [Fact]
    public async Task Recusa_alteracao_invalida_com_codigo_estavel()
    {
        var s = await NewSpaceAsync();
        var id = await CreateSaidaAsync(s, "Tim", "44,99", 10, "2026-03");
        var cinza = await Factory.SeedWalletAsync(s.Account, "Cinza", archived: true);

        (await CodeOfAsync(await PutRecurrenceAsync(s, id, new { kind = "income" }), HttpStatusCode.UnprocessableEntity)).ShouldBe("recurrence.kind_immutable");
        (await CodeOfAsync(await PutRecurrenceAsync(s, id, new { fromMonth = "2026-02" }), HttpStatusCode.UnprocessableEntity)).ShouldBe("recurrence.from_month_before_start");
        (await CodeOfAsync(await PutRecurrenceAsync(s, id, new { walletId = cinza }), HttpStatusCode.UnprocessableEntity)).ShouldBe("wallet.archived");
        (await CodeOfAsync(await PutRecurrenceAsync(s, id, new { dayOfMonth = 32 }), HttpStatusCode.UnprocessableEntity)).ShouldBe("recurrence.day_invalid");
        (await CodeOfAsync(await PutRecurrenceAsync(s, id, new { version = (long?)null }), HttpStatusCode.UnprocessableEntity)).ShouldBe("version.required");
        (await CodeOfAsync(await PutRecurrenceAsync(s, id, new { fromMonth = "março" }), HttpStatusCode.UnprocessableEntity)).ShouldBe("fromMonth.invalid");

        // Nada mudou.
        (await EntriesAsync(s, id)).ShouldAllBe(e => e.AmountCents == 4499);
    }

    [Fact]
    public async Task Versao_desatualizada_da_recorrencia_e_conflito()
    {
        var s = await NewSpaceAsync();
        var id = await CreateSaidaAsync(s, "Tim", "44,99", 10, "2026-03", new { endType = "after_count", occurrenceLimit = 3 });
        var velho = await UpdateBodyAsync(s, id, new { amount = "50,99" });
        (await s.Http.PutAsJsonAsync($"{Url}/{id}", velho)).EnsureSuccessStatusCode();

        (await CodeOfAsync(await s.Http.PutAsJsonAsync($"{Url}/{id}", velho), HttpStatusCode.Conflict)).ShouldBe("concurrency_conflict");
    }

    // ---- Encerrar (US3.2, US3.4, FR-007) ----

    [Fact]
    public async Task Encerrar_a_partir_de_um_mes_exclui_so_as_elegiveis_e_nao_cria_mais()
    {
        var s = await NewSpaceAsync();
        var id = await CreateSaidaAsync(s, "WellHub", "99,90", 5, "2026-03");
        var inicio = await EntriesAsync(s, id);
        await SettleAsync(s, InMonth(inicio, "2026-11").Id); // Paga
        await OkAsync(await PutEntryAsync(s, InMonth(inicio, "2027-02").Id, new { amount = "89,90" })); // alterada

        var body = await OkAsync(await s.Http.PostAsJsonAsync($"{Url}/{id}/end", new { fromMonth = "2026-10" }));

        // 2026-10 a 2027-12 são 15 meses; ficam a Paga e a alterada.
        body.GetProperty("deletedCount").GetInt32().ShouldBe(13);
        body.GetProperty("deletionBatchId").GetGuid().ShouldNotBe(Guid.Empty);
        body.GetProperty("recurrence").GetProperty("endType").GetString().ShouldBe("until_month");
        body.GetProperty("recurrence").GetProperty("endMonth").GetString().ShouldBe("2026-09");
        body.GetProperty("recurrence").GetProperty("status").GetString().ShouldBe("active");

        var entries = await EntriesAsync(s, id);
        entries.Select(e => Ym(e.RecurrenceMonth)).ShouldBe(
            ["2026-03", "2026-04", "2026-05", "2026-06", "2026-07", "2026-08", "2026-09", "2026-11", "2027-02"]);
        InMonth(entries, "2026-11").Status.ShouldBe(EntryStatus.Settled);
        InMonth(entries, "2027-02").AmountCents.ShouldBe(8990);

        // Nenhuma nova ocorrência é criada ao alterar depois.
        await OkAsync(await PutRecurrenceAsync(s, id, new { amount = "109,90" }));
        (await EntriesAsync(s, id)).Count.ShouldBe(9);
    }

    [Fact]
    public async Task Encerrar_compra_recorrente_preserva_as_de_fatura_fechada()
    {
        var s = await NewSpaceAsync();
        var id = await CreateCompraAsync(s, "Prime Video", "19,90", 2, "2026-01", new { endType = "after_count", occurrenceLimit = 6 });

        var body = await OkAsync(await s.Http.PostAsJsonAsync($"{Url}/{id}/end", new { fromMonth = "2026-02" }));

        // Fevereiro tem a fatura fechada em 18/02: fica; março a junho saem.
        body.GetProperty("deletedCount").GetInt32().ShouldBe(4);
        (await PurchasesAsync(s, id)).Select(p => Ym(p.Purchase.RecurrenceMonth)).ShouldBe(["2026-01", "2026-02"]);
    }

    [Fact]
    public async Task Encerrar_recusa_recorrencia_ja_encerrada_e_mes_inicial()
    {
        var s = await NewSpaceAsync();
        var id = await CreateSaidaAsync(s, "WellHub", "99,90", 5, "2026-03");

        (await CodeOfAsync(await s.Http.PostAsJsonAsync($"{Url}/{id}/end", new { fromMonth = "2026-03" }), HttpStatusCode.UnprocessableEntity)).ShouldBe("recurrence.end_before_start");
        await OkAsync(await s.Http.PostAsJsonAsync($"{Url}/{id}/end", new { fromMonth = "2026-06" }));

        (await CodeOfAsync(await s.Http.PostAsJsonAsync($"{Url}/{id}/end", new { fromMonth = "2026-08" }), HttpStatusCode.UnprocessableEntity)).ShouldBe("recurrence.already_ended");

        var passada = await CreateSaidaAsync(s, "Seguro Vida", "25,99", 25, "2026-01", new { endType = "until_month", endMonth = "2026-02" });
        (await CodeOfAsync(await s.Http.PostAsJsonAsync($"{Url}/{passada}/end", new { fromMonth = "2026-02" }), HttpStatusCode.UnprocessableEntity)).ShouldBe("recurrence.already_ended");
    }

    // ---- Excluir (FR-007) ----

    [Fact]
    public async Task Excluir_a_recorrencia_exclui_as_elegiveis_e_deixa_as_demais_como_lancamentos_comuns()
    {
        var s = await NewSpaceAsync();
        var id = await CreateSaidaAsync(s, "Prime Video", "19,90", 2, "2026-01", new { endType = "after_count", occurrenceLimit = 8 });
        var inicio = await EntriesAsync(s, id);
        await SettleAsync(s, InMonth(inicio, "2026-05").Id); // Paga, no futuro
        await OkAsync(await PutEntryAsync(s, InMonth(inicio, "2026-06").Id, new { amount = "21,00" })); // alterada

        var body = await OkAsync(await s.Http.DeleteAsync($"{Url}/{id}"));

        // Elegíveis de 2026-03 em diante: março, abril, julho e agosto; ficam janeiro, fevereiro (passado), a Paga e a alterada.
        body.GetProperty("deletedCount").GetInt32().ShouldBe(4);
        body.GetProperty("unlinkedCount").GetInt32().ShouldBe(4);

        (await s.Http.GetAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await EntriesAsync(s, id)).ShouldBeEmpty();
        var comuns = await EntriesByDescriptionAsync(s, "Prime Video");
        comuns.Select(e => Ym(e.EntryDate)).ShouldBe(["2026-01", "2026-02", "2026-05", "2026-06"]);
        comuns.ShouldAllBe(e => e.RecurrenceId == null && e.RecurrenceMonth == null && !e.RecurrenceModified);
        comuns.Single(e => e.EntryDate.Month == 5).Status.ShouldBe(EntryStatus.Settled);
        comuns.Single(e => e.EntryDate.Month == 6).AmountCents.ShouldBe(2100);

        var lista = await OkAsync(await s.Http.GetAsync($"{Url}?status=all"));
        lista.GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Excluir_compra_recorrente_desvincula_as_de_fatura_fechada()
    {
        var s = await NewSpaceAsync();
        var id = await CreateCompraAsync(s, "Prime Video", "19,90", 2, "2026-01", new { endType = "after_count", occurrenceLimit = 5 });

        var body = await OkAsync(await s.Http.DeleteAsync($"{Url}/{id}"));

        body.GetProperty("deletedCount").GetInt32().ShouldBe(3);
        body.GetProperty("unlinkedCount").GetInt32().ShouldBe(2);
        (await PurchasesAsync(s, id)).ShouldBeEmpty();
        var comuns = await WithDbAsync(Factory, s.Account, (db, _) => db.Set<CardPurchase>().AsNoTracking().Where(p => p.Description == "Prime Video").ToListAsync());
        comuns.Count.ShouldBe(2);
        comuns.ShouldAllBe(p => p.RecurrenceId == null && p.RecurrenceMonth == null && p.Origin == PurchaseOrigin.Manual);
    }

    [Fact]
    public async Task Recorrencia_de_outro_Espaco_nao_e_alcancada()
    {
        var lia = await NewSpaceAsync("Lia");
        var duda = await NewSpaceAsync("Duda");
        var id = await CreateSaidaAsync(lia, "Tim", "44,99", 10, "2026-03", new { endType = "after_count", occurrenceLimit = 2 });
        var body = await UpdateBodyAsync(lia, id, new { amount = "50,99" });

        (await duda.Http.PutAsJsonAsync($"{Url}/{id}", body)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await duda.Http.PostAsJsonAsync($"{Url}/{id}/end", new { fromMonth = "2026-04" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await duda.Http.DeleteAsync($"{Url}/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await EntriesAsync(lia, id)).ShouldAllBe(e => e.AmountCents == 4499);
    }
}
