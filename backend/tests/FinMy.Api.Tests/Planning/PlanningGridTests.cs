using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Api.Tests.Cards;
using FinMy.Api.Tests.Support;
using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;
using FinMy.Domain.Tags;
using static FinMy.Api.Tests.Cards.CardTestSupport;
using static FinMy.Api.Tests.Months.MonthSeeding;

namespace FinMy.Api.Tests.Planning;

/// <summary>
/// <c>GET /api/v1/years/{yyyy}/planning</c> (007 T002, research R2; US1.1, FR-001..FR-003, SC-002): linhas calculadas dos
/// lançamentos, faturas por cartão e totais Previstos idênticos aos do painel e da visão de cada mês.
/// </summary>
public class PlanningGridTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2027, 1, 10, 15, 0, 0, TimeSpan.Zero));

    protected override IClock? Clock => _clock;

    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?> { ["RateLimiting:Auth:PermitLimit"] = "100000" };

    private static DateOnly D(int month, int day) => new(2027, month, day);

    private sealed record Space(HttpClient Http, TestAccount Account, WalletRefDto Azul, WalletRefDto Verde);

    private async Task<Space> SpaceAsync()
    {
        _clock.UtcNow = DateTimeOffset.UtcNow;
        var (http, account) = await NewSpaceAsync(Factory);
        var response = await http.PostAsJsonAsync(
            "/api/v1/wallets",
            new { name = "Azul", type = "checking", color = "#1E88E5", initialBalance = "2000", initialBalanceDate = "2027-01-01" });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var azul = (await response.Content.ReadFromJsonAsync<WalletRefDto>(JsonDefaults.Options))!;
        var verde = await CreateWalletAsync(http, "Verde");
        _clock.UtcNow = new DateTimeOffset(2027, 1, 10, 15, 0, 0, TimeSpan.Zero);
        return new Space(http, account, azul, verde);
    }

    private static Task<JsonElement> GridAsync(Space s, int year = 2027) => s.Http.GetFromJsonAsync<JsonElement>($"/api/v1/years/{year}/planning");

    private static JsonElement Row(JsonElement grid, string block, string key) =>
        grid.GetProperty(block).EnumerateArray().Single(r => r.GetProperty("key").GetString() == key);

    private static JsonElement Cell(JsonElement row, int month) => row.GetProperty("cells").EnumerateArray().ElementAt(month - 1);

    [Fact]
    public async Task US1_1_ano_sem_dados_devolve_blocos_vazios_e_12_meses_de_totais()
    {
        var space = await SpaceAsync();

        var grid = await GridAsync(space);

        grid.GetProperty("year").GetInt32().ShouldBe(2027);
        grid.GetProperty("today").GetString().ShouldBe("2027-01-10");
        grid.GetProperty("incomeRows").GetArrayLength().ShouldBe(0);
        grid.GetProperty("expenseRows").GetArrayLength().ShouldBe(0);
        grid.GetProperty("invoiceRows").GetArrayLength().ShouldBe(0);
        var months = grid.GetProperty("totals").GetProperty("months").EnumerateArray().ToList();
        months.Select(m => m.GetProperty("month").GetString()).ShouldBe(Enumerable.Range(1, 12).Select(m => $"2027-{m:00}"));
        months.ShouldAllBe(m => m.GetProperty("incomeCents").GetInt64() == 0 && m.GetProperty("finalBalanceCents").GetInt64() == 200000);
    }

    [Fact]
    public async Task US1_1_FR_002_agrupa_por_descricao_sem_maiusculas_acentos_e_espacos_nas_pontas_e_ordena_alfabeticamente()
    {
        var space = await SpaceAsync();
        await Factory.SeedAsync(
            space.Account,
            NewEntry(EntryKind.Expense, "Tim", 4499, D(1, 5), space.Azul.Id, EntryStatus.Planned),
            NewEntry(EntryKind.Expense, " TIM ", 1000, D(1, 20), space.Verde.Id, EntryStatus.Planned),
            NewEntry(EntryKind.Expense, "Ação Natal", 120000, D(12, 20), space.Azul.Id, EntryStatus.Planned),
            NewEntry(EntryKind.Expense, "acao natal", 500, D(11, 2), space.Azul.Id, EntryStatus.Planned),
            NewEntry(EntryKind.Expense, "Aluguel", 150000, D(2, 5), space.Azul.Id, EntryStatus.Planned),
            NewEntry(EntryKind.Income, "Salário", 500000, D(1, 5), space.Azul.Id, EntryStatus.Planned),
            NewEntry(EntryKind.Income, "Tim", 700, D(3, 5), space.Azul.Id, EntryStatus.Planned));

        var grid = await GridAsync(space);

        grid.GetProperty("expenseRows").EnumerateArray().Select(r => r.GetProperty("key").GetString())
            .ShouldBe(["expense:acao natal", "expense:aluguel", "expense:tim"]);
        grid.GetProperty("incomeRows").EnumerateArray().Select(r => r.GetProperty("key").GetString()).ShouldBe(["income:salario", "income:tim"]);

        var tim = Row(grid, "expenseRows", "expense:tim");
        tim.GetProperty("kind").GetString().ShouldBe("expense");
        tim.GetProperty("description").GetString().ShouldBe("TIM", "o nome é o do lançamento mais recente (mesma data: o criado por último)");
        tim.GetProperty("latestDay").GetInt32().ShouldBe(20);
        tim.GetProperty("latestWalletId").GetGuid().ShouldBe(space.Verde.Id);
        Cell(tim, 1).GetProperty("plannedCents").GetInt64().ShouldBe(5499);
        Cell(tim, 1).GetProperty("itemCount").GetInt32().ShouldBe(2);
        Cell(tim, 2).GetProperty("plannedCents").GetInt64().ShouldBe(0);

        var natal = Row(grid, "expenseRows", "expense:acao natal");
        natal.GetProperty("description").GetString().ShouldBe("Ação Natal", "o lançamento de dezembro é o mais recente");
        natal.GetProperty("latestDay").GetInt32().ShouldBe(20);
        Cell(natal, 11).GetProperty("plannedCents").GetInt64().ShouldBe(500);
        Cell(natal, 12).GetProperty("plannedCents").GetInt64().ShouldBe(120000);
    }

    [Fact]
    public async Task FR_003_celula_e_editavel_com_zero_itens_ou_um_item_previsto_e_so_traz_entryId_com_um_item()
    {
        var space = await SpaceAsync();
        await Factory.SeedAsync(
            space.Account,
            NewEntry(EntryKind.Expense, "Tim", 4499, D(1, 5), space.Azul.Id, EntryStatus.Planned),
            NewEntry(EntryKind.Expense, "Tim", 4499, D(2, 5), space.Azul.Id, EntryStatus.Planned),
            NewEntry(EntryKind.Expense, "Tim", 100, D(2, 6), space.Azul.Id, EntryStatus.Planned),
            NewEntry(EntryKind.Expense, "Tim", 4499, D(3, 5), space.Azul.Id, EntryStatus.Settled),
            NewEntry(EntryKind.Expense, "Tim", 4499, D(4, 5), space.Azul.Id, EntryStatus.Settled),
            NewEntry(EntryKind.Expense, "Tim", 4499, D(4, 6), space.Azul.Id, EntryStatus.Planned));

        var tim = Row(await GridAsync(space), "expenseRows", "expense:tim");

        var jan = Cell(tim, 1);
        jan.GetProperty("editable").GetBoolean().ShouldBeTrue();
        jan.GetProperty("entryId").ValueKind.ShouldBe(JsonValueKind.String);
        jan.GetProperty("allSettled").GetBoolean().ShouldBeFalse();

        var feb = Cell(tim, 2);
        feb.GetProperty("editable").GetBoolean().ShouldBeFalse();
        feb.GetProperty("itemCount").GetInt32().ShouldBe(2);
        feb.GetProperty("entryId").ValueKind.ShouldBe(JsonValueKind.Null);
        feb.GetProperty("plannedCents").GetInt64().ShouldBe(4599);

        var mar = Cell(tim, 3);
        mar.GetProperty("editable").GetBoolean().ShouldBeFalse();
        mar.GetProperty("allSettled").GetBoolean().ShouldBeTrue();
        mar.GetProperty("itemCount").GetInt32().ShouldBe(1);

        var apr = Cell(tim, 4);
        apr.GetProperty("editable").GetBoolean().ShouldBeFalse();
        apr.GetProperty("allSettled").GetBoolean().ShouldBeFalse();

        var may = Cell(tim, 5);
        may.GetProperty("editable").GetBoolean().ShouldBeTrue();
        may.GetProperty("itemCount").GetInt32().ShouldBe(0);
        may.GetProperty("entryId").ValueKind.ShouldBe(JsonValueKind.Null);
        may.GetProperty("allSettled").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Usa_o_Mes_de_referencia_e_so_do_ano_pedido()
    {
        var space = await SpaceAsync();
        await Factory.SeedAsync(
            space.Account,
            NewEntry(EntryKind.Expense, "IPVA", 90000, D(1, 28), space.Azul.Id, EntryStatus.Planned, YearMonth.Of(2027, 2)),
            NewEntry(EntryKind.Expense, "IPVA", 88000, new DateOnly(2026, 12, 30), space.Azul.Id, EntryStatus.Planned),
            NewEntry(EntryKind.Expense, "Carla 2028", 1000, new DateOnly(2028, 1, 3), space.Azul.Id, EntryStatus.Planned));

        var grid = await GridAsync(space);

        var ipva = Row(grid, "expenseRows", "expense:ipva");
        Cell(ipva, 1).GetProperty("itemCount").GetInt32().ShouldBe(0);
        Cell(ipva, 2).GetProperty("plannedCents").GetInt64().ShouldBe(90000);
        ipva.GetProperty("cells").EnumerateArray().Sum(c => c.GetProperty("itemCount").GetInt32()).ShouldBe(1);
        grid.GetProperty("expenseRows").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Metadados_da_linha_trazem_carteira_ativa_e_tags_do_lancamento_mais_recente()
    {
        var space = await SpaceAsync();
        var bruna = Tag.Create("Bruna", "#E53935", isSettlement: true);
        var viagem = Tag.Create("Viagem", "#1E88E5");
        var recent = NewEntry(EntryKind.Expense, "Presente", 4000, D(6, 15), space.Verde.Id, EntryStatus.Planned);
        recent.ChangeSettlementTag(bruna.Id);
        var old = NewEntry(EntryKind.Expense, "Presente", 3000, D(3, 3), space.Azul.Id, EntryStatus.Planned);
        await Factory.SeedAsync(space.Account, bruna, viagem, recent, old, new EntryTag(recent.Id, viagem.Id));

        var row = Row(await GridAsync(space), "expenseRows", "expense:presente");

        row.GetProperty("latestWalletId").GetGuid().ShouldBe(space.Verde.Id);
        row.GetProperty("latestDay").GetInt32().ShouldBe(15);
        row.GetProperty("latestTagIds").EnumerateArray().Select(t => t.GetGuid()).ShouldBe([bruna.Id, viagem.Id]);

        // Carteira do mais recente arquivada: sem latestWalletId (FR-005 cai na informada ou na principal).
        (await space.Http.PostAsync($"/api/v1/wallets/{space.Verde.Id}/archive", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var archived = Row(await GridAsync(space), "expenseRows", "expense:presente");
        archived.GetProperty("latestWalletId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Compras_e_transferencias_nao_viram_linhas_mas_a_fatura_aparece_no_bloco_de_faturas()
    {
        var space = await SpaceAsync();
        var card = await CreateCardAsync(space.Http, "Azul", space.Azul.Id);
        (await space.Http.PostAsJsonAsync("/api/v1/card-purchases", new { description = "Mercado Lia", amount = "205,53", date = "2027-04-20", creditCardId = card.Id }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        await Factory.SeedAsync(space.Account, NewTransfer(space.Azul.Id, space.Verde.Id, 5000, D(3, 3), "Reserva"));

        var grid = await GridAsync(space);

        grid.GetProperty("incomeRows").GetArrayLength().ShouldBe(0);
        grid.GetProperty("expenseRows").GetArrayLength().ShouldBe(0);
        var row = grid.GetProperty("invoiceRows").EnumerateArray().Single();
        row.GetProperty("creditCardId").GetGuid().ShouldBe(card.Id);
        row.GetProperty("creditCardName").GetString().ShouldBe("Azul");
        row.GetProperty("archived").GetBoolean().ShouldBeFalse();
        var cells = row.GetProperty("cells").EnumerateArray().ToList();
        cells.Count.ShouldBe(12);
        cells.ShouldAllBe(c => c.GetProperty("editable").GetBoolean());
        var may = cells[4];
        may.GetProperty("month").GetString().ShouldBe("2027-05");
        may.GetProperty("plannedCents").GetInt64().ShouldBe(20553);
        may.GetProperty("totalCents").GetInt64().ShouldBe(20553);
        may.GetProperty("estimateCents").ValueKind.ShouldBe(JsonValueKind.Null);
        cells.Where((_, i) => i != 4).ShouldAllBe(c => c.GetProperty("plannedCents").GetInt64() == 0);
    }

    [Fact]
    public async Task US2_1_celula_da_fatura_usa_a_estimativa_e_o_previsto_acompanha_a_regra_do_fechamento()
    {
        var space = await SpaceAsync();
        var card = await CreateCardAsync(space.Http, "Azul", space.Azul.Id);
        (await space.Http.PostAsJsonAsync("/api/v1/card-purchases", new { description = "Mercado Lia", amount = "205,53", date = "2027-04-20", creditCardId = card.Id }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await space.Http.PutAsJsonAsync($"{CardsUrl}/{card.Id}/invoices/2027-05/estimate", new { amount = "800" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await space.Http.PutAsJsonAsync($"{CardsUrl}/{card.Id}/invoices/2027-08/estimate", new { amount = "300" })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var before = Cell(Row(await GridAsync(space), "invoiceRows", card.Id), 5);
        before.GetProperty("plannedCents").GetInt64().ShouldBe(80000);
        before.GetProperty("totalCents").GetInt64().ShouldBe(20553);
        before.GetProperty("estimateCents").GetInt64().ShouldBe(80000);

        // Fatura só com estimativa, sem compras: o Previsto é a estimativa.
        var august = Cell(Row(await GridAsync(space), "invoiceRows", card.Id), 8);
        august.GetProperty("plannedCents").GetInt64().ShouldBe(30000);
        august.GetProperty("totalCents").GetInt64().ShouldBe(0);

        // Depois do fechamento a estimativa deixa de valer no Previsto, mas continua na célula.
        _clock.UtcNow = new DateTimeOffset(2027, 5, 19, 15, 0, 0, TimeSpan.Zero);
        var after = Cell(Row(await GridAsync(space), "invoiceRows", card.Id), 5);
        after.GetProperty("plannedCents").GetInt64().ShouldBe(20553);
        after.GetProperty("estimateCents").GetInt64().ShouldBe(80000);
    }

    private static JsonElement Row(JsonElement grid, string block, Guid cardId) =>
        grid.GetProperty(block).EnumerateArray().Single(r => r.GetProperty("creditCardId").GetGuid() == cardId);

    [Fact]
    public async Task Cartao_arquivado_so_aparece_com_fatura_no_ano_e_cartao_ativo_aparece_sempre()
    {
        var space = await SpaceAsync();
        var comFatura = await CreateCardAsync(space.Http, "Antigo", space.Azul.Id);
        var semFatura = await CreateCardAsync(space.Http, "Velho", space.Azul.Id);
        var ativo = await CreateCardAsync(space.Http, "Verde", space.Verde.Id);
        (await space.Http.PutAsJsonAsync($"{CardsUrl}/{comFatura.Id}/invoices/2027-03/estimate", new { amount = "100" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await space.Http.PostAsync($"{CardsUrl}/{comFatura.Id}/archive", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await space.Http.PostAsync($"{CardsUrl}/{semFatura.Id}/archive", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var rows = (await GridAsync(space)).GetProperty("invoiceRows").EnumerateArray().ToList();
        rows.Select(r => r.GetProperty("creditCardName").GetString()).ShouldBe(["Antigo", "Verde"]);
        rows[0].GetProperty("archived").GetBoolean().ShouldBeTrue();
        rows[1].GetProperty("creditCardId").GetGuid().ShouldBe(ativo.Id);

        // Em outro ano, o arquivado sem fatura no ano some.
        (await GridAsync(space, 2028)).GetProperty("invoiceRows").EnumerateArray()
            .Select(r => r.GetProperty("creditCardName").GetString()).ShouldBe(["Verde"]);
    }

    [Fact]
    public async Task SC_002_totais_da_grade_sao_identicos_aos_do_painel_e_aos_da_visao_de_cada_mes()
    {
        var space = await SpaceAsync();
        var card = await CreateCardAsync(space.Http, "Azul", space.Azul.Id);
        var rows = new List<object>();
        for (var month = 1; month <= 12; month++)
        {
            rows.Add(NewEntry(EntryKind.Income, "Salário", 400000 + month * 100, D(month, 5), space.Azul.Id, month <= 2 ? EntryStatus.Settled : EntryStatus.Planned));
            rows.Add(NewEntry(EntryKind.Expense, "Aluguel", 120000, D(month, 10), space.Azul.Id, EntryStatus.Planned));
            rows.Add(NewEntry(EntryKind.Expense, "Léo Casa", 7700 + month, D(month, 12), space.Verde.Id, EntryStatus.Planned));
        }

        rows.Add(NewEntry(EntryKind.Income, "13º Duda", 150000, D(12, 20), space.Verde.Id, EntryStatus.Planned));
        rows.Add(NewEntry(EntryKind.Expense, "Natal", 120000, D(12, 20), space.Azul.Id, EntryStatus.Planned));
        rows.Add(NewTransfer(space.Azul.Id, space.Verde.Id, 25000, D(2, 3)));
        await Factory.SeedAsync(space.Account, [.. rows]);
        (await space.Http.PostAsJsonAsync("/api/v1/card-purchases", new { description = "Casa China", amount = "301,40", date = "2027-02-12", creditCardId = card.Id }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await space.Http.PutAsJsonAsync($"{CardsUrl}/{card.Id}/invoices/2027-06/estimate", new { amount = "800" })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var grid = await GridAsync(space);
        var panel = await space.Http.GetFromJsonAsync<JsonElement>("/api/v1/years/2027/panel");

        var totals = grid.GetProperty("totals").GetProperty("months").EnumerateArray().ToList();
        totals.Count.ShouldBe(12);
        for (var m = 1; m <= 12; m++)
        {
            var key = $"2027-{m:00}";
            var month = await space.Http.GetFromJsonAsync<JsonElement>($"/api/v1/months/{key}");
            var planned = month.GetProperty("planned");
            var total = totals[m - 1];
            total.GetProperty("month").GetString().ShouldBe(key);
            foreach (var field in new[] { "incomeCents", "expenseCents", "resultCents", "finalBalanceCents" })
            {
                total.GetProperty(field).GetInt64().ShouldBe(planned.GetProperty(field).GetInt64(), $"{key} visão do mês {field}");
                total.GetProperty(field).GetInt64().ShouldBe(panel.GetProperty("months")[m - 1].GetProperty("planned").GetProperty(field).GetInt64(), $"{key} painel {field}");
            }
        }

        // As células explicam os totais: entradas/saídas das linhas + faturas = coluna do mês.
        for (var m = 1; m <= 12; m++)
        {
            long Sum(string block) => grid.GetProperty(block).EnumerateArray().Sum(r => Cell(r, m).GetProperty("plannedCents").GetInt64());
            Sum("incomeRows").ShouldBe(totals[m - 1].GetProperty("incomeCents").GetInt64());
            (Sum("expenseRows") + Sum("invoiceRows")).ShouldBe(totals[m - 1].GetProperty("expenseCents").GetInt64());
        }
    }

    [Theory]
    [InlineData("1999")]
    [InlineData("2101")]
    [InlineData("abcd")]
    public async Task Ano_fora_de_2000_a_2100_ou_invalido_devolve_422(string year)
    {
        var space = await SpaceAsync();

        var response = await space.Http.GetAsync($"/api/v1/years/{year}/planning");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Sem_login_devolve_401()
    {
        using var anonymous = Factory.CreateClient();

        (await anonymous.GetAsync("/api/v1/years/2027/planning")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Nao_enxerga_dados_de_outro_Espaco()
    {
        _clock.UtcNow = DateTimeOffset.UtcNow;
        var (leo, _) = await NewSpaceAsync(Factory, "Léo");
        var lia = await SpaceAsync();
        await Factory.SeedAsync(lia.Account, NewEntry(EntryKind.Expense, "Natal", 120000, D(12, 20), lia.Azul.Id, EntryStatus.Planned));

        (await GridAsync(lia)).GetProperty("expenseRows").GetArrayLength().ShouldBe(1);
        _clock.UtcNow = DateTimeOffset.UtcNow;
        var other = await leo.GetFromJsonAsync<JsonElement>("/api/v1/years/2027/planning");
        other.GetProperty("expenseRows").GetArrayLength().ShouldBe(0);
        other.GetProperty("invoiceRows").GetArrayLength().ShouldBe(0);
    }
}
