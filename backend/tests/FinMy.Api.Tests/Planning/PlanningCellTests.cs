using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Api.Tests.Cards;
using FinMy.Api.Tests.Entries;
using FinMy.Api.Tests.Support;
using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Entries;
using FinMy.Domain.Tags;
using Microsoft.EntityFrameworkCore;
using static FinMy.Api.Tests.Cards.CardTestSupport;
using static FinMy.Api.Tests.Months.MonthSeeding;

namespace FinMy.Api.Tests.Planning;

/// <summary>
/// <c>PUT /years/{yyyy}/planning/cells</c> e <c>POST /years/{yyyy}/planning/fill</c> (007 T003, research R3; US1.2–US1.6, US1.8,
/// US2.4, US2.5, FR-003..FR-008). "Hoje" é 10/01/2027; dados fictícios (carteiras Azul e Verde, cartão Azul fecha dia 18).
/// </summary>
public class PlanningCellTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private const string Cells = "/api/v1/years/2027/planning/cells";
    private const string Fill = "/api/v1/years/2027/planning/fill";

    private readonly FakeClock _clock = new(new DateTimeOffset(2027, 1, 10, 15, 0, 0, TimeSpan.Zero));

    protected override IClock? Clock => _clock;

    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?> { ["RateLimiting:Auth:PermitLimit"] = "100000" };

    private static readonly string[] Jan = ["2027-01"];
    private static readonly string[] FebMar = ["2027-02", "2027-03"];

    private static DateOnly D(int month, int day) => new(2027, month, day);

    private sealed record Space(HttpClient Http, TestAccount Account, WalletRefDto Azul, WalletRefDto Verde, CreditCardDto Card);

    private async Task<Space> SpaceAsync(string name = "Lia")
    {
        _clock.UtcNow = DateTimeOffset.UtcNow;
        var (http, account) = await NewSpaceAsync(Factory, name);
        var azul = await CreateWalletAsync(http, "Azul");
        var verde = await CreateWalletAsync(http, "Verde");
        var card = await CreateCardAsync(http, "Azul", azul.Id);
        _clock.UtcNow = new DateTimeOffset(2027, 1, 10, 15, 0, 0, TimeSpan.Zero);
        return new Space(http, account, azul, verde, card);
    }

    private static Task<HttpResponseMessage> PutCell(Space s, object body, string url = Cells) => s.Http.PutAsJsonAsync(url, body);

    private static Task<HttpResponseMessage> PutExpense(Space s, string description, string month, string? value, object? extra = null)
    {
        var body = new Dictionary<string, object?> { ["kind"] = "expense", ["description"] = description, ["month"] = month, ["value"] = value };
        foreach (var property in extra?.GetType().GetProperties() ?? [])
        {
            body[property.Name] = property.GetValue(extra);
        }

        return PutCell(s, body);
    }

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    private static Task<JsonElement> GridAsync(Space s) => s.Http.GetFromJsonAsync<JsonElement>("/api/v1/years/2027/planning");

    private static JsonElement Cell(JsonElement row, int month) => row.GetProperty("cells").EnumerateArray().ElementAt(month - 1);

    private static JsonElement Month(JsonElement totals, int month) => totals.GetProperty("months").EnumerateArray().ElementAt(month - 1);

    private static JsonElement Row(JsonElement grid, string block, string key) =>
        grid.GetProperty(block).EnumerateArray().Single(r => r.GetProperty("key").GetString() == key);

    private static async Task<JsonElement> EntryAsync(Space s, Guid id) => await s.Http.GetFromJsonAsync<JsonElement>($"/api/v1/entries/{id}");

    private async Task<List<Entry>> EntriesAsync(Space s, string description) =>
        await WithDbAsync(Factory, s.Account, (db, _) =>
            db.Set<Entry>().AsNoTracking().Where(e => e.Description == description).OrderBy(e => e.ReferenceMonth).ToListAsync());

    [Fact]
    public async Task US1_2_digitar_em_celula_vazia_cria_Saida_Prevista_com_mes_de_referencia_da_coluna_e_padroes()
    {
        var space = await SpaceAsync();

        var body = await OkAsync(await PutExpense(space, "Natal", "2027-12", "1200"));

        var cell = Cell(body.GetProperty("row"), 12);
        cell.GetProperty("plannedCents").GetInt64().ShouldBe(120000);
        cell.GetProperty("itemCount").GetInt32().ShouldBe(1);
        body.GetProperty("row").GetProperty("key").GetString().ShouldBe("expense:natal");
        body.GetProperty("deletionBatchId").ValueKind.ShouldBe(JsonValueKind.Null);

        var entry = await EntryAsync(space, cell.GetProperty("entryId").GetGuid());
        entry.GetProperty("kind").GetString().ShouldBe("expense");
        entry.GetProperty("status").GetString().ShouldBe("planned");
        entry.GetProperty("referenceMonth").GetString().ShouldBe("2027-12");
        entry.GetProperty("date").GetString().ShouldBe("2027-12-01", "sem lançamento na linha nem dia informado: dia 1 (FR-004)");
        entry.GetProperty("walletId").GetGuid().ShouldBe(space.Azul.Id, "sem carteira informada: a principal (FR-005)");
    }

    [Fact]
    public async Task US1_2_FR_008_totais_da_resposta_se_atualizam_na_hora_e_iguais_ao_da_grade()
    {
        var space = await SpaceAsync();
        await OkAsync(await PutExpense(space, "Natal", "2027-12", "1200"));

        var body = await OkAsync(await PutCell(space, new { kind = "income", description = "13º salário", month = "2027-12", value = "1500" }));

        var december = Month(body.GetProperty("totals"), 12);
        december.GetProperty("incomeCents").GetInt64().ShouldBe(150000);
        december.GetProperty("expenseCents").GetInt64().ShouldBe(120000);
        december.GetProperty("resultCents").GetInt64().ShouldBe(30000, "Natal e 13º juntos mudam o resultado em +300,00 (teste independente da US1)");
        var grid = await GridAsync(space);
        Month(grid.GetProperty("totals"), 12).GetRawText().ShouldBe(december.GetRawText());
    }

    [Fact]
    public async Task FR_004_FR_005_linha_nova_usa_carteira_e_dia_informados_e_ajusta_dia_inexistente()
    {
        var space = await SpaceAsync();

        var march = await OkAsync(await PutExpense(space, "Condomínio", "2027-03", "300", new { walletId = space.Verde.Id, day = 31 }));
        var feb = await OkAsync(await PutExpense(space, "Condomínio", "2027-02", "300", new { walletId = space.Azul.Id, day = 9 }));

        var marchEntry = await EntryAsync(space, Cell(march.GetProperty("row"), 3).GetProperty("entryId").GetGuid());
        marchEntry.GetProperty("date").GetString().ShouldBe("2027-03-31");
        marchEntry.GetProperty("walletId").GetGuid().ShouldBe(space.Verde.Id);
        var febEntry = await EntryAsync(space, Cell(feb.GetProperty("row"), 2).GetProperty("entryId").GetGuid());
        febEntry.GetProperty("date").GetString().ShouldBe("2027-02-28", "a linha já existe: vale o dia do mais recente (31), ajustado ao fim de fevereiro (RT-03)");
        febEntry.GetProperty("walletId").GetGuid().ShouldBe(space.Verde.Id, "a carteira do lançamento mais recente da linha vence a informada (FR-005)");
    }

    [Fact]
    public async Task FR_004_FR_005_celula_de_linha_existente_herda_dia_carteira_e_tags_do_lancamento_mais_recente()
    {
        var space = await SpaceAsync();
        var bruna = Tag.Create("Bruna", "#E53935", isSettlement: true);
        var viagem = Tag.Create("Viagem", "#1E88E5");
        var latest = NewEntry(EntryKind.Expense, "Presente", 4000, D(6, 15), space.Verde.Id, EntryStatus.Planned);
        latest.ChangeSettlementTag(bruna.Id);
        await Factory.SeedAsync(space.Account, bruna, viagem, latest, new EntryTag(latest.Id, viagem.Id));

        var body = await OkAsync(await PutExpense(space, "presente", "2027-09", "55,50", new { walletId = space.Azul.Id, day = 2 }));

        var entry = await EntryAsync(space, Cell(body.GetProperty("row"), 9).GetProperty("entryId").GetGuid());
        entry.GetProperty("description").GetString().ShouldBe("Presente", "o nome exibido da linha");
        entry.GetProperty("date").GetString().ShouldBe("2027-09-15");
        entry.GetProperty("walletId").GetGuid().ShouldBe(space.Verde.Id);
        entry.GetProperty("tagIds").EnumerateArray().Select(t => t.GetGuid()).ToHashSet().ShouldBe([bruna.Id, viagem.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task FR_005_carteira_arquivada_da_linha_cai_na_informada_e_tag_arquivada_nao_e_levada()
    {
        var space = await SpaceAsync();
        var carla = Tag.Create("Carla", "#8E24AA");
        await Factory.SeedAsync(space.Account, carla);
        var seed = NewEntry(EntryKind.Expense, "Escola", 90000, D(2, 5), space.Verde.Id, EntryStatus.Planned);
        await Factory.SeedAsync(space.Account, seed, new EntryTag(seed.Id, carla.Id));
        (await space.Http.PostAsync($"/api/v1/wallets/{space.Verde.Id}/archive", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await space.Http.PostAsync($"/api/v1/tags/{carla.Id}/archive", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await OkAsync(await PutExpense(space, "Escola", "2027-03", "900"));

        var entry = await EntryAsync(space, Cell(body.GetProperty("row"), 3).GetProperty("entryId").GetGuid());
        entry.GetProperty("walletId").GetGuid().ShouldBe(space.Azul.Id, "sem carteira ativa na linha nem informada: a principal");
        entry.GetProperty("tagIds").GetArrayLength().ShouldBe(0);
        entry.GetProperty("date").GetString().ShouldBe("2027-03-05");
    }

    [Fact]
    public async Task US1_3_digitar_em_celula_com_uma_Prevista_altera_o_valor()
    {
        var space = await SpaceAsync();
        await Factory.SeedAsync(space.Account, NewEntry(EntryKind.Expense, "Tim", 4499, D(3, 5), space.Azul.Id, EntryStatus.Planned));

        var body = await OkAsync(await PutExpense(space, "Tim", "2027-03", "50,99"));

        var cell = Cell(body.GetProperty("row"), 3);
        cell.GetProperty("plannedCents").GetInt64().ShouldBe(5099);
        cell.GetProperty("itemCount").GetInt32().ShouldBe(1);
        var entry = await EntryAsync(space, cell.GetProperty("entryId").GetGuid());
        entry.GetProperty("amountCents").GetInt64().ShouldBe(5099);
        entry.GetProperty("date").GetString().ShouldBe("2027-03-05");
        entry.GetProperty("status").GetString().ShouldBe("planned");
        Month(body.GetProperty("totals"), 3).GetProperty("expenseCents").GetInt64().ShouldBe(5099);
    }

    [Fact]
    public async Task US1_3_ocorrencia_de_recorrencia_editada_pela_grade_fica_marcada_como_alterada()
    {
        var space = await SpaceAsync();
        var created = await space.Http.PostAsJsonAsync(
            "/api/v1/recurrences",
            new { kind = "expense", description = "Tim", amount = "44,99", dayOfMonth = 10, walletId = space.Azul.Id, startMonth = "2027-01" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var recurrenceId = (await created.JsonAsync()).GetProperty("id").GetGuid();

        await OkAsync(await PutExpense(space, "Tim", "2027-03", "50,99"));

        var entries = await WithDbAsync(Factory, space.Account, (db, _) =>
            db.Set<Entry>().AsNoTracking().Where(e => e.RecurrenceId == recurrenceId).OrderBy(e => e.RecurrenceMonth).ToListAsync());
        entries.Where(e => e.RecurrenceModified).Select(e => e.ReferenceMonth).ShouldBe([D(3, 1)]);
        entries.Single(e => e.RecurrenceModified).AmountCents.ShouldBe(5099);
    }

    [Fact]
    public async Task US1_4_apagar_a_celula_exclui_a_Saida_num_lote_que_pode_ser_desfeito_e_a_linha_sem_lancamentos_volta_vazia()
    {
        var space = await SpaceAsync();
        await Factory.SeedAsync(space.Account, NewEntry(EntryKind.Expense, "Tim", 4499, D(3, 5), space.Azul.Id, EntryStatus.Planned));

        var body = await OkAsync(await PutExpense(space, "Tim", "2027-03", ""));

        body.GetProperty("deletionBatchId").ValueKind.ShouldBe(JsonValueKind.String);
        var cell = Cell(body.GetProperty("row"), 3);
        cell.GetProperty("itemCount").GetInt32().ShouldBe(0);
        cell.GetProperty("plannedCents").GetInt64().ShouldBe(0);
        Month(body.GetProperty("totals"), 3).GetProperty("expenseCents").GetInt64().ShouldBe(0);
        (await GridAsync(space)).GetProperty("expenseRows").GetArrayLength().ShouldBe(0, "linha sem lançamentos no ano some da grade");

        var restore = await space.Http.PostAsync($"/api/v1/deletions/{body.GetProperty("deletionBatchId").GetGuid()}/restore", content: null);
        restore.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        Cell(Row(await GridAsync(space), "expenseRows", "expense:tim"), 3).GetProperty("plannedCents").GetInt64().ShouldBe(4499);
    }

    [Fact]
    public async Task US1_4_apagar_celula_ja_vazia_nao_faz_nada()
    {
        var space = await SpaceAsync();

        var body = await OkAsync(await PutExpense(space, "Tim", "2027-03", null));

        body.GetProperty("deletionBatchId").ValueKind.ShouldBe(JsonValueKind.Null);
        Cell(body.GetProperty("row"), 3).GetProperty("itemCount").GetInt32().ShouldBe(0);
        (await EntriesAsync(space, "Tim")).ShouldBeEmpty();
    }

    [Fact]
    public async Task US1_5_celula_com_dois_lancamentos_devolve_409_com_a_lista_e_nao_altera_nada()
    {
        var space = await SpaceAsync();
        await Factory.SeedAsync(
            space.Account,
            NewEntry(EntryKind.Expense, "Tim", 4499, D(2, 5), space.Azul.Id, EntryStatus.Planned),
            NewEntry(EntryKind.Expense, "Tim", 100, D(2, 6), space.Verde.Id, EntryStatus.Planned));
        var before = (await GridAsync(space)).GetRawText();

        var response = await PutExpense(space, "Tim", "2027-02", "10");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await response.JsonAsync();
        problem.GetProperty("code").GetString().ShouldBe("planning.cell_not_editable");
        var items = problem.GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("amountCents").GetInt64()).ShouldBe([4499L, 100L]);
        items[0].GetProperty("description").GetString().ShouldBe("Tim");
        items[0].GetProperty("date").GetString().ShouldBe("2027-02-05");
        items[0].GetProperty("status").GetString().ShouldBe("planned");
        items[1].GetProperty("walletId").GetGuid().ShouldBe(space.Verde.Id);
        (await GridAsync(space)).GetRawText().ShouldBe(before);
    }

    [Fact]
    public async Task US1_5_celula_com_lancamento_efetivado_devolve_409_tambem_ao_apagar()
    {
        var space = await SpaceAsync();
        await Factory.SeedAsync(space.Account, NewEntry(EntryKind.Expense, "Tim", 4499, D(3, 5), space.Azul.Id, EntryStatus.Settled));

        foreach (var value in new[] { "10", "" })
        {
            var response = await PutExpense(space, "Tim", "2027-03", value);

            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            var problem = await response.JsonAsync();
            problem.GetProperty("code").GetString().ShouldBe("planning.cell_not_editable");
            problem.GetProperty("items").EnumerateArray().Single().GetProperty("status").GetString().ShouldBe("settled");
        }

        (await EntriesAsync(space, "Tim")).Single().AmountCents.ShouldBe(4499);
    }

    [Theory]
    [InlineData("=1100-500")]
    [InlineData("1100-500")]
    public async Task US1_6_expressao_com_ou_sem_igual_inicial_vale_600(string value)
    {
        var space = await SpaceAsync();

        var body = await OkAsync(await PutExpense(space, "Reforma", "2027-04", value));

        Cell(body.GetProperty("row"), 4).GetProperty("plannedCents").GetInt64().ShouldBe(60000);
    }

    [Fact]
    public async Task US1_6_expressao_tambem_vale_ao_alterar_celula_existente()
    {
        var space = await SpaceAsync();
        await Factory.SeedAsync(space.Account, NewEntry(EntryKind.Expense, "Reforma", 1000, D(4, 5), space.Azul.Id, EntryStatus.Planned));

        var body = await OkAsync(await PutExpense(space, "Reforma", "2027-04", "=1100-500"));

        Cell(body.GetProperty("row"), 4).GetProperty("plannedCents").GetInt64().ShouldBe(60000);
    }

    [Theory]
    [InlineData("kind", "transfer")]
    [InlineData("month", "2026-12")]
    [InlineData("month", "2027-13")]
    [InlineData("value", "abc")]
    [InlineData("value", "0")]
    [InlineData("description", "   ")]
    [InlineData("day", "40")]
    public async Task Entradas_invalidas_devolvem_422_e_nao_criam_nada(string field, string bad)
    {
        var space = await SpaceAsync();
        var body = new Dictionary<string, object?>
        {
            ["kind"] = "expense",
            ["description"] = "Natal",
            ["month"] = "2027-12",
            ["value"] = "100",
        };
        body[field] = field == "day" ? int.Parse(bad, System.Globalization.CultureInfo.InvariantCulture) : bad;

        var response = await PutCell(space, body);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, await response.Content.ReadAsStringAsync());
        (await EntriesAsync(space, "Natal")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Ano_invalido_na_rota_devolve_422()
    {
        var space = await SpaceAsync();

        var response = await PutCell(space, new { kind = "expense", description = "Natal", month = "1999-12", value = "100" }, "/api/v1/years/1999/planning/cells");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task US2_4_valor_na_linha_do_cartao_define_a_estimativa_e_nao_cria_compra_e_apagar_remove()
    {
        var space = await SpaceAsync();

        var set = await OkAsync(await PutCell(space, new { kind = "invoice", creditCardId = space.Card.Id, month = "2027-05", value = "800" }));

        set.GetProperty("row").GetProperty("creditCardId").GetGuid().ShouldBe(space.Card.Id);
        var cell = Cell(set.GetProperty("row"), 5);
        cell.GetProperty("estimateCents").GetInt64().ShouldBe(80000);
        cell.GetProperty("plannedCents").GetInt64().ShouldBe(80000, "antes do fechamento o Previsto usa a estimativa");
        cell.GetProperty("totalCents").GetInt64().ShouldBe(0, "nenhuma compra foi criada");
        Month(set.GetProperty("totals"), 5).GetProperty("expenseCents").GetInt64().ShouldBe(80000);
        (await GridAsync(space)).GetProperty("expenseRows").GetArrayLength().ShouldBe(0);

        var cleared = await OkAsync(await PutCell(space, new { kind = "invoice", creditCardId = space.Card.Id, month = "2027-05", value = "" }));

        Cell(cleared.GetProperty("row"), 5).GetProperty("estimateCents").ValueKind.ShouldBe(JsonValueKind.Null);
        Month(cleared.GetProperty("totals"), 5).GetProperty("expenseCents").GetInt64().ShouldBe(0);
    }

    [Fact]
    public async Task US2_4_cartao_inexistente_ou_de_outro_espaco_devolve_404_e_valor_invalido_422()
    {
        var space = await SpaceAsync();
        var other = await SpaceAsync("Léo");

        (await PutCell(space, new { kind = "invoice", creditCardId = other.Card.Id, month = "2027-05", value = "800" }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PutCell(space, new { kind = "invoice", creditCardId = Guid.NewGuid(), month = "2027-05", value = "800" }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PutCell(space, new { kind = "invoice", month = "2027-05", value = "800" }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await PutCell(space, new { kind = "invoice", creditCardId = space.Card.Id, month = "2027-05", value = "xx" }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Isolamento_celula_nao_enxerga_nem_altera_lancamentos_de_outro_espaco()
    {
        var space = await SpaceAsync();
        var other = await SpaceAsync("Léo");
        await Factory.SeedAsync(other.Account, NewEntry(EntryKind.Expense, "Tim", 4499, D(3, 5), other.Azul.Id, EntryStatus.Planned));

        var body = await OkAsync(await PutExpense(space, "Tim", "2027-03", "10"));

        Cell(body.GetProperty("row"), 3).GetProperty("itemCount").GetInt32().ShouldBe(1);
        (await EntriesAsync(other, "Tim")).Single().AmountCents.ShouldBe(4499);
    }

    [Fact]
    public async Task US1_8_preencher_meses_cria_Previstas_nos_meses_vazios_e_pula_os_que_ja_tem_valor()
    {
        var space = await SpaceAsync();
        await Factory.SeedAsync(
            space.Account,
            NewEntry(EntryKind.Expense, "Mercado", 25000, D(3, 8), space.Verde.Id, EntryStatus.Planned),
            NewEntry(EntryKind.Expense, "Mercado", 100, D(6, 8), space.Verde.Id, EntryStatus.Settled));

        var body = await OkAsync(await space.Http.PostAsJsonAsync(
            Fill,
            new { kind = "expense", description = "Mercado", amount = "300", months = Enumerable.Range(1, 12).Select(m => $"2027-{m:00}") }));

        body.GetProperty("created").GetInt32().ShouldBe(10);
        body.GetProperty("skipped").GetInt32().ShouldBe(2);
        var row = body.GetProperty("row");
        Cell(row, 1).GetProperty("plannedCents").GetInt64().ShouldBe(30000);
        Cell(row, 3).GetProperty("plannedCents").GetInt64().ShouldBe(25000, "mês com valor não é alterado");
        Cell(row, 6).GetProperty("plannedCents").GetInt64().ShouldBe(100);
        Month(body.GetProperty("totals"), 1).GetProperty("expenseCents").GetInt64().ShouldBe(30000);

        var created = (await EntriesAsync(space, "Mercado")).Where(e => e.Description == "Mercado" && e.AmountCents == 30000).ToList();
        created.Count.ShouldBe(10);
        created.ShouldAllBe(e => e.Status == EntryStatus.Planned && e.WalletId == space.Verde.Id && e.EntryDate.Day == 8);
    }

    [Fact]
    public async Task US1_8_preencher_em_linha_nova_usa_carteira_e_dia_informados()
    {
        var space = await SpaceAsync();

        var body = await OkAsync(await space.Http.PostAsJsonAsync(
            Fill,
            new { kind = "income", description = "Salário Lia", amount = "5000", months = FebMar, walletId = space.Verde.Id, day = 5 }));

        body.GetProperty("created").GetInt32().ShouldBe(2);
        body.GetProperty("skipped").GetInt32().ShouldBe(0);
        var entries = await EntriesAsync(space, "Salário Lia");
        entries.ShouldAllBe(e => e.Kind == EntryKind.Income && e.WalletId == space.Verde.Id && e.EntryDate.Day == 5);
        entries.Select(e => e.EntryDate.Month).ShouldBe([2, 3]);
    }

    [Fact]
    public async Task US1_8_preenchimento_invalido_devolve_422_e_nao_cria_nada()
    {
        var space = await SpaceAsync();

        foreach (var body in new object[]
        {
            new { kind = "expense", description = "Mercado", amount = "abc", months = new[] { "2027-02" } },
            new { kind = "expense", description = "Mercado", amount = "300", months = Array.Empty<string>() },
            new { kind = "expense", description = "Mercado", amount = "300", months = new[] { "2027-02", "2028-01" } },
            new { kind = "expense", description = "", amount = "300", months = new[] { "2027-02" } },
        })
        {
            (await space.Http.PostAsJsonAsync(Fill, body)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }

        (await EntriesAsync(space, "Mercado")).ShouldBeEmpty();
    }

    [Fact]
    public async Task US2_5_preencher_a_linha_do_cartao_define_a_estimativa_so_nas_faturas_sem_estimativa()
    {
        var space = await SpaceAsync();
        (await space.Http.PutAsJsonAsync($"{CardsUrl}/{space.Card.Id}/invoices/2027-05/estimate", new { amount = "500" })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await OkAsync(await space.Http.PostAsJsonAsync(
            Fill, new { kind = "invoice", creditCardId = space.Card.Id, amount = "800", months = Enumerable.Range(1, 12).Select(m => $"2027-{m:00}") }));

        body.GetProperty("created").GetInt32().ShouldBe(11);
        body.GetProperty("skipped").GetInt32().ShouldBe(1);
        var row = body.GetProperty("row");
        row.GetProperty("creditCardId").GetGuid().ShouldBe(space.Card.Id);
        Cell(row, 5).GetProperty("estimateCents").GetInt64().ShouldBe(50000, "a estimativa existente é mantida");
        Cell(row, 1).GetProperty("estimateCents").GetInt64().ShouldBe(80000);
        Cell(row, 12).GetProperty("estimateCents").GetInt64().ShouldBe(80000);
        (await GridAsync(space)).GetProperty("expenseRows").GetArrayLength().ShouldBe(0, "estimativa não cria lançamentos");
    }

    [Fact]
    public async Task US2_5_preencher_cartao_invalido_devolve_422_ou_404()
    {
        var space = await SpaceAsync();

        (await space.Http.PostAsJsonAsync(Fill, new { kind = "invoice", amount = "800", months = Jan }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await space.Http.PostAsJsonAsync(Fill, new { kind = "invoice", creditCardId = Guid.NewGuid(), amount = "800", months = Jan }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await space.Http.PostAsJsonAsync(Fill, new { kind = "invoice", creditCardId = space.Card.Id, amount = "", months = Jan }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }
}
