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
/// <c>GET /years/{yyyy}/planning/copy-candidates</c> e <c>POST /years/{yyyy}/planning/copy</c> (007 T004, research R4; US3.1–US3.4,
/// FR-013). "Hoje" é 10/01/2027; dados fictícios (carteiras Azul e Verde, cartão Azul).
/// </summary>
public class PlanningCopyTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private const string Candidates = "/api/v1/years/2027/planning/copy-candidates";
    private const string Copy = "/api/v1/years/2027/planning/copy";

    private readonly FakeClock _clock = new(new DateTimeOffset(2027, 1, 10, 15, 0, 0, TimeSpan.Zero));

    protected override IClock? Clock => _clock;

    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?> { ["RateLimiting:Auth:PermitLimit"] = "100000" };

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

    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    private static Task<JsonElement> CandidatesAsync(Space s, string url = Candidates) => s.Http.GetFromJsonAsync<JsonElement>(url);

    private static Task<HttpResponseMessage> PostCopy(Space s, string[] rowKeys, bool copyEstimates = false, string url = Copy) =>
        s.Http.PostAsJsonAsync(url, new { rowKeys, copyEstimates });

    private static JsonElement RowOf(JsonElement candidates, string key) =>
        candidates.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("key").GetString() == key);

    private static string[] Keys(JsonElement candidates) =>
        [.. candidates.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("key").GetString()!)];

    private Task<List<Entry>> EntriesOfYearAsync(Space s, int year) =>
        WithDbAsync(Factory, s.Account, (db, _) =>
            db.Set<Entry>().AsNoTracking()
                .Where(e => e.ReferenceMonth >= new DateOnly(year, 1, 1) && e.ReferenceMonth <= new DateOnly(year, 12, 1))
                .OrderBy(e => e.ReferenceMonth).ThenBy(e => e.Description).ToListAsync());

    private async Task SeedNatalAnd13Async(Space s)
    {
        await Factory.SeedAsync(
            s.Account,
            NewEntry(EntryKind.Expense, "Natal", 120000, D(2026, 12, 20), s.Azul.Id, EntryStatus.Settled),
            NewEntry(EntryKind.Income, "13º salário", 150000, D(2026, 12, 5), s.Azul.Id, EntryStatus.Settled));
    }

    [Fact]
    public async Task US3_1_lista_as_linhas_do_ano_anterior_com_valores_por_mes_sem_criar_nada()
    {
        var space = await SpaceAsync();
        await SeedNatalAnd13Async(space);

        var body = await CandidatesAsync(space);

        Keys(body).ShouldBe(["income:13º salario", "expense:natal"], "Entradas antes das Saídas, por descrição");
        var natal = RowOf(body, "expense:natal");
        natal.GetProperty("kind").GetString().ShouldBe("expense");
        natal.GetProperty("description").GetString().ShouldBe("Natal");
        var cells = natal.GetProperty("cells").EnumerateArray().ToList();
        cells.Count.ShouldBe(12);
        cells[11].GetProperty("month").GetString().ShouldBe("2026-12");
        cells[11].GetProperty("plannedCents").GetInt64().ShouldBe(120000);
        cells[11].GetProperty("itemCount").GetInt32().ShouldBe(1);
        cells[0].GetProperty("plannedCents").GetInt64().ShouldBe(0);
        (await EntriesOfYearAsync(space, 2027)).ShouldBeEmpty("nada é criado sem confirmação (FR-013)");
    }

    [Fact]
    public async Task US3_3_ocorrencias_de_recorrencia_nao_aparecem_e_partes_de_divisao_ficam_separadas_por_tag_de_acerto()
    {
        var space = await SpaceAsync();
        var created = await space.Http.PostAsJsonAsync(
            "/api/v1/recurrences",
            new { kind = "expense", description = "Tim", amount = "44,99", dayOfMonth = 10, walletId = space.Azul.Id, startMonth = "2026-01" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        (await EntriesOfYearAsync(space, 2026)).Count(e => e.RecurrenceId is not null).ShouldBeGreaterThan(0, "a recorrência gerou ocorrências em 2026");

        var bruna = Tag.Create("Bruna", "#E53935", isSettlement: true);
        var carla = Tag.Create("Carla", "#8E24AA", isSettlement: true);
        var group = Guid.CreateVersion7();
        var partBruna = NewEntry(EntryKind.Expense, "Mercado", 30000, D(2026, 3, 8), space.Azul.Id, EntryStatus.Settled);
        partBruna.ChangeSettlementTag(bruna.Id);
        partBruna.ChangeSplitGroup(group);
        var partCarla = NewEntry(EntryKind.Expense, "Mercado", 20000, D(2026, 3, 8), space.Azul.Id, EntryStatus.Settled);
        partCarla.ChangeSettlementTag(carla.Id);
        partCarla.ChangeSplitGroup(group);
        await Factory.SeedAsync(space.Account, bruna, carla, partBruna, partCarla);

        var body = await CandidatesAsync(space);

        Keys(body).ShouldNotContain(k => k.Contains("tim"), "a recorrência já gera o ano novo (005)");
        Keys(body).Length.ShouldBe(2);
        var parts = body.GetProperty("rows").EnumerateArray().ToList();
        parts.Select(p => p.GetProperty("settlementTagName").GetString()).ShouldBe(["Bruna", "Carla"], ignoreOrder: true);
        parts.Single(p => p.GetProperty("settlementTagName").GetString() == "Bruna")
            .GetProperty("cells")[2].GetProperty("plannedCents").GetInt64().ShouldBe(30000);
        parts.Single(p => p.GetProperty("settlementTagName").GetString() == "Carla")
            .GetProperty("cells")[2].GetProperty("plannedCents").GetInt64().ShouldBe(20000);
    }

    [Fact]
    public async Task US3_2_copia_as_linhas_marcadas_como_Previstas_comuns_no_mesmo_mes_dia_carteira_e_tags()
    {
        var space = await SpaceAsync();
        var viagem = Tag.Create("Viagem", "#1E88E5");
        var natal = NewEntry(EntryKind.Expense, "Natal", 120000, D(2026, 12, 20), space.Verde.Id, EntryStatus.Settled);
        var decimo = NewEntry(EntryKind.Income, "13º salário", 150000, D(2026, 12, 5), space.Azul.Id, EntryStatus.Settled);
        var ignorado = NewEntry(EntryKind.Expense, "IPVA", 90000, D(2026, 2, 3), space.Azul.Id, EntryStatus.Settled);
        await Factory.SeedAsync(space.Account, viagem, natal, decimo, ignorado, new EntryTag(natal.Id, viagem.Id));

        var response = await OkAsync(await PostCopy(space, ["expense:natal", "income:13º salario"]));

        response.GetProperty("created").GetInt32().ShouldBe(2);
        response.GetProperty("skipped").GetInt32().ShouldBe(0);
        var copies = await EntriesOfYearAsync(space, 2027);
        copies.Select(e => e.Description).ShouldBe(["13º salário", "Natal"], "IPVA não foi marcada");
        var copiedNatal = copies.Single(e => e.Description == "Natal");
        copiedNatal.Status.ShouldBe(EntryStatus.Planned);
        copiedNatal.AmountCents.ShouldBe(120000);
        copiedNatal.EntryDate.ShouldBe(D(2027, 12, 20));
        copiedNatal.ReferenceMonth.ShouldBe(D(2027, 12, 1));
        copiedNatal.WalletId.ShouldBe(space.Verde.Id);
        copiedNatal.RecurrenceId.ShouldBeNull("sem vínculo com a origem");
        copiedNatal.SplitGroupId.ShouldBeNull();
        var links = await WithDbAsync(Factory, space.Account, (db, _) =>
            db.Set<EntryTag>().AsNoTracking().Where(l => l.EntryId == copiedNatal.Id).Select(l => l.TagId).ToListAsync());
        links.ShouldBe([viagem.Id]);
        copies.Single(e => e.Description == "13º salário").EntryDate.ShouldBe(D(2027, 12, 5));
        (await EntriesOfYearAsync(space, 2026)).Count.ShouldBe(3, "a origem não muda");
    }

    [Fact]
    public async Task US3_2_carteira_arquivada_da_origem_cai_na_principal_e_tag_arquivada_nao_e_levada()
    {
        var space = await SpaceAsync();
        var carla = Tag.Create("Carla", "#8E24AA");
        var school = NewEntry(EntryKind.Expense, "Escola", 90000, D(2026, 2, 5), space.Verde.Id, EntryStatus.Settled);
        await Factory.SeedAsync(space.Account, carla, school, new EntryTag(school.Id, carla.Id));
        (await space.Http.PostAsync($"/api/v1/wallets/{space.Verde.Id}/archive", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await space.Http.PostAsync($"/api/v1/tags/{carla.Id}/archive", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await OkAsync(await PostCopy(space, ["expense:escola"]));

        var copy = (await EntriesOfYearAsync(space, 2027)).Single();
        copy.WalletId.ShouldBe(space.Azul.Id, "a carteira principal");
        var links = await WithDbAsync(Factory, space.Account, (db, _) =>
            db.Set<EntryTag>().AsNoTracking().Where(l => l.EntryId == copy.Id).ToListAsync());
        links.ShouldBeEmpty();
    }

    [Fact]
    public async Task US3_2_RT_03_dia_inexistente_no_ano_novo_vai_para_o_ultimo_dia_do_mes()
    {
        var space = await SpaceAsync();
        await Factory.SeedAsync(space.Account, NewEntry(EntryKind.Expense, "Seguro", 50000, D(2028, 2, 29), space.Azul.Id, EntryStatus.Planned));

        await OkAsync(await PostCopy(space, ["expense:seguro"], url: "/api/v1/years/2029/planning/copy"));

        var copy = (await EntriesOfYearAsync(space, 2029)).Single();
        copy.EntryDate.ShouldBe(D(2029, 2, 28));
        copy.ReferenceMonth.ShouldBe(D(2029, 2, 1));
    }

    [Fact]
    public async Task US3_2_meses_que_ja_tem_valor_na_linha_de_destino_sao_pulados_e_informados()
    {
        var space = await SpaceAsync();
        await Factory.SeedAsync(
            space.Account,
            NewEntry(EntryKind.Expense, "Natal", 120000, D(2026, 12, 20), space.Azul.Id, EntryStatus.Settled),
            NewEntry(EntryKind.Expense, "Natal", 30000, D(2026, 11, 20), space.Azul.Id, EntryStatus.Settled),
            NewEntry(EntryKind.Expense, "natal", 99900, D(2027, 12, 1), space.Azul.Id, EntryStatus.Planned));

        var response = await OkAsync(await PostCopy(space, ["expense:natal"]));

        response.GetProperty("created").GetInt32().ShouldBe(1);
        response.GetProperty("skipped").GetInt32().ShouldBe(1);
        var copies = await EntriesOfYearAsync(space, 2027);
        copies.Select(e => (e.ReferenceMonth.Month, e.AmountCents)).ShouldBe([(11, 30000L), (12, 99900L)], "dezembro manteve o valor que já existia");

        var again = await OkAsync(await PostCopy(space, ["expense:natal"]));
        again.GetProperty("created").GetInt32().ShouldBe(0, "copiar de novo não duplica");
        again.GetProperty("skipped").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task US3_3_copia_so_a_parte_de_divisao_escolhida_pela_tag_de_acerto()
    {
        var space = await SpaceAsync();
        var bruna = Tag.Create("Bruna", "#E53935", isSettlement: true);
        var carla = Tag.Create("Carla", "#8E24AA", isSettlement: true);
        var partBruna = NewEntry(EntryKind.Expense, "Mercado", 30000, D(2026, 3, 8), space.Azul.Id, EntryStatus.Settled);
        partBruna.ChangeSettlementTag(bruna.Id);
        var partCarla = NewEntry(EntryKind.Expense, "Mercado", 20000, D(2026, 3, 8), space.Azul.Id, EntryStatus.Settled);
        partCarla.ChangeSettlementTag(carla.Id);
        await Factory.SeedAsync(space.Account, bruna, carla, partBruna, partCarla);

        var key = $"expense:mercado:{carla.Id}";
        var response = await OkAsync(await PostCopy(space, [key]));

        response.GetProperty("created").GetInt32().ShouldBe(1);
        var copy = (await EntriesOfYearAsync(space, 2027)).Single();
        copy.AmountCents.ShouldBe(20000);
        copy.SettlementTagId.ShouldBe(carla.Id);
    }

    [Fact]
    public async Task US3_4_copia_as_estimativas_de_fatura_do_mesmo_mes_para_faturas_sem_estimativa()
    {
        var space = await SpaceAsync();
        foreach (var (month, amount) in new[] { ("2026-05", "800"), ("2026-06", "650,50") })
        {
            (await space.Http.PutAsJsonAsync($"/api/v1/credit-cards/{space.Card.Id}/invoices/{month}/estimate", new { amount }))
                .StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await space.Http.PutAsJsonAsync($"/api/v1/credit-cards/{space.Card.Id}/invoices/2027-06/estimate", new { amount = "700" }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var candidates = await CandidatesAsync(space);
        var card = candidates.GetProperty("estimates").EnumerateArray().Single();
        card.GetProperty("creditCardName").GetString().ShouldBe("Azul");
        card.GetProperty("months").EnumerateArray().Select(m => (m.GetProperty("month").GetString(), m.GetProperty("estimateCents").GetInt64()))
            .ShouldBe([("2026-05", 80000L), ("2026-06", 65050L)]);

        var response = await OkAsync(await PostCopy(space, [], copyEstimates: true));

        response.GetProperty("created").GetInt32().ShouldBe(1);
        response.GetProperty("skipped").GetInt32().ShouldBe(1, "junho de 2027 já tinha estimativa");
        var grid = await space.Http.GetFromJsonAsync<JsonElement>("/api/v1/years/2027/planning");
        var cells = grid.GetProperty("invoiceRows").EnumerateArray().Single().GetProperty("cells").EnumerateArray().ToList();
        cells[4].GetProperty("estimateCents").GetInt64().ShouldBe(80000);
        cells[5].GetProperty("estimateCents").GetInt64().ShouldBe(70000);
    }

    [Fact]
    public async Task Sem_marcar_linhas_nem_estimativas_nada_e_criado_e_responde_422()
    {
        var space = await SpaceAsync();
        await SeedNatalAnd13Async(space);

        var response = await PostCopy(space, []);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await EntriesOfYearAsync(space, 2027)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Chave_inexistente_no_ano_anterior_responde_422_sem_criar_nenhuma()
    {
        var space = await SpaceAsync();
        await SeedNatalAnd13Async(space);

        var response = await PostCopy(space, ["expense:natal", "expense:nao-existe"]);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await EntriesOfYearAsync(space, 2027)).ShouldBeEmpty("tudo ou nada");
    }

    [Fact]
    public async Task Ano_fora_de_2000_a_2100_responde_422_na_lista_e_na_copia()
    {
        var space = await SpaceAsync();

        (await space.Http.GetAsync("/api/v1/years/1999/planning/copy-candidates")).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await PostCopy(space, ["expense:natal"], url: "/api/v1/years/2101/planning/copy")).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Dados_de_outro_espaco_nao_aparecem_na_lista()
    {
        var lia = await SpaceAsync();
        await SeedNatalAnd13Async(lia);
        var leo = await SpaceAsync("Léo");

        Keys(await CandidatesAsync(leo)).ShouldBeEmpty();
        (await CandidatesAsync(leo)).GetProperty("estimates").GetArrayLength().ShouldBe(0);
    }
}
