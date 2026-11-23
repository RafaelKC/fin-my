using System.Net;
using System.Net.Http.Json;
using FinMy.Api.Tests.Support;
using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Cards;
using FinMy.Domain.Monetary;
using Microsoft.EntityFrameworkCore;
using static FinMy.Api.Tests.Cards.CardTestSupport;

namespace FinMy.Api.Tests.Cards;

/// <summary>Cartões pela API (003 US1.1–US1.3, US6.1, US6.3; research R10 e códigos de erro do contrato).</summary>
public class CreditCardEndpointsTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    // Hoje = 20/03/2026. O token é emitido com o relógio real (o JWT é validado pelo relógio do sistema);
    // o relógio de teste só vai para a data fixa depois de cada cadastro de conta.
    private readonly FakeClock _clock = new(DateTimeOffset.UtcNow);
    private DateTimeOffset _today = new(2026, 3, 20, 15, 0, 0, TimeSpan.Zero);

    protected override IClock? Clock => _clock;

    // Cada teste abre uma conta nova; o limite por IP do /auth não deve atrapalhar.
    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?> { ["RateLimiting:Auth:PermitLimit"] = "100000" };

    private static readonly DateOnly March20 = new(2026, 3, 20);

    private async Task<(HttpClient Http, TestAccount Account)> NewSpaceAsync(string name = "Lia")
    {
        _clock.UtcNow = DateTimeOffset.UtcNow;
        var space = await CardTestSupport.NewSpaceAsync(Factory, name);
        _clock.UtcNow = _today;
        return space;
    }

    /// <summary>Cria a fatura do mês (datas do ciclo do cartão Azul 18/25) e as compras já em centavos.</summary>
    private async Task<Invoice> SeedInvoiceAsync(TestAccount account, CreditCardDto card, YearMonth month, params (string Description, long Cents, PurchaseNature Nature)[] purchases)
    {
        var invoice = Invoice.Create(
            card.Id, month, InvoiceCycle.ClosingDate(month, card.ClosingDay, card.DueDay), InvoiceCycle.DueDate(month, card.DueDay));
        var rows = new List<object> { invoice };
        rows.AddRange(purchases.Select(p => CardPurchase.CreateManual(
            card.Id, invoice.Id, p.Description, Money.FromCents(p.Cents), p.Nature, new DateOnly(2026, 3, 1), null)));
        await Months.MonthSeeding.SeedAsync(Factory, account, [.. rows]);
        return invoice;
    }

    // --- US1.1 ---

    [Fact]
    public async Task US1_1_cadastra_o_cartao_e_ele_aparece_na_lista_com_a_fatura_atual_zerada()
    {
        var (lia, _) = await NewSpaceAsync();
        var azulWallet = await CreateWalletAsync(lia, "Azul");

        var response = await lia.PostAsJsonAsync(CardsUrl, CardBody("Azul", azulWallet.Id, 18, 25, limit: "3.000,00"));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var card = (await response.Content.ReadFromJsonAsync<CreditCardDto>(JsonDefaults.Options))!;
        response.Headers.Location!.ToString().ShouldBe($"{CardsUrl}/{card.Id}");
        card.Name.ShouldBe("Azul");
        card.ClosingDay.ShouldBe(18);
        card.DueDay.ShouldBe(25);
        card.PaymentWalletId.ShouldBe(azulWallet.Id);
        card.Color.ShouldBe("#1E88E5");
        card.LimitCents.ShouldBe(300000);
        card.Archived.ShouldBeFalse();
        card.HasPurchases.ShouldBeFalse();
        card.Version.ShouldBeGreaterThan(0);

        var lista = await ListCardsAsync(lia);
        var azul = lista.ShouldHaveSingleItem();
        azul.Id.ShouldBe(card.Id);

        // Hoje é 20/03, depois do fechamento de 18/03: a fatura atual é a de abril (fecha 18/04, vence 25/04).
        azul.CurrentInvoice!.DueMonth.ShouldBe("2026-04");
        azul.CurrentInvoice.ClosingDate.ShouldBe(new DateOnly(2026, 4, 18));
        azul.CurrentInvoice.DueDate.ShouldBe(new DateOnly(2026, 4, 25));
        azul.CurrentInvoice.TotalCents.ShouldBe(0);
        azul.CurrentInvoice.Status.ShouldBe("no_amount_due");
        azul.CurrentInvoice.Id.ShouldBeNull();
        azul.NextInvoice!.DueMonth.ShouldBe("2026-05");
        azul.NextInvoice.TotalCents.ShouldBe(0);
        azul.OpenPastInvoices.ShouldBeEmpty();
        azul.AvailableLimitCents.ShouldBe(300000);
    }

    [Fact]
    public async Task US1_1_a_fatura_atual_antes_do_fechamento_e_a_do_proprio_mes()
    {
        _today = new DateTimeOffset(2026, 3, 17, 15, 0, 0, TimeSpan.Zero);
        var (lia, _) = await NewSpaceAsync();
        var wallet = await CreateWalletAsync(lia, "Azul");
        var card = await CreateCardAsync(lia, "Azul", wallet.Id);

        var overview = await GetCardAsync(lia, card.Id);

        overview.CurrentInvoice!.DueMonth.ShouldBe("2026-03");
        overview.CurrentInvoice.ClosingDate.ShouldBe(new DateOnly(2026, 3, 18));
        overview.CurrentInvoice.DueDate.ShouldBe(new DateOnly(2026, 3, 25));
        overview.NextInvoice!.DueMonth.ShouldBe("2026-04");
    }

    [Fact]
    public async Task Cartao_sem_limite_devolve_limite_nulo_e_limite_zero_e_aceito()
    {
        var (lia, _) = await NewSpaceAsync();
        var wallet = await CreateWalletAsync(lia, "Azul");

        var semLimite = await CreateCardAsync(lia, "Verde", wallet.Id);
        var zero = await CreateCardAsync(lia, "Roxo", wallet.Id, limit: "0");

        semLimite.LimitCents.ShouldBeNull();
        (await GetCardAsync(lia, semLimite.Id)).AvailableLimitCents.ShouldBeNull();
        zero.LimitCents.ShouldBe(0);
    }

    [Theory]
    [InlineData("name", "", "credit_card.name.required")]
    [InlineData("closingDay", 0, "credit_card.closing_day_invalid")]
    [InlineData("closingDay", 32, "credit_card.closing_day_invalid")]
    [InlineData("dueDay", 0, "credit_card.due_day_invalid")]
    [InlineData("dueDay", 32, "credit_card.due_day_invalid")]
    [InlineData("color", "azul", "credit_card.color_invalid")]
    [InlineData("limit", "-1", "credit_card.limit_invalid")]
    [InlineData("limit", "abc", "amount.invalid_expression")]
    public async Task Cadastro_invalido_responde_422_com_o_codigo_do_campo(string field, object value, string code)
    {
        var (lia, _) = await NewSpaceAsync();
        var wallet = await CreateWalletAsync(lia, "Azul");
        var body = new Dictionary<string, object?>
        {
            ["name"] = "Azul",
            ["closingDay"] = 18,
            ["dueDay"] = 25,
            ["paymentWalletId"] = wallet.Id,
            ["color"] = "#1E88E5",
            ["limit"] = null,
        };
        body[field] = value is int or string ? value : value.ToString();

        var response = await lia.PostAsJsonAsync(CardsUrl, body);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ProblemAsync(response)).Code.ShouldBe(code);
    }

    [Fact]
    public async Task Carteira_de_pagamento_e_obrigatoria_ativa_e_do_proprio_espaco()
    {
        var (lia, _) = await NewSpaceAsync("Lia");
        var (leo, _) = await NewSpaceAsync("Léo");
        var azul = await CreateWalletAsync(lia, "Azul");
        var verde = await CreateWalletAsync(lia, "Verde");
        var deLeo = await CreateWalletAsync(leo, "Azul");
        (await lia.PostAsync($"/api/v1/wallets/{verde.Id}/archive", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var semCarteira = await lia.PostAsJsonAsync(CardsUrl, new { name = "Azul", closingDay = 18, dueDay = 25, color = "#1E88E5" });
        var arquivada = await lia.PostAsJsonAsync(CardsUrl, CardBody("Azul", verde.Id));
        var deOutro = await lia.PostAsJsonAsync(CardsUrl, CardBody("Azul", deLeo.Id));

        semCarteira.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ProblemAsync(semCarteira)).Code.ShouldBe("wallet.required");
        arquivada.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ProblemAsync(arquivada)).Code.ShouldBe("wallet.archived");
        deOutro.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ListCardsAsync(lia)).ShouldBeEmpty();
        (await CreateCardAsync(lia, "Azul", azul.Id)).Name.ShouldBe("Azul");
    }

    [Fact]
    public async Task Post_com_a_mesma_chave_de_idempotencia_nao_duplica_o_cartao()
    {
        var (lia, _) = await NewSpaceAsync();
        var wallet = await CreateWalletAsync(lia, "Azul");
        var key = Guid.NewGuid().ToString();

        async Task<HttpResponseMessage> PostAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, CardsUrl) { Content = JsonContent.Create(CardBody("Azul", wallet.Id)) };
            request.Headers.Add("Idempotency-Key", key);
            return await lia.SendAsync(request);
        }

        var first = await PostAsync();
        var second = await PostAsync();

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ListCardsAsync(lia)).Count.ShouldBe(1);
    }

    // --- US1.2 ---

    [Theory]
    [InlineData("AZUL")]
    [InlineData("  azul ")]
    [InlineData("Ázul")]
    public async Task US1_2_nome_repetido_sem_diferenciar_caixa_e_acento_responde_409(string other)
    {
        var (lia, _) = await NewSpaceAsync();
        var wallet = await CreateWalletAsync(lia, "Azul");
        await CreateCardAsync(lia, "Azul", wallet.Id);

        var response = await lia.PostAsJsonAsync(CardsUrl, CardBody(other, wallet.Id));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await ProblemAsync(response);
        problem.Code.ShouldBe("credit_card.name_taken");
        problem.Errors!["name"].ShouldBe(["Já existe um cartão com esse nome."]);
        (await ListCardsAsync(lia)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task US1_2_o_mesmo_nome_em_outro_espaco_e_livre()
    {
        var (lia, _) = await NewSpaceAsync("Lia");
        var (leo, _) = await NewSpaceAsync("Léo");
        await CreateCardAsync(lia, "Azul", (await CreateWalletAsync(lia, "Azul")).Id);

        var card = await CreateCardAsync(leo, "Azul", (await CreateWalletAsync(leo, "Azul")).Id);

        card.Name.ShouldBe("Azul");
        (await ListCardsAsync(leo)).Count.ShouldBe(1);
    }

    // --- US1.3 ---

    [Fact]
    public async Task US1_3_cartao_sem_compras_pode_ser_excluido_e_libera_o_nome()
    {
        var (lia, _) = await NewSpaceAsync();
        var wallet = await CreateWalletAsync(lia, "Azul");
        var card = await CreateCardAsync(lia, "Azul", wallet.Id);

        var delete = await lia.DeleteAsync($"{CardsUrl}/{card.Id}");

        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await lia.GetAsync($"{CardsUrl}/{card.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ListCardsAsync(lia, "?archived=all")).ShouldBeEmpty();
        (await CreateCardAsync(lia, "Azul", wallet.Id)).Id.ShouldNotBe(card.Id);
    }

    [Fact]
    public async Task US1_3_cartao_com_compras_so_pode_ser_arquivado_e_a_fatura_em_aberto_continua_visivel()
    {
        var (lia, account) = await NewSpaceAsync();
        var wallet = await CreateWalletAsync(lia, "Azul");
        var card = await CreateCardAsync(lia, "Azul", wallet.Id);
        await SeedInvoiceAsync(account, card, YearMonth.Of(2026, 3), ("Uber", 20000, PurchaseNature.Debit));

        var delete = await lia.DeleteAsync($"{CardsUrl}/{card.Id}");

        delete.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemAsync(delete)).Code.ShouldBe("credit_card.has_purchases_archive_instead");
        (await GetCardAsync(lia, card.Id)).HasPurchases.ShouldBeTrue();

        var archive = await lia.PostAsync($"{CardsUrl}/{card.Id}/archive", null);

        archive.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await archive.Content.ReadFromJsonAsync<CreditCardDto>(JsonDefaults.Options))!.Archived.ShouldBeTrue();
        (await ListCardsAsync(lia)).ShouldBeEmpty();
        var arquivados = await ListCardsAsync(lia, "?archived=true");
        arquivados.ShouldHaveSingleItem().OpenPastInvoices!.ShouldHaveSingleItem().RemainingCents.ShouldBe(20000);
        (await ListCardsAsync(lia, "?archived=all")).Count.ShouldBe(1);

        var unarchive = await lia.PostAsync($"{CardsUrl}/{card.Id}/unarchive", null);

        unarchive.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ListCardsAsync(lia)).ShouldHaveSingleItem().Archived.ShouldBeFalse();
    }

    [Fact]
    public async Task US1_3_cartao_com_pagamento_de_fatura_tambem_nao_pode_ser_excluido()
    {
        var (lia, account) = await NewSpaceAsync();
        var wallet = await CreateWalletAsync(lia, "Azul");
        var card = await CreateCardAsync(lia, "Azul", wallet.Id);
        var invoice = await SeedInvoiceAsync(account, card, YearMonth.Of(2026, 3));
        await Months.MonthSeeding.SeedAsync(Factory, account, InvoicePayment.Create(invoice.Id, wallet.Id, Money.FromCents(1000), March20));

        var delete = await lia.DeleteAsync($"{CardsUrl}/{card.Id}");

        delete.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemAsync(delete)).Code.ShouldBe("credit_card.has_purchases_archive_instead");
    }

    [Fact]
    public async Task Arquivar_e_desarquivar_sao_idempotentes_e_cartao_inexistente_da_404()
    {
        var (lia, _) = await NewSpaceAsync();
        var wallet = await CreateWalletAsync(lia, "Azul");
        var card = await CreateCardAsync(lia, "Azul", wallet.Id);

        (await lia.PostAsync($"{CardsUrl}/{card.Id}/archive", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await lia.PostAsync($"{CardsUrl}/{card.Id}/archive", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await lia.PostAsync($"{CardsUrl}/{card.Id}/unarchive", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await lia.PostAsync($"{CardsUrl}/{card.Id}/unarchive", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await lia.PostAsync($"{CardsUrl}/{Guid.NewGuid()}/archive", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await lia.PostAsync($"{CardsUrl}/{Guid.NewGuid()}/unarchive", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await lia.DeleteAsync($"{CardsUrl}/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Filtro_archived_invalido_responde_422()
    {
        var (lia, _) = await NewSpaceAsync();

        var response = await lia.GetAsync($"{CardsUrl}?archived=talvez");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Cartao_de_outro_espaco_responde_404_e_nao_aparece_na_lista()
    {
        var (lia, _) = await NewSpaceAsync("Lia");
        var (leo, _) = await NewSpaceAsync("Léo");
        var card = await CreateCardAsync(lia, "Azul", (await CreateWalletAsync(lia, "Azul")).Id);

        (await leo.GetAsync($"{CardsUrl}/{card.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await leo.DeleteAsync($"{CardsUrl}/{card.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await leo.GetAsync($"{CardsUrl}/{card.Id}/invoice-for?date=2026-03-17")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ListCardsAsync(leo)).ShouldBeEmpty();
    }

    // --- Edição ---

    [Fact]
    public async Task Edita_nome_cor_limite_e_carteira_exigindo_a_versao()
    {
        var (lia, _) = await NewSpaceAsync();
        var azul = await CreateWalletAsync(lia, "Azul");
        var verde = await CreateWalletAsync(lia, "Verde");
        var card = await CreateCardAsync(lia, "Azul", azul.Id);

        var semVersao = await lia.PutAsJsonAsync($"{CardsUrl}/{card.Id}", CardBody("Verde", verde.Id));
        var response = await lia.PutAsJsonAsync(
            $"{CardsUrl}/{card.Id}",
            new { name = "Cartão Verde", closingDay = 18, dueDay = 25, paymentWalletId = verde.Id, color = "#43A047", limit = "1.500,00", version = card.Version });

        semVersao.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var edited = (await response.Content.ReadFromJsonAsync<CreditCardDto>(JsonDefaults.Options))!;
        edited.Name.ShouldBe("Cartão Verde");
        edited.Color.ShouldBe("#43A047");
        edited.LimitCents.ShouldBe(150000);
        edited.PaymentWalletId.ShouldBe(verde.Id);
        edited.Version.ShouldNotBe(card.Version);
    }

    [Fact]
    public async Task Edicao_com_versao_antiga_responde_409_concurrency_conflict()
    {
        var (lia, _) = await NewSpaceAsync();
        var wallet = await CreateWalletAsync(lia, "Azul");
        var card = await CreateCardAsync(lia, "Azul", wallet.Id);
        var body = new { name = "Azul", closingDay = 18, dueDay = 25, paymentWalletId = wallet.Id, color = "#1E88E5", limit = "100", version = card.Version };
        (await lia.PutAsJsonAsync($"{CardsUrl}/{card.Id}", body)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var stale = await lia.PutAsJsonAsync($"{CardsUrl}/{card.Id}", body);

        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemAsync(stale)).Code.ShouldBe("concurrency_conflict");
    }

    [Fact]
    public async Task Edicao_para_o_nome_de_outro_cartao_responde_409_name_taken()
    {
        var (lia, _) = await NewSpaceAsync();
        var wallet = await CreateWalletAsync(lia, "Azul");
        await CreateCardAsync(lia, "Azul", wallet.Id);
        var verde = await CreateCardAsync(lia, "Verde", wallet.Id);

        var response = await lia.PutAsJsonAsync(
            $"{CardsUrl}/{verde.Id}",
            new { name = "azul", closingDay = 1, dueDay = 10, paymentWalletId = wallet.Id, color = "#1E88E5", limit = (string?)null, version = verde.Version });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemAsync(response)).Code.ShouldBe("credit_card.name_taken");
        var unchanged = await GetCardAsync(lia, verde.Id);
        unchanged.Name.ShouldBe("Verde");
        unchanged.ClosingDay.ShouldBe(18);
    }

    [Fact]
    public async Task Manter_a_carteira_arquivada_nao_impede_editar_mas_trocar_para_arquivada_sim()
    {
        var (lia, _) = await NewSpaceAsync();
        var azul = await CreateWalletAsync(lia, "Azul");
        var verde = await CreateWalletAsync(lia, "Verde");
        var card = await CreateCardAsync(lia, "Azul", verde.Id);
        (await lia.PostAsync($"/api/v1/wallets/{verde.Id}/archive", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var mantem = await lia.PutAsJsonAsync(
            $"{CardsUrl}/{card.Id}",
            new { name = "Azul 2", closingDay = 18, dueDay = 25, paymentWalletId = verde.Id, color = "#1E88E5", limit = (string?)null, version = card.Version });
        var atual = (await mantem.Content.ReadFromJsonAsync<CreditCardDto>(JsonDefaults.Options))!;
        var volta = await lia.PutAsJsonAsync(
            $"{CardsUrl}/{card.Id}",
            new { name = "Azul 2", closingDay = 18, dueDay = 25, paymentWalletId = azul.Id, color = "#1E88E5", limit = (string?)null, version = atual.Version });
        var troca = (await volta.Content.ReadFromJsonAsync<CreditCardDto>(JsonDefaults.Options))!;
        var paraArquivada = await lia.PutAsJsonAsync(
            $"{CardsUrl}/{card.Id}",
            new { name = "Azul 2", closingDay = 18, dueDay = 25, paymentWalletId = verde.Id, color = "#1E88E5", limit = (string?)null, version = troca.Version });

        mantem.StatusCode.ShouldBe(HttpStatusCode.OK);
        volta.StatusCode.ShouldBe(HttpStatusCode.OK);
        paraArquivada.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ProblemAsync(paraArquivada)).Code.ShouldBe("wallet.archived");
    }

    // --- US6.1 / US6.3 ---

    [Fact]
    public async Task US6_1_limite_disponivel_desconta_o_restante_das_faturas_nao_pagas_inclusive_as_futuras()
    {
        var (lia, account) = await NewSpaceAsync();
        var wallet = await CreateWalletAsync(lia, "Azul");
        var card = await CreateCardAsync(lia, "Azul", wallet.Id, limit: "3.000,00");
        await SeedInvoiceAsync(account, card, YearMonth.Of(2026, 3), ("Uber", 20000, PurchaseNature.Debit));
        await SeedInvoiceAsync(account, card, YearMonth.Of(2026, 4), ("Casa China 2/3", 23333, PurchaseNature.Debit));
        await SeedInvoiceAsync(account, card, YearMonth.Of(2026, 5), ("Casa China 3/3", 23333, PurchaseNature.Debit));

        var overview = await GetCardAsync(lia, card.Id);

        overview.AvailableLimitCents.ShouldBe(300000 - 20000 - 23333 - 23333); // R$ 2.333,34
        overview.AvailableLimitCents.ShouldBe(233334);
        overview.CurrentInvoice!.DueMonth.ShouldBe("2026-04");
        overview.CurrentInvoice.TotalCents.ShouldBe(23333);
        overview.CurrentInvoice.Status.ShouldBe("open");
        overview.NextInvoice!.DueMonth.ShouldBe("2026-05");
        overview.NextInvoice.TotalCents.ShouldBe(23333);
    }

    [Fact]
    public async Task US6_1_fatura_paga_nao_conta_no_limite_pagamento_parcial_so_libera_o_que_foi_pago_e_credito_abate()
    {
        var (lia, account) = await NewSpaceAsync();
        var wallet = await CreateWalletAsync(lia, "Azul");
        var card = await CreateCardAsync(lia, "Azul", wallet.Id, limit: "3.000,00");
        var fevereiro = await SeedInvoiceAsync(account, card, YearMonth.Of(2026, 2), ("Mercado", 50000, PurchaseNature.Debit));
        var marco = await SeedInvoiceAsync(
            account, card, YearMonth.Of(2026, 3), ("Uber", 30000, PurchaseNature.Debit), ("Estorno", 5000, PurchaseNature.Credit));
        await Months.MonthSeeding.SeedAsync(
            Factory,
            account,
            InvoicePayment.Create(fevereiro.Id, wallet.Id, Money.FromCents(50000), new DateOnly(2026, 2, 25)),
            InvoicePayment.Create(marco.Id, wallet.Id, Money.FromCents(10000), March20));

        var overview = await GetCardAsync(lia, card.Id);

        // Fevereiro paga (0) + março 300,00 − 50,00 estorno − 100,00 pago = 150,00 em aberto.
        overview.AvailableLimitCents.ShouldBe(300000 - 15000);
        overview.OpenPastInvoices!.Select(i => i.DueMonth).ShouldBe(["2026-03"]);
        var fechada = overview.OpenPastInvoices!.Single();
        fechada.TotalCents.ShouldBe(25000);
        fechada.PaidCents.ShouldBe(10000);
        fechada.RemainingCents.ShouldBe(15000);
        fechada.Status.ShouldBe("closed");
    }

    [Fact]
    public async Task US6_1_fatura_vencida_aparece_como_vencida_e_limite_pode_ficar_negativo()
    {
        var (lia, account) = await NewSpaceAsync();
        var wallet = await CreateWalletAsync(lia, "Azul");
        var card = await CreateCardAsync(lia, "Azul", wallet.Id, limit: "100,00");
        await SeedInvoiceAsync(account, card, YearMonth.Of(2026, 2), ("Mercado", 50000, PurchaseNature.Debit));

        var overview = await GetCardAsync(lia, card.Id);

        overview.OpenPastInvoices.ShouldHaveSingleItem().Status.ShouldBe("overdue");
        overview.AvailableLimitCents.ShouldBe(10000 - 50000);
    }

    [Fact]
    public async Task US6_3_cartao_sem_limite_nao_devolve_limite_disponivel_mesmo_com_compras()
    {
        var (lia, account) = await NewSpaceAsync();
        var wallet = await CreateWalletAsync(lia, "Azul");
        var card = await CreateCardAsync(lia, "Azul", wallet.Id);
        await SeedInvoiceAsync(account, card, YearMonth.Of(2026, 3), ("Uber", 20000, PurchaseNature.Debit));

        var overview = await GetCardAsync(lia, card.Id);

        overview.LimitCents.ShouldBeNull();
        overview.AvailableLimitCents.ShouldBeNull();
    }

    [Fact]
    public async Task Compra_excluida_nao_entra_no_total_mas_o_cartao_continua_com_historico()
    {
        var (lia, account) = await NewSpaceAsync();
        var wallet = await CreateWalletAsync(lia, "Azul");
        var card = await CreateCardAsync(lia, "Azul", wallet.Id, limit: "1.000,00");
        var invoice = await SeedInvoiceAsync(account, card, YearMonth.Of(2026, 3), ("Uber", 20000, PurchaseNature.Debit));
        await WithDbAsync(Factory, account, async (db, _) =>
        {
            var purchase = await db.Set<CardPurchase>().SingleAsync(p => p.InvoiceId == invoice.Id);
            db.Remove(purchase);
            await db.SaveChangesAsync();
            return 0;
        });

        var overview = await GetCardAsync(lia, card.Id);

        overview.AvailableLimitCents.ShouldBe(100000);
        overview.HasPurchases.ShouldBeTrue();
        (await lia.DeleteAsync($"{CardsUrl}/{card.Id}")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    // --- invoice-for ---

    [Theory]
    [InlineData("2026-03-17", "2026-03", "2026-03-18", "2026-03-25")]
    [InlineData("2026-03-18", "2026-04", "2026-04-18", "2026-04-25")]
    [InlineData("2026-12-20", "2027-01", "2027-01-18", "2027-01-25")]
    public async Task Invoice_for_devolve_a_fatura_da_compra_pelas_regras_do_ciclo(string date, string month, string closing, string due)
    {
        var (lia, _) = await NewSpaceAsync();
        var card = await CreateCardAsync(lia, "Azul", (await CreateWalletAsync(lia, "Azul")).Id);

        var response = await lia.GetAsync($"{CardsUrl}/{card.Id}/invoice-for?date={date}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        body.GetProperty("creditCardId").GetGuid().ShouldBe(card.Id);
        body.GetProperty("dueMonth").GetString().ShouldBe(month);
        body.GetProperty("closingDate").GetString().ShouldBe(closing);
        body.GetProperty("dueDate").GetString().ShouldBe(due);
    }

    [Fact]
    public async Task Invoice_for_com_cartao_de_fechamento_menor_que_o_vencimento_em_mes_curto_usa_o_ultimo_dia()
    {
        var (lia, _) = await NewSpaceAsync();
        var card = await CreateCardAsync(lia, "Amarelo", (await CreateWalletAsync(lia, "Azul")).Id, closingDay: 30, dueDay: 8);

        var response = await lia.GetAsync($"{CardsUrl}/{card.Id}/invoice-for?date=2026-02-28");

        // Fecha dia 30 no mês anterior ao vencimento (30 ≥ 8); em fevereiro vira 28 (RT-03): compra no dia 28 vai para a fatura seguinte.
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        body.GetProperty("dueMonth").GetString().ShouldBe("2026-04");
        body.GetProperty("closingDate").GetString().ShouldBe("2026-03-30");
    }

    [Theory]
    [InlineData("")]
    [InlineData("?date=17/03/2026")]
    [InlineData("?date=1999-12-31")]
    public async Task Invoice_for_com_data_ausente_invalida_ou_fora_do_intervalo_responde_422(string query)
    {
        var (lia, _) = await NewSpaceAsync();
        var card = await CreateCardAsync(lia, "Azul", (await CreateWalletAsync(lia, "Azul")).Id);

        var response = await lia.GetAsync($"{CardsUrl}/{card.Id}/invoice-for{query}");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Invoice_for_usa_as_datas_gravadas_da_fatura_existente()
    {
        var (lia, account) = await NewSpaceAsync();
        var wallet = await CreateWalletAsync(lia, "Azul");
        var card = await CreateCardAsync(lia, "Azul", wallet.Id);
        // Fatura de março gravada com outras datas (por exemplo, de antes de uma mudança de dias).
        await Months.MonthSeeding.SeedAsync(
            Factory, account, Invoice.Create(card.Id, YearMonth.Of(2026, 3), new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 20)));

        var response = await lia.GetAsync($"{CardsUrl}/{card.Id}/invoice-for?date=2026-03-05");

        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        body.GetProperty("dueMonth").GetString().ShouldBe("2026-03");
        body.GetProperty("closingDate").GetString().ShouldBe("2026-03-10");
        body.GetProperty("dueDate").GetString().ShouldBe("2026-03-20");
    }
}
