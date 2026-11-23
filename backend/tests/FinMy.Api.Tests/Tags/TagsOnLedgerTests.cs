using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Api.Tests.Cards;
using FinMy.Api.Tests.Entries;
using FinMy.Api.Tests.Support;
using FinMy.Application.Tests.Infrastructure;
using static FinMy.Api.Tests.Cards.CardTestSupport;

namespace FinMy.Api.Tests.Tags;

/// <summary>
/// <c>tagIds</c> em Entradas, Saídas e Compras e filtros por tag (004 US1.4, US1.5, US2.4, SC-004; research R1 e R6).
/// Dinheiro e datas fictícios; cartão Azul fecha dia 18 e vence dia 25.
/// </summary>
public class TagsOnLedgerTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?> { ["RateLimiting:Auth:PermitLimit"] = "100000" };

    private sealed record Space(HttpClient Http, TestAccount Account, Guid WalletId, Guid CardId);

    private async Task<Space> NewSpaceAsync(string name = "Lia")
    {
        var (http, account) = await CardTestSupport.NewSpaceAsync(Factory, name);
        var wallet = await CreateWalletAsync(http, "Azul");
        var card = await CreateCardAsync(http, "Azul", wallet.Id);
        return new Space(http, account, wallet.Id, card.Id);
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

    private static Task<HttpResponseMessage> PostEntryAsync(Space s, string description, string amount, string date, string kind = "expense", params Guid[]? tagIds) =>
        s.Http.PostAsJsonAsync("/api/v1/entries", new { kind, description, amount, date, walletId = s.WalletId, tagIds });

    private static async Task<JsonElement> CreateEntryAsync(Space s, string description, string amount, string date, string kind = "expense", params Guid[]? tagIds) =>
        await OkAsync(await PostEntryAsync(s, description, amount, date, kind, tagIds), HttpStatusCode.Created);

    private static async Task<JsonElement> CreatePurchaseAsync(
        Space s, string description, string amount, string date, Guid[]? tagIds = null, object? installments = null, string? nature = null)
    {
        var body = await OkAsync(
            await s.Http.PostAsJsonAsync("/api/v1/card-purchases", new { description, amount, date, creditCardId = s.CardId, tagIds, installments, nature }),
            HttpStatusCode.Created);
        return body.GetProperty("purchases")[0];
    }

    private static async Task TransferAsync(Space s, string date, string description)
    {
        var verde = await CreateWalletAsync(s.Http, $"Verde {date}");
        var response = await s.Http.PostAsJsonAsync("/api/v1/transfers", new { fromWalletId = s.WalletId, toWalletId = verde.Id, amount = "1,00", date, description });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private static List<Guid> Ids(JsonElement array) => array.EnumerateArray().Select(e => e.GetGuid()).Order().ToList();

    private static List<Guid> Sorted(params Guid[] ids) => ids.Order().ToList();

    private static async Task<string?> CodeAsync(Task<HttpResponseMessage> response) => await (await response).CodeAsync();

    // --- Entradas e Saídas: tagIds e settlementTagId (R1) ---

    [Fact]
    public async Task Cria_lancamento_separando_a_tag_de_acerto_das_comuns_e_edita_trocando_as_tags()
    {
        var s = await NewSpaceAsync();
        var bruna = await TagAsync(s.Http, "Bruna", isSettlement: true);
        var mercado = await TagAsync(s.Http, "Mercado");
        var farmacia = await TagAsync(s.Http, "Farmácia");

        var created = await CreateEntryAsync(s, "Mercado da Bruna", "80,00", "2026-03-10", "expense", mercado, bruna);

        created.GetProperty("settlementTagId").GetGuid().ShouldBe(bruna);
        Ids(created.GetProperty("tagIds")).ShouldBe(Sorted(bruna, mercado));
        var id = created.GetProperty("id").GetGuid();

        var read = await OkAsync(await s.Http.GetAsync($"/api/v1/entries/{id}"));
        Ids(read.GetProperty("tagIds")).ShouldBe(Sorted(bruna, mercado));

        var version = created.GetProperty("version").GetInt64();
        var edited = await OkAsync(await s.Http.PutAsJsonAsync($"/api/v1/entries/{id}", new
        {
            kind = "expense",
            description = "Mercado da Bruna",
            amount = "80,00",
            date = "2026-03-10",
            walletId = s.WalletId,
            status = "settled",
            version,
            tagIds = new[] { farmacia },
        }));
        edited.GetProperty("settlementTagId").ValueKind.ShouldBe(JsonValueKind.Null);
        Ids(edited.GetProperty("tagIds")).ShouldBe([farmacia]);
    }

    [Fact]
    public async Task Edicao_sem_tagIds_nao_mexe_nas_tags_e_com_lista_vazia_remove_todas()
    {
        var s = await NewSpaceAsync();
        var bruna = await TagAsync(s.Http, "Bruna", isSettlement: true);
        var mercado = await TagAsync(s.Http, "Mercado");
        var created = await CreateEntryAsync(s, "Feira", "30,00", "2026-03-10", "expense", bruna, mercado);
        var id = created.GetProperty("id").GetGuid();

        var untouched = await OkAsync(await s.Http.PutAsJsonAsync($"/api/v1/entries/{id}", new
        {
            kind = "expense",
            description = "Feira nova",
            amount = "30,00",
            date = "2026-03-10",
            walletId = s.WalletId,
            status = "settled",
            version = created.GetProperty("version").GetInt64(),
        }));
        Ids(untouched.GetProperty("tagIds")).ShouldBe(Sorted(bruna, mercado));

        var cleared = await OkAsync(await s.Http.PutAsJsonAsync($"/api/v1/entries/{id}", new
        {
            kind = "expense",
            description = "Feira nova",
            amount = "30,00",
            date = "2026-03-10",
            walletId = s.WalletId,
            status = "settled",
            version = untouched.GetProperty("version").GetInt64(),
            tagIds = Array.Empty<Guid>(),
        }));
        cleared.GetProperty("tagIds").GetArrayLength().ShouldBe(0);
        cleared.GetProperty("settlementTagId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task US1_4_duas_tags_de_acerto_no_mesmo_lancamento_sao_recusadas_e_nada_e_salvo()
    {
        var s = await NewSpaceAsync();
        var bruna = await TagAsync(s.Http, "Bruna", isSettlement: true);
        var carla = await TagAsync(s.Http, "Carla", isSettlement: true);

        var response = await PostEntryAsync(s, "Pizza", "60,00", "2026-03-10", "expense", bruna, carla);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.CodeAsync()).ShouldBe("tags.multiple_settlement");
        (await OkAsync(await s.Http.GetAsync("/api/v1/entries/search?q=pizza"))).GetProperty("totalCount").GetInt32().ShouldBe(0);

        var cartao = await s.Http.PostAsJsonAsync("/api/v1/card-purchases", new { description = "Pizza", amount = "60,00", date = "2026-03-10", creditCardId = s.CardId, tagIds = new[] { bruna, carla } });
        cartao.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await cartao.CodeAsync()).ShouldBe("tags.multiple_settlement");
    }

    [Fact]
    public async Task Tag_arquivada_so_pode_ser_mantida_nunca_adicionada_e_tag_inexistente_e_404()
    {
        var s = await NewSpaceAsync();
        var velha = await TagAsync(s.Http, "Velha");
        var entry = await CreateEntryAsync(s, "Conta antiga", "10,00", "2026-03-10", "expense", velha);
        (await s.Http.PostAsync($"/api/v1/tags/{velha}/archive", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var adicionar = await PostEntryAsync(s, "Outra conta", "10,00", "2026-03-11", "expense", velha);
        adicionar.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await adicionar.CodeAsync()).ShouldBe("tag.archived");

        var manter = await OkAsync(await s.Http.PutAsJsonAsync($"/api/v1/entries/{entry.GetProperty("id").GetGuid()}", new
        {
            kind = "expense",
            description = "Conta antiga",
            amount = "12,00",
            date = "2026-03-10",
            walletId = s.WalletId,
            status = "settled",
            version = entry.GetProperty("version").GetInt64(),
            tagIds = new[] { velha },
        }));
        Ids(manter.GetProperty("tagIds")).ShouldBe([velha]);

        (await PostEntryAsync(s, "Fantasma", "10,00", "2026-03-11", "expense", Guid.NewGuid())).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Tag_de_outro_espaco_e_404_e_mais_de_20_tags_e_recusado()
    {
        var lia = await NewSpaceAsync();
        var leo = await NewSpaceAsync("Léo");
        var deLeo = await TagAsync(leo.Http, "Do Léo");

        (await PostEntryAsync(lia, "Tentativa", "10,00", "2026-03-10", "expense", deLeo)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var muitas = new List<Guid>();
        for (var i = 0; i < 21; i++)
        {
            muitas.Add(await TagAsync(lia.Http, $"Tag {i}"));
        }

        var response = await PostEntryAsync(lia, "Demais", "10,00", "2026-03-10", "expense", [.. muitas]);
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.CodeAsync()).ShouldBe("tags.too_many");
        (await PostEntryAsync(lia, "No limite", "10,00", "2026-03-10", "expense", [.. muitas.Take(20)])).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Duplicar_leva_as_tags_ativas_do_original()
    {
        var s = await NewSpaceAsync();
        var bruna = await TagAsync(s.Http, "Bruna", isSettlement: true);
        var mercado = await TagAsync(s.Http, "Mercado");
        var original = await CreateEntryAsync(s, "Feira", "30,00", "2026-03-10", "expense", bruna, mercado);

        var copy = await OkAsync(
            await s.Http.PostAsJsonAsync($"/api/v1/entries/{original.GetProperty("id").GetGuid()}/duplicate", new { date = "2026-04-10" }),
            HttpStatusCode.Created);

        copy.GetProperty("settlementTagId").GetGuid().ShouldBe(bruna);
        Ids(copy.GetProperty("tagIds")).ShouldBe(Sorted(bruna, mercado));
    }

    // --- Compras: criar, editar e parcela com escopo ---

    [Fact]
    public async Task Compra_a_vista_e_parcelada_nascem_com_as_tags_em_todas_as_parcelas()
    {
        var s = await NewSpaceAsync();
        var bruna = await TagAsync(s.Http, "Bruna", isSettlement: true);
        var presente = await TagAsync(s.Http, "Presente");

        var avista = await CreatePurchaseAsync(s, "Jacomar Niver", "239,80", "2026-03-10", [bruna, presente]);
        avista.GetProperty("settlementTagId").GetGuid().ShouldBe(bruna);
        Ids(avista.GetProperty("tagIds")).ShouldBe(Sorted(bruna, presente));

        var body = await OkAsync(
            await s.Http.PostAsJsonAsync("/api/v1/card-purchases", new
            {
                description = "MYNO",
                amount = "207,90",
                date = "2026-03-10",
                creditCardId = s.CardId,
                tagIds = new[] { bruna },
                installments = new { total = 4, mode = "total_amount" },
            }),
            HttpStatusCode.Created);
        var parcels = body.GetProperty("purchases").EnumerateArray().ToList();
        parcels.Count.ShouldBe(4);
        parcels.ShouldAllBe(p => p.GetProperty("settlementTagId").GetGuid() == bruna);
    }

    [Fact]
    public async Task Edicao_de_parcela_aplica_as_tags_so_na_parcela_ou_em_todas_conforme_o_escopo()
    {
        var s = await NewSpaceAsync();
        var bruna = await TagAsync(s.Http, "Bruna", isSettlement: true);
        var carla = await TagAsync(s.Http, "Carla", isSettlement: true);
        var created = await OkAsync(
            await s.Http.PostAsJsonAsync("/api/v1/card-purchases", new
            {
                description = "Sapato",
                amount = "300,00",
                date = "2026-03-10",
                creditCardId = s.CardId,
                tagIds = new[] { bruna },
                installments = new { total = 3, mode = "total_amount" },
            }),
            HttpStatusCode.Created);
        var parcels = created.GetProperty("purchases").EnumerateArray().ToList();

        Task<HttpResponseMessage> Put(JsonElement p, string scope, Guid[] tags) => s.Http.PutAsJsonAsync($"/api/v1/card-purchases/{p.GetProperty("id").GetGuid()}", new
        {
            description = p.GetProperty("description").GetString(),
            amount = "100,00",
            date = "2026-03-10",
            creditCardId = s.CardId,
            scope,
            tagIds = tags,
            version = p.GetProperty("version").GetInt64(),
        });

        var soEsta = await OkAsync(await Put(parcels[1], "this", [carla]));
        soEsta.GetProperty("settlementTagId").GetGuid().ShouldBe(carla);
        (await OkAsync(await s.Http.GetAsync($"/api/v1/card-purchases/{parcels[0].GetProperty("id").GetGuid()}")))
            .GetProperty("settlementTagId").GetGuid().ShouldBe(bruna);

        var todas = await OkAsync(await Put(soEsta, "all", [carla]));
        todas.GetProperty("settlementTagId").GetGuid().ShouldBe(carla);
        foreach (var parcel in parcels)
        {
            (await OkAsync(await s.Http.GetAsync($"/api/v1/card-purchases/{parcel.GetProperty("id").GetGuid()}")))
                .GetProperty("settlementTagId").GetGuid().ShouldBe(carla);
        }
    }

    [Fact]
    public async Task Alterar_o_parcelamento_recria_as_parcelas_sem_perder_as_tags()
    {
        var s = await NewSpaceAsync();
        var bruna = await TagAsync(s.Http, "Bruna", isSettlement: true);
        var presente = await TagAsync(s.Http, "Presente");
        var created = await OkAsync(
            await s.Http.PostAsJsonAsync("/api/v1/card-purchases", new
            {
                description = "Bolsa",
                amount = "400,00",
                date = "2026-03-10",
                creditCardId = s.CardId,
                tagIds = new[] { bruna, presente },
                installments = new { total = 2, mode = "total_amount" },
            }),
            HttpStatusCode.Created);
        var planId = created.GetProperty("purchases")[0].GetProperty("installmentPlanId").GetGuid();

        var recreated = await OkAsync(await s.Http.PutAsJsonAsync($"/api/v1/installment-plans/{planId}", new { installmentTotal = 4, amount = "400,00", amountMode = "total_amount" }));

        recreated.GetArrayLength().ShouldBe(4);
        foreach (var parcel in recreated.EnumerateArray())
        {
            parcel.GetProperty("settlementTagId").GetGuid().ShouldBe(bruna);
            Ids(parcel.GetProperty("tagIds")).ShouldBe(Sorted(bruna, presente));
        }
    }

    // --- Sugestões com tagIds (R1) ---

    [Fact]
    public async Task Sugestoes_trazem_as_tags_do_lancamento_mais_recente_sem_as_arquivadas()
    {
        var s = await NewSpaceAsync();
        var bruna = await TagAsync(s.Http, "Bruna", isSettlement: true);
        var velha = await TagAsync(s.Http, "Velha");
        var nova = await TagAsync(s.Http, "Nova");
        await CreateEntryAsync(s, "Pizza", "50,00", "2026-02-10", "expense", velha);
        await CreateEntryAsync(s, "Pizza", "60,00", "2026-03-10", "expense", bruna, velha, nova);
        await CreateEntryAsync(s, "Pizza", "70,00", "2026-03-11", "income", nova);
        (await s.Http.PostAsync($"/api/v1/tags/{velha}/archive", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var suggestions = await OkAsync(await s.Http.GetAsync("/api/v1/entries/suggestions?q=pizza&kind=expense"));
        Ids(suggestions[0].GetProperty("tagIds")).ShouldBe(Sorted(bruna, nova));

        await CreatePurchaseAsync(s, "Livraria", "90,00", "2026-03-12", [nova]);
        var purchaseSuggestions = await OkAsync(await s.Http.GetAsync("/api/v1/entries/suggestions?q=livraria&kind=card_purchase"));
        Ids(purchaseSuggestions[0].GetProperty("tagIds")).ShouldBe([nova]);
    }

    // --- US1.5: filtro por tag na visão do mês ---

    [Fact]
    public async Task US1_5_visao_do_mes_filtra_entradas_saidas_e_compras_da_fatura_e_soma_os_totais_do_filtro()
    {
        var s = await NewSpaceAsync();
        var bruna = await TagAsync(s.Http, "Bruna", isSettlement: true);
        var carla = await TagAsync(s.Http, "Carla", isSettlement: true);
        var mercado = await TagAsync(s.Http, "Mercado");

        await CreateEntryAsync(s, "Pizza da Bruna", "60,00", "2026-03-12", "expense", bruna);
        await CreateEntryAsync(s, "Bruna me pagou", "150,00", "2026-03-15", "income", bruna);
        await CreateEntryAsync(s, "Feira", "40,00", "2026-03-16", "expense", mercado);
        await CreateEntryAsync(s, "Pizza da Carla", "25,00", "2026-03-13", "expense", carla);
        await CreatePurchaseAsync(s, "Jacomar", "119,90", "2026-03-10", [bruna]);
        await CreatePurchaseAsync(s, "Estorno Jacomar", "19,90", "2026-03-11", [bruna], nature: "credit");
        await CreatePurchaseAsync(s, "Sapato", "90,00", "2026-03-10", [carla]);
        await CreatePurchaseAsync(s, "Fatura de abril", "70,00", "2026-03-20", [bruna]); // fecha dia 18 → vence em abril
        await TransferAsync(s, "2026-03-14", "Transferência");

        var semFiltro = await OkAsync(await s.Http.GetAsync("/api/v1/months/2026-03"));
        semFiltro.GetProperty("purchases").GetArrayLength().ShouldBe(0);
        semFiltro.GetProperty("filterTotals").ValueKind.ShouldBe(JsonValueKind.Null);
        semFiltro.GetProperty("entries").GetArrayLength().ShouldBe(4);

        var filtrado = await OkAsync(await s.Http.GetAsync($"/api/v1/months/2026-03?tagIds={bruna}"));

        filtrado.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("description").GetString())
            .ShouldBe(["Pizza da Bruna", "Bruna me pagou"]);
        filtrado.GetProperty("purchases").EnumerateArray().Select(p => p.GetProperty("description").GetString())
            .ShouldBe(["Jacomar", "Estorno Jacomar"]);
        filtrado.GetProperty("transfers").GetArrayLength().ShouldBe(0);
        semFiltro.GetProperty("invoices").GetArrayLength().ShouldBeGreaterThan(0);
        filtrado.GetProperty("invoices").GetArrayLength().ShouldBe(0);
        var totals = filtrado.GetProperty("filterTotals");
        totals.GetProperty("incomeCents").GetInt64().ShouldBe(15000);
        totals.GetProperty("expenseCents").GetInt64().ShouldBe(6000);
        totals.GetProperty("purchasesNetCents").GetInt64().ShouldBe(10000);
        Ids(filtrado.GetProperty("entries")[0].GetProperty("tagIds")).ShouldBe([bruna]);

        // O resumo (Previsto/Realizado) e as pendências não mudam com o filtro.
        filtrado.GetProperty("planned").ToString().ShouldBe(semFiltro.GetProperty("planned").ToString());
        filtrado.GetProperty("actual").ToString().ShouldBe(semFiltro.GetProperty("actual").ToString());
        filtrado.GetProperty("pending").ToString().ShouldBe(semFiltro.GetProperty("pending").ToString());

        // Qualquer uma das tags: Bruna ou Mercado; e abril traz a compra que venceu lá.
        var ou = await OkAsync(await s.Http.GetAsync($"/api/v1/months/2026-03?tagIds={bruna},{mercado}"));
        ou.GetProperty("entries").GetArrayLength().ShouldBe(3);
        var abril = await OkAsync(await s.Http.GetAsync($"/api/v1/months/2026-04?tagIds={bruna}"));
        abril.GetProperty("purchases").EnumerateArray().Select(p => p.GetProperty("description").GetString()).ShouldBe(["Fatura de abril"]);
        abril.GetProperty("filterTotals").GetProperty("purchasesNetCents").GetInt64().ShouldBe(7000);
    }

    [Fact]
    public async Task Filtro_com_id_invalido_e_422_e_com_tag_de_outro_espaco_nao_traz_nada()
    {
        var lia = await NewSpaceAsync();
        var leo = await NewSpaceAsync("Léo");
        var deLeo = await TagAsync(leo.Http, "Do Léo");
        await CreateEntryAsync(leo, "Segredo", "10,00", "2026-03-10", "expense", deLeo);

        var invalido = await lia.Http.GetAsync("/api/v1/months/2026-03?tagIds=abc");
        invalido.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await invalido.CodeAsync()).ShouldBe("tagIds.invalid");

        var vazio = await OkAsync(await lia.Http.GetAsync($"/api/v1/months/2026-03?tagIds={deLeo}"));
        vazio.GetProperty("entries").GetArrayLength().ShouldBe(0);
        vazio.GetProperty("purchases").GetArrayLength().ShouldBe(0);
        vazio.GetProperty("filterTotals").GetProperty("expenseCents").GetInt64().ShouldBe(0);
    }

    // --- Fatura: filtro, total filtrado e subtotais por Tag de acerto (FR-011, US2.4, SC-004) ---

    [Fact]
    public async Task US2_4_e_SC_004_fatura_traz_subtotais_por_tag_de_acerto_que_somam_o_total()
    {
        var s = await NewSpaceAsync();
        var bruna = await TagAsync(s.Http, "Bruna", isSettlement: true);
        var carla = await TagAsync(s.Http, "Carla", isSettlement: true);
        var mercado = await TagAsync(s.Http, "Mercado");

        await CreatePurchaseAsync(s, "Jacomar", "119,90", "2026-03-10", [bruna, mercado]);
        await CreatePurchaseAsync(s, "Estorno Jacomar", "19,90", "2026-03-11", [bruna], nature: "credit");
        await CreatePurchaseAsync(s, "Sapato", "90,00", "2026-03-12", [carla]);
        await CreatePurchaseAsync(s, "Café", "12,10", "2026-03-13", [mercado]);
        await CreatePurchaseAsync(s, "Cinema", "40,00", "2026-03-14");

        var fatura = await OkAsync(await s.Http.GetAsync($"/api/v1/credit-cards/{s.CardId}/invoices/2026-03"));

        fatura.GetProperty("totalCents").GetInt64().ShouldBe(24210);
        var subtotals = fatura.GetProperty("settlementSubtotals").EnumerateArray()
            .Select(t => (Tag: t.GetProperty("tagId").ValueKind == JsonValueKind.Null ? (Guid?)null : t.GetProperty("tagId").GetGuid(), Cents: t.GetProperty("cents").GetInt64()))
            .ToList();
        subtotals.ShouldBe([(bruna, 10000L), (carla, 9000L), (null, 5210L)]);
        subtotals.Sum(t => t.Cents).ShouldBe(fatura.GetProperty("totalCents").GetInt64());
        fatura.GetProperty("filteredTotalCents").ValueKind.ShouldBe(JsonValueKind.Null);
        fatura.GetProperty("purchases").GetArrayLength().ShouldBe(5);
    }

    [Fact]
    public async Task Fatura_filtrada_por_tag_lista_so_as_compras_da_tag_com_total_filtrado_e_subtotais_inteiros()
    {
        var s = await NewSpaceAsync();
        var bruna = await TagAsync(s.Http, "Bruna", isSettlement: true);
        var mercado = await TagAsync(s.Http, "Mercado");
        await CreatePurchaseAsync(s, "Jacomar", "119,90", "2026-03-10", [bruna]);
        await CreatePurchaseAsync(s, "Estorno Jacomar", "19,90", "2026-03-11", [bruna], nature: "credit");
        await CreatePurchaseAsync(s, "Café", "12,10", "2026-03-13", [mercado]);
        await CreatePurchaseAsync(s, "Cinema", "40,00", "2026-03-14");

        var filtrada = await OkAsync(await s.Http.GetAsync($"/api/v1/credit-cards/{s.CardId}/invoices/2026-03?tagIds={bruna},{mercado}"));

        filtrada.GetProperty("purchases").EnumerateArray().Select(p => p.GetProperty("description").GetString())
            .ShouldBe(["Jacomar", "Estorno Jacomar", "Café"]);
        filtrada.GetProperty("filteredTotalCents").GetInt64().ShouldBe(11210);
        filtrada.GetProperty("totalCents").GetInt64().ShouldBe(15210, "o total da fatura não muda com o filtro");
        filtrada.GetProperty("settlementSubtotals").GetArrayLength().ShouldBe(2);

        var semFatura = await OkAsync(await s.Http.GetAsync($"/api/v1/credit-cards/{s.CardId}/invoices/2027-01?tagIds={bruna}"));
        semFatura.GetProperty("filteredTotalCents").GetInt64().ShouldBe(0);
        semFatura.GetProperty("settlementSubtotals").GetArrayLength().ShouldBe(0);
    }

    // --- Busca: filtro por tag e compras como card_purchase (R6) ---

    [Fact]
    public async Task Busca_inclui_compras_como_card_purchase_e_filtra_por_tag()
    {
        var s = await NewSpaceAsync();
        var bruna = await TagAsync(s.Http, "Bruna", isSettlement: true);
        await CreateEntryAsync(s, "Pizza", "60,00", "2026-03-12", "expense", bruna);
        await CreateEntryAsync(s, "Pizza do Léo", "30,00", "2026-03-13");
        await CreatePurchaseAsync(s, "Pizza no cartão", "45,00", "2026-03-14", [bruna]);
        await CreatePurchaseAsync(s, "Pizza estornada", "5,00", "2026-03-15", nature: "credit");
        await TransferAsync(s, "2026-03-16", "Pizza");

        var todos = await OkAsync(await s.Http.GetAsync("/api/v1/entries/search?q=pizza"));
        todos.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("type").GetString())
            .ShouldBe(["transfer", "card_purchase", "card_purchase", "expense", "expense"], ignoreOrder: true);
        todos.GetProperty("totalCents").GetInt64().ShouldBe(-60_00 - 30_00 - 45_00 + 5_00);

        var soCompras = await OkAsync(await s.Http.GetAsync("/api/v1/entries/search?kinds=card_purchase"));
        soCompras.GetProperty("totalCount").GetInt32().ShouldBe(2);
        var primeira = soCompras.GetProperty("items")[1].GetProperty("purchase");
        primeira.GetProperty("description").GetString().ShouldBe("Pizza no cartão");
        primeira.GetProperty("settlementTagId").GetGuid().ShouldBe(bruna);

        var porTag = await OkAsync(await s.Http.GetAsync($"/api/v1/entries/search?tagIds={bruna}"));
        porTag.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("type").GetString()).ShouldBe(["card_purchase", "expense"]);
        porTag.GetProperty("totalCents").GetInt64().ShouldBe(-60_00 - 45_00);

        // Compra não tem carteira nem situação: esses filtros a deixam de fora.
        var porCarteira = await OkAsync(await s.Http.GetAsync($"/api/v1/entries/search?walletIds={s.WalletId}&q=pizza"));
        porCarteira.GetProperty("items").EnumerateArray().ShouldNotContain(i => i.GetProperty("type").GetString() == "card_purchase");
    }
}
