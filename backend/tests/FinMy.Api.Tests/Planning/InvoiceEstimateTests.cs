using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Api.Tests.Cards;
using FinMy.Api.Tests.Support;
using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;
using static FinMy.Api.Tests.Cards.CardTestSupport;

namespace FinMy.Api.Tests.Planning;

/// <summary>
/// Estimativa de fatura (007 T001, research R1; US2.1–US2.3, US2.6, FR-011, FR-012). Exemplo da US2: cartão Azul (fecha 18, vence 25,
/// paga pela carteira Azul), fatura 2027-05 (fecha 18/05/2027) com estimativa de R$ 800,00.
/// </summary>
public class InvoiceEstimateTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private readonly FakeClock _clock = new(At(2027, 1, 10));

    protected override IClock? Clock => _clock;

    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?> { ["RateLimiting:Auth:PermitLimit"] = "100000" };

    private static DateTimeOffset At(int year, int month, int day) => new(year, month, day, 15, 0, 0, TimeSpan.Zero);

    private sealed record Space(HttpClient Http, WalletRefDto Azul, WalletRefDto Verde, CreditCardDto Card);

    private async Task<Space> SpaceAsync(string? limit = null)
    {
        _clock.UtcNow = DateTimeOffset.UtcNow;
        var (http, _) = await NewSpaceAsync(Factory);
        var response = await http.PostAsJsonAsync(
            "/api/v1/wallets",
            new { name = "Azul", type = "checking", color = "#1E88E5", initialBalance = "2000", initialBalanceDate = "2027-01-01" });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var azul = (await response.Content.ReadFromJsonAsync<WalletRefDto>(JsonDefaults.Options))!;
        var verde = await CreateWalletAsync(http, "Verde");
        var card = await CreateCardAsync(http, "Azul", azul.Id, limit: limit);
        _clock.UtcNow = At(2027, 1, 10);
        return new Space(http, azul, verde, card);
    }

    private static async Task PurchaseAsync(Space s, string amount, string date = "2027-04-20", string description = "Mercado Lia")
    {
        var response = await s.Http.PostAsJsonAsync(
            "/api/v1/card-purchases", new { description, amount, date, creditCardId = s.Card.Id });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private static Task<HttpResponseMessage> PutEstimateAsync(Space s, string? amount, string month = "2027-05") =>
        s.Http.PutAsJsonAsync($"{CardsUrl}/{s.Card.Id}/invoices/{month}/estimate", new { amount });

    private static async Task<JsonElement> EstimateAsync(Space s, string? amount, string month = "2027-05")
    {
        var response = await PutEstimateAsync(s, amount, month);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Task<JsonElement> MonthAsync(Space s, string month = "2027-05", params Guid[] walletIds)
    {
        var query = walletIds.Length == 0 ? string.Empty : $"?walletIds={string.Join(',', walletIds)}";
        return s.Http.GetFromJsonAsync<JsonElement>($"/api/v1/months/{month}{query}");
    }

    private static long Cents(JsonElement element, string column, string field) => element.GetProperty(column).GetProperty(field).GetInt64();

    [Fact]
    public async Task US2_1_antes_do_fechamento_a_linha_do_mes_mostra_previsto_800_e_lancado_205_53()
    {
        var space = await SpaceAsync();
        await PurchaseAsync(space, "205,53");

        var invoice = await EstimateAsync(space, "800");
        var may = await MonthAsync(space);

        invoice.GetProperty("estimateCents").GetInt64().ShouldBe(80000);
        invoice.GetProperty("estimateActive").GetBoolean().ShouldBeTrue();
        invoice.GetProperty("totalCents").GetInt64().ShouldBe(20553);
        var line = may.GetProperty("invoices").EnumerateArray().Single();
        line.GetProperty("plannedCents").GetInt64().ShouldBe(80000);
        line.GetProperty("totalCents").GetInt64().ShouldBe(20553);
        line.GetProperty("estimateCents").GetInt64().ShouldBe(80000);
        line.GetProperty("estimateActive").GetBoolean().ShouldBeTrue();
        line.GetProperty("actualCents").GetInt64().ShouldBe(0);
        Cents(may, "planned", "expenseCents").ShouldBe(80000);
        Cents(may, "actual", "expenseCents").ShouldBe(0);
    }

    [Fact]
    public async Task US2_1_estimativa_pesa_no_saldo_final_previsto_dos_meses_seguintes()
    {
        var space = await SpaceAsync();
        await EstimateAsync(space, "800");

        var june = await MonthAsync(space, "2027-06");

        Cents(june, "planned", "finalBalanceCents").ShouldBe(200000 - 80000);
        Cents(june, "planned", "expenseCents").ShouldBe(0);
        Cents(june, "actual", "finalBalanceCents").ShouldBe(200000);
    }

    [Fact]
    public async Task US2_2_total_lancado_acima_da_estimativa_antes_do_fechamento_vira_o_previsto()
    {
        var space = await SpaceAsync();
        await EstimateAsync(space, "800");
        await PurchaseAsync(space, "912,40");
        _clock.UtcNow = At(2027, 5, 10);

        var may = await MonthAsync(space);

        var line = may.GetProperty("invoices").EnumerateArray().Single();
        line.GetProperty("plannedCents").GetInt64().ShouldBe(91240);
        line.GetProperty("estimateActive").GetBoolean().ShouldBeTrue();
        Cents(may, "planned", "expenseCents").ShouldBe(91240);
    }

    [Fact]
    public async Task US2_3_depois_do_fechamento_a_estimativa_e_ignorada_mas_continua_visivel()
    {
        var space = await SpaceAsync();
        await PurchaseAsync(space, "640");
        await EstimateAsync(space, "800");
        _clock.UtcNow = At(2027, 5, 19);

        var may = await MonthAsync(space);
        var invoice = await space.Http.GetFromJsonAsync<JsonElement>($"{CardsUrl}/{space.Card.Id}/invoices/2027-05");

        var line = may.GetProperty("invoices").EnumerateArray().Single();
        line.GetProperty("plannedCents").GetInt64().ShouldBe(64000);
        line.GetProperty("estimateCents").GetInt64().ShouldBe(80000);
        line.GetProperty("estimateActive").GetBoolean().ShouldBeFalse();
        invoice.GetProperty("estimateCents").GetInt64().ShouldBe(80000);
        invoice.GetProperty("estimateActive").GetBoolean().ShouldBeFalse();
        Cents(may, "planned", "expenseCents").ShouldBe(64000);
    }

    [Fact]
    public async Task US2_3_no_dia_do_fechamento_a_estimativa_ja_nao_vale()
    {
        var space = await SpaceAsync();
        await PurchaseAsync(space, "640");
        await EstimateAsync(space, "800");

        _clock.UtcNow = At(2027, 5, 17);
        var before = (await MonthAsync(space)).GetProperty("invoices").EnumerateArray().Single();
        _clock.UtcNow = At(2027, 5, 18);
        var onClosing = (await MonthAsync(space)).GetProperty("invoices").EnumerateArray().Single();

        before.GetProperty("plannedCents").GetInt64().ShouldBe(80000);
        onClosing.GetProperty("plannedCents").GetInt64().ShouldBe(64000);
    }

    [Fact]
    public async Task US2_6_e_FR_012_estimativa_nao_mexe_em_limite_total_restante_situacao_nem_saldo_de_carteira()
    {
        var space = await SpaceAsync(limit: "5000");
        await PurchaseAsync(space, "205,53");
        var before = await GetCardAsync(space.Http, space.Card.Id);

        var invoice = await EstimateAsync(space, "800");
        var after = await GetCardAsync(space.Http, space.Card.Id);
        var may = await MonthAsync(space);

        after.AvailableLimitCents.ShouldBe(before.AvailableLimitCents);
        after.AvailableLimitCents.ShouldBe(500000 - 20553);
        invoice.GetProperty("totalCents").GetInt64().ShouldBe(20553);
        invoice.GetProperty("remainingCents").GetInt64().ShouldBe(20553);
        invoice.GetProperty("paidCents").GetInt64().ShouldBe(0);
        invoice.GetProperty("status").GetString().ShouldBe("open");
        var line = may.GetProperty("invoices").EnumerateArray().Single();
        line.GetProperty("remainingCents").GetInt64().ShouldBe(20553);
        line.GetProperty("status").GetString().ShouldBe("open");
        Cents(may, "actual", "finalBalanceCents").ShouldBe(200000);
        var wallets = await space.Http.GetFromJsonAsync<JsonElement>("/api/v1/wallets");
        wallets.GetRawText().ShouldNotContain("estimate");
    }

    [Fact]
    public async Task FR_012_estimativa_nao_entra_nos_subtotais_por_tag_de_acerto()
    {
        var space = await SpaceAsync();
        await PurchaseAsync(space, "100");

        var invoice = await EstimateAsync(space, "800");

        invoice.GetProperty("settlementSubtotals").EnumerateArray().Sum(t => t.GetProperty("cents").GetInt64()).ShouldBe(10000);
        invoice.GetProperty("purchases").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task FR_011_com_filtro_por_carteira_a_diferenca_vai_para_a_carteira_de_pagamento_do_cartao()
    {
        var space = await SpaceAsync();
        await PurchaseAsync(space, "205,53");
        await EstimateAsync(space, "800");

        var onlyAzul = await MonthAsync(space, walletIds: space.Azul.Id);
        var onlyVerde = await MonthAsync(space, walletIds: space.Verde.Id);
        var all = await MonthAsync(space);

        Cents(onlyAzul, "planned", "expenseCents").ShouldBe(80000);
        onlyAzul.GetProperty("invoices").EnumerateArray().Single().GetProperty("plannedCents").GetInt64().ShouldBe(80000);
        Cents(onlyVerde, "planned", "expenseCents").ShouldBe(0);
        onlyVerde.GetProperty("invoices").GetArrayLength().ShouldBe(0);
        (Cents(onlyAzul, "planned", "expenseCents") + Cents(onlyVerde, "planned", "expenseCents")).ShouldBe(Cents(all, "planned", "expenseCents"));
    }

    [Fact]
    public async Task FR_011_pagamento_com_outra_carteira_mantem_a_parte_paga_e_a_diferenca_na_carteira_de_pagamento()
    {
        var space = await SpaceAsync();
        await PurchaseAsync(space, "205,53");
        await EstimateAsync(space, "800");
        var pay = await space.Http.PostAsJsonAsync(
            $"{CardsUrl}/{space.Card.Id}/invoices/2027-05/payments", new { amount = "100", walletId = space.Verde.Id, date = "2027-01-10" });
        pay.StatusCode.ShouldBe(HttpStatusCode.Created, await pay.Content.ReadAsStringAsync());

        var onlyAzul = await MonthAsync(space, walletIds: space.Azul.Id);
        var onlyVerde = await MonthAsync(space, walletIds: space.Verde.Id);
        var all = await MonthAsync(space);

        Cents(onlyVerde, "planned", "expenseCents").ShouldBe(10000);
        Cents(onlyAzul, "planned", "expenseCents").ShouldBe(70000);
        Cents(all, "planned", "expenseCents").ShouldBe(80000);
        Cents(all, "actual", "expenseCents").ShouldBe(10000);
    }

    [Fact]
    public async Task Definir_estimativa_cria_a_linha_da_fatura_e_remover_volta_ao_estado_sem_estimativa()
    {
        var space = await SpaceAsync();

        var created = await EstimateAsync(space, "=1000-200");
        created.GetProperty("id").ValueKind.ShouldBe(JsonValueKind.String);
        created.GetProperty("estimateCents").GetInt64().ShouldBe(80000);
        created.GetProperty("totalCents").GetInt64().ShouldBe(0);

        var replaced = await EstimateAsync(space, "950,50");
        replaced.GetProperty("estimateCents").GetInt64().ShouldBe(95050);

        var removed = await EstimateAsync(space, null);
        removed.GetProperty("estimateCents").ValueKind.ShouldBe(JsonValueKind.Null);
        removed.GetProperty("estimateActive").GetBoolean().ShouldBeFalse();
        var may = await MonthAsync(space);
        may.GetProperty("invoices").GetArrayLength().ShouldBe(0);
        Cents(may, "planned", "expenseCents").ShouldBe(0);

        var empty = await EstimateAsync(space, "  ");
        empty.GetProperty("estimateCents").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Remover_estimativa_de_fatura_sem_linha_nao_cria_a_linha()
    {
        var space = await SpaceAsync();

        var invoice = await EstimateAsync(space, null, "2027-08");

        invoice.GetProperty("id").ValueKind.ShouldBe(JsonValueKind.Null);
        invoice.GetProperty("estimateCents").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Estimativa_aparece_na_visao_geral_do_cartao()
    {
        var space = await SpaceAsync();
        await EstimateAsync(space, "800", "2027-01");

        var card = await GetCardAsync(space.Http, space.Card.Id);

        card.CurrentInvoice!.EstimateCents.ShouldBe(80000);
        card.CurrentInvoice.EstimateActive.ShouldBeTrue();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("abc")]
    [InlineData("10000000")]
    public async Task Valor_invalido_responde_422_sem_gravar(string amount)
    {
        var space = await SpaceAsync();

        var response = await PutEstimateAsync(space, amount);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await EstimateAsync(space, null)).GetProperty("id").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Mes_invalido_responde_422_e_cartao_de_outro_espaco_responde_404()
    {
        var space = await SpaceAsync();
        _clock.UtcNow = DateTimeOffset.UtcNow;
        var (leo, _) = await NewSpaceAsync(Factory, "Léo");
        _clock.UtcNow = At(2027, 1, 10);

        var month = await PutEstimateAsync(space, "800", "2027-13");
        var other = await leo.PutAsJsonAsync($"{CardsUrl}/{space.Card.Id}/invoices/2027-05/estimate", new { amount = "800" });
        var unknown = await space.Http.PutAsJsonAsync($"{CardsUrl}/{Guid.NewGuid()}/invoices/2027-05/estimate", new { amount = "800" });

        month.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        other.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Sem_login_responde_401()
    {
        using var anonymous = Factory.CreateClient();

        var response = await anonymous.PutAsJsonAsync($"{CardsUrl}/{Guid.NewGuid()}/invoices/2027-05/estimate", new { amount = "800" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
