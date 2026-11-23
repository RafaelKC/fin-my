using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Api.Tests.Cards;
using FinMy.Api.Tests.Support;
using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Common;
using static FinMy.Api.Tests.Cards.CardTestSupport;

namespace FinMy.Api.Tests.Months;

/// <summary>
/// Faturas na visão do mês (003 T010, research R8, FR-021..FR-023). Exemplo da US5 em março: hoje 20/03/2026, cartão Azul
/// (fecha 18, vence 25, paga pela carteira Azul), fatura 2026-03 com total R$ 200,00 e parcela 3/3 de R$ 33,33 em abril.
/// </summary>
public class MonthInvoicesTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 3, 20, 15, 0, 0, TimeSpan.Zero));

    protected override IClock? Clock => _clock;

    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?> { ["RateLimiting:Auth:PermitLimit"] = "100000" };

    private sealed record Space(HttpClient Http, WalletRefDto Azul, WalletRefDto Verde, CreditCardDto Card);

    private async Task<Space> SpaceAsync(bool withExample = true)
    {
        _clock.UtcNow = DateTimeOffset.UtcNow;
        var (http, _) = await NewSpaceAsync(Factory);
        var response = await http.PostAsJsonAsync(
            "/api/v1/wallets",
            new { name = "Azul", type = "checking", color = "#1E88E5", initialBalance = "2000", initialBalanceDate = "2026-01-01" });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var azul = (await response.Content.ReadFromJsonAsync<WalletRefDto>(JsonDefaults.Options))!;
        var verde = await CreateWalletAsync(http, "Verde");
        var card = await CreateCardAsync(http, "Azul", azul.Id);
        _clock.UtcNow = new DateTimeOffset(2026, 3, 20, 15, 0, 0, TimeSpan.Zero);

        if (withExample)
        {
            await PurchaseAsync(http, card, "Uber", "12,95", "2026-03-10");
            await PurchaseAsync(http, card, "Casa China", "33,33", "2026-01-28", new { total = 3, mode = "in_progress", nextNumber = 2, firstInvoiceMonth = "2026-03" });
            await PurchaseAsync(http, card, "Shopee", "44", "2026-03-12");
            await PurchaseAsync(http, card, "Estorno Óculos", "-99,90", "2026-03-14");
            await PurchaseAsync(http, card, "Lanche", "209,62", "2026-03-16");
        }

        return new Space(http, azul, verde, card);
    }

    private static async Task PurchaseAsync(HttpClient http, CreditCardDto card, string description, string amount, string date, object? installments = null)
    {
        var response = await http.PostAsJsonAsync("/api/v1/card-purchases", new { description, amount, date, creditCardId = card.Id, installments });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private static async Task PayAsync(Space s, object body)
    {
        var response = await s.Http.PostAsJsonAsync($"{CardsUrl}/{s.Card.Id}/invoices/2026-03/payments", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonElement> MonthAsync(Space s, string month = "2026-03", params Guid[] walletIds)
    {
        var query = walletIds.Length == 0 ? string.Empty : $"?walletIds={string.Join(',', walletIds)}";
        return await s.Http.GetFromJsonAsync<JsonElement>($"/api/v1/months/{month}{query}");
    }

    private static long Cents(JsonElement element, string column, string field) => element.GetProperty(column).GetProperty(field).GetInt64();

    private static JsonElement Invoices(JsonElement month) => month.GetProperty("invoices");

    [Fact]
    public async Task US5_exemplo_na_visao_de_marco_mostra_a_fatura_Azul_fechada_e_soma_200_nas_saidas_do_Previsto()
    {
        var space = await SpaceAsync();

        var march = await MonthAsync(space);

        var line = Invoices(march).EnumerateArray().Single();
        line.GetProperty("creditCardName").GetString().ShouldBe("Azul");
        line.GetProperty("creditCardId").GetGuid().ShouldBe(space.Card.Id);
        line.GetProperty("dueMonth").GetString().ShouldBe("2026-03");
        line.GetProperty("closingDate").GetString().ShouldBe("2026-03-18");
        line.GetProperty("dueDate").GetString().ShouldBe("2026-03-25");
        line.GetProperty("status").GetString().ShouldBe("closed");
        line.GetProperty("totalCents").GetInt64().ShouldBe(20000);
        line.GetProperty("paidCents").GetInt64().ShouldBe(0);
        line.GetProperty("remainingCents").GetInt64().ShouldBe(20000);
        line.GetProperty("plannedCents").GetInt64().ShouldBe(20000);
        line.GetProperty("actualCents").GetInt64().ShouldBe(0);
        line.GetProperty("id").GetGuid().ShouldNotBe(Guid.Empty);
        Cents(march, "planned", "expenseCents").ShouldBe(20000);
        Cents(march, "planned", "finalBalanceCents").ShouldBe(180000);
        Cents(march, "actual", "expenseCents").ShouldBe(0);
        Cents(march, "actual", "finalBalanceCents").ShouldBe(200000);
    }

    [Fact]
    public async Task US5_1_fatura_paga_mantem_o_Previsto_e_leva_o_Realizado_para_200()
    {
        var space = await SpaceAsync();
        await PayAsync(space, new { });

        var march = await MonthAsync(space);

        var line = Invoices(march).EnumerateArray().Single();
        line.GetProperty("status").GetString().ShouldBe("paid");
        line.GetProperty("paidCents").GetInt64().ShouldBe(20000);
        line.GetProperty("plannedCents").GetInt64().ShouldBe(20000);
        line.GetProperty("actualCents").GetInt64().ShouldBe(20000);
        Cents(march, "planned", "expenseCents").ShouldBe(20000);
        Cents(march, "actual", "expenseCents").ShouldBe(20000);
        Cents(march, "actual", "finalBalanceCents").ShouldBe(180000);
    }

    [Fact]
    public async Task US5_2_pagamento_parcial_em_marco_tem_Previsto_200_e_Realizado_150()
    {
        var space = await SpaceAsync();
        await PayAsync(space, new { amount = "150" });

        var march = await MonthAsync(space);

        var line = Invoices(march).EnumerateArray().Single();
        line.GetProperty("status").GetString().ShouldBe("closed");
        line.GetProperty("remainingCents").GetInt64().ShouldBe(5000);
        Cents(march, "planned", "expenseCents").ShouldBe(20000);
        Cents(march, "actual", "expenseCents").ShouldBe(15000);
    }

    [Fact]
    public async Task FR_023_filtro_por_carteira_recorta_a_fatura_e_a_soma_das_partes_fecha_com_o_valor_sem_filtro()
    {
        var space = await SpaceAsync();
        await PayAsync(space, new { amount = "100", walletId = space.Azul.Id });
        await PayAsync(space, new { amount = "50", walletId = space.Verde.Id });

        var all = await MonthAsync(space);
        var azul = await MonthAsync(space, "2026-03", space.Azul.Id);
        var verde = await MonthAsync(space, "2026-03", space.Verde.Id);

        // Azul: pagou 100,00 e é a carteira de pagamento (restante 50,00); Verde: só os 50,00 que pagou.
        var azulLine = Invoices(azul).EnumerateArray().Single();
        azulLine.GetProperty("plannedCents").GetInt64().ShouldBe(15000);
        azulLine.GetProperty("actualCents").GetInt64().ShouldBe(10000);
        azulLine.GetProperty("totalCents").GetInt64().ShouldBe(20000);
        var verdeLine = Invoices(verde).EnumerateArray().Single();
        verdeLine.GetProperty("plannedCents").GetInt64().ShouldBe(5000);
        verdeLine.GetProperty("actualCents").GetInt64().ShouldBe(5000);

        foreach (var column in new[] { "planned", "actual" })
        {
            (Cents(azul, column, "expenseCents") + Cents(verde, column, "expenseCents")).ShouldBe(Cents(all, column, "expenseCents"));
        }

        Cents(all, "planned", "expenseCents").ShouldBe(20000);
        Cents(all, "actual", "expenseCents").ShouldBe(15000);
    }

    [Fact]
    public async Task Filtro_por_carteira_sem_relacao_com_a_fatura_nao_traz_a_linha_nem_valor()
    {
        var space = await SpaceAsync();
        await PayAsync(space, new { amount = "50", walletId = space.Azul.Id });

        var verde = await MonthAsync(space, "2026-03", space.Verde.Id);

        Invoices(verde).GetArrayLength().ShouldBe(0);
        Cents(verde, "planned", "expenseCents").ShouldBe(0);
        Cents(verde, "actual", "expenseCents").ShouldBe(0);
    }

    [Fact]
    public async Task Fatura_aparece_so_no_mes_de_vencimento_e_o_valor_previsto_e_o_pago_pesam_no_saldo_inicial_do_mes_seguinte()
    {
        var space = await SpaceAsync();
        await PayAsync(space, new { amount = "150" });

        var april = await MonthAsync(space, "2026-04");

        // A parcela 3/3 (R$ 33,33) vence em abril; a de março não volta a aparecer.
        var line = Invoices(april).EnumerateArray().Single();
        line.GetProperty("dueMonth").GetString().ShouldBe("2026-04");
        line.GetProperty("totalCents").GetInt64().ShouldBe(3333);
        line.GetProperty("status").GetString().ShouldBe("open");
        Cents(april, "planned", "expenseCents").ShouldBe(3333);
        Cents(april, "planned", "openingBalanceCents").ShouldBe(180000);
        Cents(april, "actual", "openingBalanceCents").ShouldBe(185000);
    }

    [Fact]
    public async Task Fatura_sem_valor_e_sem_pagamento_nao_aparece_e_com_credito_so_aparece_com_total_diferente_de_zero()
    {
        var space = await SpaceAsync(withExample: false);

        (await MonthAsync(space)).GetProperty("invoices").GetArrayLength().ShouldBe(0);

        await PurchaseAsync(space.Http, space.Card, "Estorno Óculos", "-99,90", "2026-03-14");

        var march = await MonthAsync(space);
        var line = Invoices(march).EnumerateArray().Single();
        line.GetProperty("totalCents").GetInt64().ShouldBe(-9990);
        line.GetProperty("status").GetString().ShouldBe("no_amount_due");
        line.GetProperty("plannedCents").GetInt64().ShouldBe(0);
        Cents(march, "planned", "expenseCents").ShouldBe(0);
    }

    [Fact]
    public async Task Fatura_de_cartao_arquivado_continua_na_visao_do_mes()
    {
        var space = await SpaceAsync();
        var card = await GetCardAsync(space.Http, space.Card.Id);
        (await space.Http.PostAsync($"{CardsUrl}/{card.Id}/archive", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var march = await MonthAsync(space);

        Invoices(march).EnumerateArray().Single().GetProperty("totalCents").GetInt64().ShouldBe(20000);
        Cents(march, "planned", "expenseCents").ShouldBe(20000);
    }
}
