using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Api.Tests.Support;
using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Common;
using static FinMy.Api.Tests.Cards.CardTestSupport;

namespace FinMy.Api.Tests.Cards;

internal sealed record InvoicePaymentDto(Guid Id, Guid CreditCardId, string DueMonth, Guid WalletId, long AmountCents, DateOnly Date, long Version);

internal sealed record InvoiceDto(
    Guid CreditCardId, string DueMonth, DateOnly ClosingDate, DateOnly DueDate, Guid? Id, string Status,
    long TotalCents, long PaidCents, long RemainingCents, long ReconciledCents, bool CanCarryOver,
    List<PurchaseDto> Purchases, List<InvoicePaymentDto> Payments, string PreviousMonth, string NextMonth);

/// <summary>
/// Fatura e pagamentos pela API (003 US5.1–US5.7, US7.1; research R2, R4). O exemplo numérico da US5: hoje 20/03/2026, cartão
/// Azul (fecha 18, vence 25, paga pela carteira Azul com saldo atual R$ 2.000,00), fatura 2026-03 com total R$ 200,00.
/// </summary>
public class InvoiceExampleTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private const string PurchasesUrl = "/api/v1/card-purchases";

    private readonly FakeClock _clock = new(DateTimeOffset.UtcNow);

    protected override IClock? Clock => _clock;

    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?> { ["RateLimiting:Auth:PermitLimit"] = "100000" };

    private sealed record Space(HttpClient Http, TestAccount Account, WalletRefDto Wallet, CreditCardDto Card);

    private void SetToday(int year, int month, int day) => _clock.UtcNow = new DateTimeOffset(year, month, day, 15, 0, 0, TimeSpan.Zero);

    /// <summary>Espaço da Lia com a carteira Azul (saldo R$ 2.000,00) e o cartão Azul; hoje = 20/03/2026.</summary>
    private async Task<Space> AzulAsync(bool withExample = true)
    {
        _clock.UtcNow = DateTimeOffset.UtcNow;
        var (http, account) = await NewSpaceAsync(Factory);
        var response = await http.PostAsJsonAsync(
            "/api/v1/wallets",
            new { name = "Azul", type = "checking", color = "#1E88E5", initialBalance = "2000", initialBalanceDate = "2026-01-01" });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var wallet = (await response.Content.ReadFromJsonAsync<WalletRefDto>(JsonDefaults.Options))!;
        var card = await CreateCardAsync(http, "Azul", wallet.Id);
        SetToday(2026, 3, 20);

        if (withExample)
        {
            await PurchaseAsync(http, card, "Uber", "12,95", "2026-03-10");
            await PurchaseAsync(http, card, "Casa China", "33,33", "2026-01-28", new { total = 3, mode = "in_progress", nextNumber = 2, firstInvoiceMonth = "2026-03" });
            await PurchaseAsync(http, card, "Shopee", "44", "2026-03-12");
            await PurchaseAsync(http, card, "Estorno Óculos", "-99,90", "2026-03-14");
            await PurchaseAsync(http, card, "Lanche", "209,62", "2026-03-16");
        }

        return new Space(http, account, wallet, card);
    }

    private static async Task PurchaseAsync(HttpClient http, CreditCardDto card, string description, string amount, string date, object? installments = null)
    {
        var response = await http.PostAsJsonAsync(PurchasesUrl, new { description, amount, date, creditCardId = card.Id, installments });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private static string InvoiceUrl(Space s, string month = "2026-03") => $"{CardsUrl}/{s.Card.Id}/invoices/{month}";

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>(JsonDefaults.Options))!;
    }

    private static Task<InvoiceDto> GetInvoiceAsync(Space s, string month = "2026-03") =>
        s.Http.GetFromJsonAsync<InvoiceDto>(InvoiceUrl(s, month), JsonDefaults.Options)!;

    private static Task<HttpResponseMessage> PayAsync(Space s, object? body = null, string month = "2026-03") =>
        s.Http.PostAsJsonAsync($"{InvoiceUrl(s, month)}/payments", body ?? new { });

    private static async Task<long> BalanceAsync(Space s, Guid? walletId = null)
    {
        var wallet = await s.Http.GetFromJsonAsync<JsonElement>($"/api/v1/wallets/{walletId ?? s.Wallet.Id}");
        return wallet.GetProperty("currentBalanceCents").GetInt64();
    }

    private static async Task<JsonElement> MonthAsync(Space s, string month = "2026-03") =>
        await s.Http.GetFromJsonAsync<JsonElement>($"/api/v1/months/{month}");

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response) => (await ProblemAsync(response)).Code;

    // --- Exemplo numérico da US5 ---

    [Fact]
    public async Task US5_exemplo_numerico_total_200_fechada_e_compras_na_ordem_da_RT_12()
    {
        var space = await AzulAsync();

        var invoice = await GetInvoiceAsync(space);

        invoice.DueMonth.ShouldBe("2026-03");
        invoice.ClosingDate.ShouldBe(new DateOnly(2026, 3, 18));
        invoice.DueDate.ShouldBe(new DateOnly(2026, 3, 25));
        invoice.TotalCents.ShouldBe(20000);
        invoice.PaidCents.ShouldBe(0);
        invoice.RemainingCents.ShouldBe(20000);
        invoice.Status.ShouldBe("closed");
        invoice.ReconciledCents.ShouldBe(0);
        invoice.CanCarryOver.ShouldBeTrue();
        invoice.PreviousMonth.ShouldBe("2026-02");
        invoice.NextMonth.ShouldBe("2026-04");
        invoice.Payments.ShouldBeEmpty();
        invoice.Purchases.Select(p => p.DisplayDescription).ShouldBe(["Casa China 2/3", "Uber", "Shopee", "Estorno Óculos", "Lanche"]);
        invoice.Purchases.Select(p => p.Date).ShouldBe(invoice.Purchases.Select(p => p.Date).OrderBy(d => d));
        (await BalanceAsync(space)).ShouldBe(200000);
    }

    [Fact]
    public async Task Fatura_sem_linha_tem_total_zero_datas_calculadas_e_id_nulo()
    {
        var space = await AzulAsync(withExample: false);

        var invoice = await GetInvoiceAsync(space, "2026-08");

        invoice.Id.ShouldBeNull();
        invoice.Status.ShouldBe("no_amount_due");
        invoice.TotalCents.ShouldBe(0);
        invoice.RemainingCents.ShouldBe(0);
        invoice.ClosingDate.ShouldBe(new DateOnly(2026, 8, 18));
        invoice.DueDate.ShouldBe(new DateOnly(2026, 8, 25));
        invoice.CanCarryOver.ShouldBeFalse();
        invoice.Purchases.ShouldBeEmpty();
        invoice.PreviousMonth.ShouldBe("2026-07");
        invoice.NextMonth.ShouldBe("2026-09");
    }

    [Fact]
    public async Task Mes_invalido_e_recusado_e_cartao_de_outro_espaco_e_404()
    {
        _clock.UtcNow = DateTimeOffset.UtcNow;
        using var anonymous = Factory.CreateClient();
        var bruna = await anonymous.SignUpAsync(name: "Bruna");
        using var other = Factory.CreateClient().WithBearer(bruna.AccessToken);
        var space = await AzulAsync(withExample: false);

        var invalid = await space.Http.GetAsync(InvoiceUrl(space, "2026-13"));
        var foreign = await other.GetAsync(InvoiceUrl(space));
        var foreignPay = await other.PostAsJsonAsync($"{InvoiceUrl(space)}/payments", new { });

        invalid.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeOfAsync(invalid)).ShouldBe("month.invalid");
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        foreignPay.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // --- US5.1 e US5.6 ---

    [Fact]
    public async Task US5_1_caixinha_registra_pagamento_total_a_fatura_fica_paga_e_o_saldo_da_carteira_cai()
    {
        var space = await AzulAsync();

        var response = await PayAsync(space);

        var invoice = await ReadAsync<InvoiceDto>(response, HttpStatusCode.Created);
        invoice.Status.ShouldBe("paid");
        invoice.PaidCents.ShouldBe(20000);
        invoice.RemainingCents.ShouldBe(0);
        invoice.CanCarryOver.ShouldBeFalse();
        var payment = invoice.Payments.ShouldHaveSingleItem();
        payment.AmountCents.ShouldBe(20000);
        payment.Date.ShouldBe(new DateOnly(2026, 3, 20));
        payment.WalletId.ShouldBe(space.Wallet.Id);
        payment.DueMonth.ShouldBe("2026-03");
        payment.CreditCardId.ShouldBe(space.Card.Id);
        (await BalanceAsync(space)).ShouldBe(180000);

        // Saídas Realizado de março aumenta 200,00 (RT-07); o saldo final Realizado acompanha o saldo da carteira.
        var march = await MonthAsync(space);
        march.GetProperty("actual").GetProperty("expenseCents").GetInt64().ShouldBe(20000);
        march.GetProperty("actual").GetProperty("finalBalanceCents").GetInt64().ShouldBe(180000);
    }

    [Fact]
    public async Task US5_6_desmarcar_exclui_todos_os_pagamentos_o_saldo_volta_e_o_lote_pode_ser_desfeito()
    {
        var space = await AzulAsync();
        (await PayAsync(space, new { amount = "150" })).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await PayAsync(space, new { amount = "50", date = "2026-03-21" })).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await BalanceAsync(space)).ShouldBe(180000);

        var response = await space.Http.DeleteAsync($"{InvoiceUrl(space)}/payments");

        var batch = (await ReadAsync<DeletionDto>(response)).DeletionBatchId;
        var invoice = await GetInvoiceAsync(space);
        invoice.Payments.ShouldBeEmpty();
        invoice.PaidCents.ShouldBe(0);
        invoice.Status.ShouldBe("closed");
        (await BalanceAsync(space)).ShouldBe(200000);

        (await space.Http.PostAsync($"/api/v1/deletions/{batch}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetInvoiceAsync(space)).Payments.Count.ShouldBe(2);
        (await BalanceAsync(space)).ShouldBe(180000);
    }

    [Fact]
    public async Task Desmarcar_sem_pagamentos_e_recusado()
    {
        var space = await AzulAsync();

        var response = await space.Http.DeleteAsync($"{InvoiceUrl(space)}/payments");
        var noRow = await space.Http.DeleteAsync($"{InvoiceUrl(space, "2026-09")}/payments");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeOfAsync(response)).ShouldBe("invoice.no_payments");
        (await CodeOfAsync(noRow)).ShouldBe("invoice.no_payments");
    }

    // --- US5.2, US5.3 ---

    [Fact]
    public async Task US5_2_pagamento_parcial_deixa_restante_e_continua_fechada()
    {
        var space = await AzulAsync();

        var invoice = await ReadAsync<InvoiceDto>(await PayAsync(space, new { amount = "150" }), HttpStatusCode.Created);

        invoice.PaidCents.ShouldBe(15000);
        invoice.RemainingCents.ShouldBe(5000);
        invoice.Status.ShouldBe("closed");
        var actual = (await MonthAsync(space)).GetProperty("actual");
        actual.GetProperty("expenseCents").GetInt64().ShouldBe(15000);
    }

    [Fact]
    public async Task US5_3_depois_do_vencimento_com_restante_a_fatura_esta_vencida()
    {
        var space = await AzulAsync();
        (await PayAsync(space, new { amount = "150" })).StatusCode.ShouldBe(HttpStatusCode.Created);

        SetToday(2026, 3, 26);
        var invoice = await GetInvoiceAsync(space);

        invoice.Status.ShouldBe("overdue");
        invoice.RemainingCents.ShouldBe(5000);
    }

    // --- US5.4 ---

    [Fact]
    public async Task US5_4_levar_restante_cria_o_par_ligado_e_excluir_uma_exclui_as_duas()
    {
        var space = await AzulAsync();
        (await PayAsync(space, new { amount = "150" })).StatusCode.ShouldBe(HttpStatusCode.Created);
        SetToday(2026, 3, 26);

        var response = await space.Http.PostAsync($"{InvoiceUrl(space)}/carry-over", null);

        var march = await ReadAsync<InvoiceDto>(response, HttpStatusCode.Created);
        march.Status.ShouldBe("paid");
        march.TotalCents.ShouldBe(15000);
        march.RemainingCents.ShouldBe(0);
        march.CanCarryOver.ShouldBeFalse();
        var credit = march.Purchases.Single(p => p.Origin == "carry_over");
        credit.DisplayDescription.ShouldBe("Saldo levado para 04/2026");
        credit.Nature.ShouldBe("credit");
        credit.AmountCents.ShouldBe(5000);

        var april = await GetInvoiceAsync(space, "2026-04");
        var debit = april.Purchases.Single(p => p.Origin == "carry_over");
        debit.DisplayDescription.ShouldBe("Saldo da fatura 03/2026");
        debit.Nature.ShouldBe("debit");
        debit.AmountCents.ShouldBe(5000);
        april.TotalCents.ShouldBe(3333 + 5000);

        var deleted = await space.Http.DeleteAsync($"{PurchasesUrl}/{debit.Id}");
        deleted.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetInvoiceAsync(space, "2026-04")).Purchases.Select(p => p.DisplayDescription).ShouldBe(["Casa China 3/3"]);
        var afterDelete = await GetInvoiceAsync(space);
        afterDelete.Purchases.ShouldNotContain(p => p.Origin == "carry_over");
        afterDelete.TotalCents.ShouldBe(20000);
    }

    [Fact]
    public async Task Levar_restante_no_sentido_inverso_com_total_negativo()
    {
        var space = await AzulAsync(withExample: false);
        await PurchaseAsync(space.Http, space.Card, "Estorno Óculos", "-30", "2026-03-10");
        var before = await GetInvoiceAsync(space);
        before.TotalCents.ShouldBe(-3000);
        before.Status.ShouldBe("no_amount_due");
        before.CanCarryOver.ShouldBeTrue();

        var march = await ReadAsync<InvoiceDto>(await space.Http.PostAsync($"{InvoiceUrl(space)}/carry-over", null), HttpStatusCode.Created);

        var debit = march.Purchases.Single(p => p.Origin == "carry_over");
        debit.Nature.ShouldBe("debit");
        debit.AmountCents.ShouldBe(3000);
        march.TotalCents.ShouldBe(0);
        var april = await GetInvoiceAsync(space, "2026-04");
        var credit = april.Purchases.ShouldHaveSingleItem();
        credit.Nature.ShouldBe("credit");
        credit.AmountCents.ShouldBe(3000);
        april.TotalCents.ShouldBe(-3000);
    }

    [Fact]
    public async Task Levar_restante_antes_do_fechamento_ou_sem_restante_e_recusado()
    {
        var space = await AzulAsync();
        await PurchaseAsync(space.Http, space.Card, "Mercado", "10", "2026-03-20"); // cai em 2026-04 (aberta)
        (await PayAsync(space)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var open = await space.Http.PostAsync($"{InvoiceUrl(space, "2026-04")}/carry-over", null);
        var settled = await space.Http.PostAsync($"{InvoiceUrl(space)}/carry-over", null);
        var noRow = await space.Http.PostAsync($"{InvoiceUrl(space, "2026-09")}/carry-over", null);

        foreach (var response in new[] { open, settled, noRow })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            (await CodeOfAsync(response)).ShouldBe("invoice.carry_over_not_available");
        }

        (await GetInvoiceAsync(space, "2026-04")).CanCarryOver.ShouldBeFalse();
    }

    // --- US5.5 e R4 ---

    [Fact]
    public async Task US5_5_pagamento_acima_do_restante_e_recusado_com_o_valor_em_aberto()
    {
        var space = await AzulAsync();
        (await PayAsync(space, new { amount = "150" })).StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await PayAsync(space, new { amount = "60" });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await ProblemAsync(response);
        problem.Code.ShouldBe("invoice.payment_exceeds_remaining");
        problem.Errors!["amount"].ShouldBe(["O valor em aberto é R$ 50,00."]);
        (await GetInvoiceAsync(space)).Payments.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Pagamento_sem_valor_em_aberto_ou_em_fatura_sem_linha_e_recusado()
    {
        var space = await AzulAsync();
        (await PayAsync(space)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var paid = await PayAsync(space);
        var noRow = await PayAsync(space, month: "2026-09");

        foreach (var response in new[] { paid, noRow })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            (await CodeOfAsync(response)).ShouldBe("invoice.nothing_to_pay");
        }
    }

    [Fact]
    public async Task Pagamento_aceita_expressao_data_e_carteira_e_antecipado_na_fatura_aberta()
    {
        var space = await AzulAsync();
        var verde = await CreateWalletAsync(space.Http, "Verde");
        await PurchaseAsync(space.Http, space.Card, "Mercado", "100", "2026-03-20"); // fatura 2026-04, ainda aberta

        var response = await PayAsync(space, new { amount = "200/4", date = "2026-03-21", walletId = verde.Id }, "2026-04");

        var invoice = await ReadAsync<InvoiceDto>(response, HttpStatusCode.Created);
        invoice.Status.ShouldBe("open");
        var payment = invoice.Payments.ShouldHaveSingleItem();
        payment.AmountCents.ShouldBe(5000);
        payment.WalletId.ShouldBe(verde.Id);
        payment.Date.ShouldBe(new DateOnly(2026, 3, 21));
        (await BalanceAsync(space, verde.Id)).ShouldBe(-5000);
        (await BalanceAsync(space)).ShouldBe(200000);
    }

    [Fact]
    public async Task Pagamento_com_carteira_arquivada_inexistente_ou_valor_invalido_e_recusado()
    {
        var space = await AzulAsync();
        var verde = await CreateWalletAsync(space.Http, "Verde");
        (await space.Http.PostAsync($"/api/v1/wallets/{verde.Id}/archive", null)).EnsureSuccessStatusCode();

        var archived = await PayAsync(space, new { walletId = verde.Id });
        var missing = await PayAsync(space, new { walletId = Guid.NewGuid() });
        var invalid = await PayAsync(space, new { amount = "abc" });

        archived.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeOfAsync(archived)).ShouldBe("wallet.archived");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        invalid.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Pagamento_com_corpo_ausente_usa_os_padroes()
    {
        var space = await AzulAsync();

        var response = await space.Http.PostAsync($"{InvoiceUrl(space)}/payments", null);

        (await ReadAsync<InvoiceDto>(response, HttpStatusCode.Created)).Status.ShouldBe("paid");
    }

    [Fact]
    public async Task Pagamentos_simultaneos_do_restante_inteiro_gravam_so_um_FOR_UPDATE()
    {
        var space = await AzulAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PayAsync(space)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity).ShouldBe(3);
        var invoice = await GetInvoiceAsync(space);
        invoice.Payments.ShouldHaveSingleItem();
        invoice.PaidCents.ShouldBe(20000);
    }

    [Fact]
    public async Task Pagamento_com_chave_de_idempotencia_repetida_nao_duplica()
    {
        var space = await AzulAsync();
        var key = Guid.NewGuid().ToString();

        HttpRequestMessage Request() => new(HttpMethod.Post, $"{InvoiceUrl(space)}/payments")
        {
            Content = JsonContent.Create(new { amount = "50" }),
            Headers = { { "Idempotency-Key", key } },
        };

        var first = await space.Http.SendAsync(Request());
        var second = await space.Http.SendAsync(Request());

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await GetInvoiceAsync(space)).Payments.ShouldHaveSingleItem();
    }

    // --- Editar e excluir pagamento (FR-018) ---

    [Fact]
    public async Task Editar_pagamento_aceita_ate_restante_mais_o_valor_atual_e_move_o_saldo_entre_carteiras()
    {
        var space = await AzulAsync();
        var verde = await CreateWalletAsync(space.Http, "Verde");
        var payment = (await ReadAsync<InvoiceDto>(await PayAsync(space, new { amount = "150" }), HttpStatusCode.Created)).Payments.Single();

        object Body(string amount, Guid walletId, long? version = null) =>
            new { amount, date = "2026-03-22", walletId, version = version ?? payment.Version };

        var tooMuch = await space.Http.PutAsJsonAsync($"/api/v1/invoice-payments/{payment.Id}", Body("200,01", space.Wallet.Id));
        var ok = await space.Http.PutAsJsonAsync($"/api/v1/invoice-payments/{payment.Id}", Body("200", verde.Id));

        tooMuch.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await ProblemAsync(tooMuch);
        problem.Code.ShouldBe("invoice.payment_exceeds_remaining");
        problem.Errors!["amount"].ShouldBe(["O valor em aberto é R$ 200,00."]);
        var edited = await ReadAsync<InvoicePaymentDto>(ok);
        edited.AmountCents.ShouldBe(20000);
        edited.WalletId.ShouldBe(verde.Id);
        edited.Date.ShouldBe(new DateOnly(2026, 3, 22));
        edited.DueMonth.ShouldBe("2026-03");
        edited.CreditCardId.ShouldBe(space.Card.Id);
        edited.Version.ShouldNotBe(payment.Version);
        (await GetInvoiceAsync(space)).Status.ShouldBe("paid");
        (await BalanceAsync(space)).ShouldBe(200000);
        (await BalanceAsync(space, verde.Id)).ShouldBe(-20000);
    }

    [Fact]
    public async Task Editar_pagamento_com_versao_antiga_ausente_ou_inexistente_e_recusado()
    {
        var space = await AzulAsync();
        var payment = (await ReadAsync<InvoiceDto>(await PayAsync(space, new { amount = "150" }), HttpStatusCode.Created)).Payments.Single();
        var url = $"/api/v1/invoice-payments/{payment.Id}";

        var stale = await space.Http.PutAsJsonAsync(url, new { amount = "100", date = "2026-03-20", walletId = space.Wallet.Id, version = payment.Version + 1 });
        var missing = await space.Http.PutAsJsonAsync(url, new { amount = "100", date = "2026-03-20", walletId = space.Wallet.Id });
        var unknown = await space.Http.PutAsJsonAsync($"/api/v1/invoice-payments/{Guid.NewGuid()}", new { amount = "100", date = "2026-03-20", walletId = space.Wallet.Id, version = 1 });

        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(stale)).ShouldBe("concurrency_conflict");
        missing.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Excluir_pagamento_devolve_o_lote_o_saldo_volta_e_desfazer_restaura()
    {
        var space = await AzulAsync();
        var payment = (await ReadAsync<InvoiceDto>(await PayAsync(space, new { amount = "150" }), HttpStatusCode.Created)).Payments.Single();

        var response = await space.Http.DeleteAsync($"/api/v1/invoice-payments/{payment.Id}");

        var batch = (await ReadAsync<DeletionDto>(response)).DeletionBatchId;
        (await GetInvoiceAsync(space)).Payments.ShouldBeEmpty();
        (await BalanceAsync(space)).ShouldBe(200000);
        (await space.Http.DeleteAsync($"/api/v1/invoice-payments/{payment.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await space.Http.PostAsync($"/api/v1/deletions/{batch}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await BalanceAsync(space)).ShouldBe(185000);
    }

    [Fact]
    public async Task Carteira_com_pagamento_de_fatura_nao_pode_ser_excluida_so_arquivada()
    {
        var space = await AzulAsync();
        var verde = await CreateWalletAsync(space.Http, "Verde");
        (await PayAsync(space, new { amount = "10", walletId = verde.Id })).StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await space.Http.DeleteAsync($"/api/v1/wallets/{verde.Id}");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(response)).ShouldBe("wallet.has_entries_archive_instead");
    }

    // --- US5.7 ---

    [Fact]
    public async Task US5_7_compra_em_fatura_paga_pede_confirmacao_e_reabre_o_valor_em_aberto()
    {
        var space = await AzulAsync();
        (await PayAsync(space)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = new { description = "Padaria", amount = "10", date = "2026-03-15", creditCardId = space.Card.Id };

        var refused = await space.Http.PostAsJsonAsync(PurchasesUrl, body);
        var confirmed = await space.Http.PostAsJsonAsync(PurchasesUrl, new { body.description, body.amount, body.date, body.creditCardId, confirmPaidInvoice = true });

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(refused)).ShouldBe("card_purchase.invoice_paid_confirmation_required");
        confirmed.StatusCode.ShouldBe(HttpStatusCode.Created);
        var invoice = await GetInvoiceAsync(space);
        invoice.TotalCents.ShouldBe(21000);
        invoice.RemainingCents.ShouldBe(1000);
        invoice.Status.ShouldBe("closed");
    }

    // --- US7.1 ---

    [Fact]
    public async Task US7_1_conferir_soma_debitos_menos_creditos_e_nao_altera_nenhum_total()
    {
        var space = await AzulAsync();
        var before = await GetInvoiceAsync(space);
        var monthBefore = await MonthAsync(space);
        var uber = before.Purchases.Single(p => p.Description == "Uber");
        var estorno = before.Purchases.Single(p => p.Description == "Estorno Óculos");

        foreach (var purchase in new[] { uber, estorno })
        {
            var response = await space.Http.PatchAsJsonAsync($"{PurchasesUrl}/{purchase.Id}/reconciled", new { reconciled = true });
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var after = await GetInvoiceAsync(space);
        after.ReconciledCents.ShouldBe(1295 - 9990);
        after.TotalCents.ShouldBe(before.TotalCents);
        after.Status.ShouldBe(before.Status);
        (await MonthAsync(space)).GetRawText().ShouldBe(monthBefore.GetRawText());
    }

    // --- Cartão arquivado continua pagável ---

    [Fact]
    public async Task Cartao_arquivado_continua_com_fatura_visivel_e_pagavel()
    {
        var space = await AzulAsync();
        (await space.Http.PostAsync($"{CardsUrl}/{space.Card.Id}/archive", null)).EnsureSuccessStatusCode();

        var invoice = await ReadAsync<InvoiceDto>(await PayAsync(space), HttpStatusCode.Created);

        invoice.Status.ShouldBe("paid");
    }
}
