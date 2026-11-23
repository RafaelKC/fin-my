using System.Net;
using System.Net.Http.Json;
using FinMy.Api.Tests.Entries;
using FinMy.Api.Tests.Support;
using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Cards;
using FinMy.Domain.Monetary;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FinMy.Api.Tests.Cards.CardTestSupport;

namespace FinMy.Api.Tests.Cards;

internal sealed record PurchaseDto(
    Guid Id, Guid CreditCardId, string DueMonth, string Description, string DisplayDescription, long AmountCents, string Nature,
    DateOnly Date, string Origin, bool Reconciled, bool ManuallyMoved, string? Notes, Guid? InstallmentPlanId,
    int? InstallmentNumber, int? InstallmentTotal, DateTimeOffset CreatedAt, long Version);

internal sealed record CreatedPurchasesDto(List<PurchaseDto> Purchases, List<string> Warnings);

internal sealed record DeletionDto(Guid DeletionBatchId);

/// <summary>Compras e parcelamentos pela API (003 US2.1–US2.4, US3.1, US3.2, US3.4, US3.5, US4.1, US4.2, US7.2, US7.3; research R5–R7, R9).</summary>
public class CardPurchaseEndpointsTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private const string PurchasesUrl = "/api/v1/card-purchases";

    // Hoje = 20/03/2026. O token é emitido com o relógio real; o relógio de teste só vai para a data fixa depois do cadastro.
    private readonly FakeClock _clock = new(DateTimeOffset.UtcNow);
    private DateTimeOffset _today = new(2026, 3, 20, 15, 0, 0, TimeSpan.Zero);

    protected override IClock? Clock => _clock;

    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?> { ["RateLimiting:Auth:PermitLimit"] = "100000" };

    private async Task<(HttpClient Http, TestAccount Account)> NewSpaceAsync(string name = "Lia")
    {
        _clock.UtcNow = DateTimeOffset.UtcNow;
        var space = await CardTestSupport.NewSpaceAsync(Factory, name);
        _clock.UtcNow = _today;
        return space;
    }

    /// <summary>Espaço da Lia com a carteira e o cartão Azul (fecha 18, vence 25).</summary>
    private async Task<(HttpClient Http, TestAccount Account, CreditCardDto Card)> AzulAsync()
    {
        var (http, account) = await NewSpaceAsync();
        var wallet = await CreateWalletAsync(http, "Azul");
        return (http, account, await CreateCardAsync(http, "Azul", wallet.Id));
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient http, object body, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, PurchasesUrl) { Content = JsonContent.Create(body) };
        if (key is not null)
        {
            request.Headers.Add("Idempotency-Key", key);
        }

        return http.SendAsync(request);
    }

    private static object Purchase(Guid cardId, string description, string amount, string date, object? installments = null, string? nature = null, bool? confirm = null) =>
        new { description, amount, date, creditCardId = cardId, nature, installments, confirmPaidInvoice = confirm };

    private static async Task<CreatedPurchasesDto> CreateAsync(HttpClient http, object body)
    {
        var response = await PostAsync(http, body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CreatedPurchasesDto>(JsonDefaults.Options))!;
    }

    private static async Task<PurchaseDto> GetAsync(HttpClient http, Guid id) =>
        (await http.GetFromJsonAsync<PurchaseDto>($"{PurchasesUrl}/{id}", JsonDefaults.Options))!;

    private static Task<HttpResponseMessage> PutAsync(HttpClient http, PurchaseDto p, object? changes = null, long? version = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["description"] = p.Description,
            ["amount"] = (p.AmountCents / 100m).ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")),
            ["nature"] = p.Nature,
            ["date"] = p.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ["creditCardId"] = p.CreditCardId,
            ["notes"] = p.Notes,
            ["version"] = version ?? p.Version,
        };
        foreach (var property in (changes ?? new { }).GetType().GetProperties())
        {
            body[property.Name] = property.GetValue(changes);
        }

        return http.PutAsJsonAsync($"{PurchasesUrl}/{p.Id}", body);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>(JsonDefaults.Options))!;
    }

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response) => (await ProblemAsync(response)).Code;

    private static async Task<CreditCardDto> NewCardAsync(HttpClient http, string name, int closing, int due)
    {
        var wallet = await CreateWalletAsync(http, name);
        return await CreateCardAsync(http, name, wallet.Id, closing, due);
    }

    private static object Installments(int total, string mode, int? next = null, string? firstInvoiceMonth = null) =>
        new { total, mode, nextNumber = next, firstInvoiceMonth };

    // --- US2.1 a US2.3 ---

    [Fact]
    public async Task US2_1_compra_a_vista_cai_na_fatura_do_ciclo_e_nao_tem_aviso()
    {
        _today = new DateTimeOffset(2026, 3, 17, 15, 0, 0, TimeSpan.Zero);
        var (lia, _, azul) = await AzulAsync();

        var response = await PostAsync(lia, Purchase(azul.Id, "Uber", "12,95", "2026-03-17"));

        var created = await ReadAsync<CreatedPurchasesDto>(response, HttpStatusCode.Created);
        var uber = created.Purchases.ShouldHaveSingleItem();
        response.Headers.Location!.ToString().ShouldBe($"{PurchasesUrl}/{uber.Id}");
        created.Warnings.ShouldBeEmpty();
        uber.AmountCents.ShouldBe(1295);
        uber.Nature.ShouldBe("debit");
        uber.Origin.ShouldBe("manual");
        uber.Date.ShouldBe(new DateOnly(2026, 3, 17));
        uber.DueMonth.ShouldBe("2026-03");
        uber.DisplayDescription.ShouldBe("Uber");
        uber.CreditCardId.ShouldBe(azul.Id);
        uber.Reconciled.ShouldBeFalse();
        uber.ManuallyMoved.ShouldBeFalse();
        uber.InstallmentPlanId.ShouldBeNull();
        uber.Version.ShouldBeGreaterThan(0);

        (await GetAsync(lia, uber.Id)).ShouldBe(uber);
        (await GetCardAsync(lia, azul.Id)).CurrentInvoice!.TotalCents.ShouldBe(1295);
    }

    [Fact]
    public async Task US2_2_valor_negativo_vira_credito_com_aviso()
    {
        var (lia, _, azul) = await AzulAsync();

        var created = await CreateAsync(lia, Purchase(azul.Id, "Estorno Óculos", "-99,90", "2026-03-10"));

        var estorno = created.Purchases.ShouldHaveSingleItem();
        estorno.Nature.ShouldBe("credit");
        estorno.AmountCents.ShouldBe(9990);
        created.Warnings.ShouldBe(["recorded_as_credit"]);
    }

    [Fact]
    public async Task Natureza_credito_explicita_nao_gera_aviso_e_natureza_invalida_e_recusada()
    {
        var (lia, _, azul) = await AzulAsync();

        var credito = await CreateAsync(lia, Purchase(azul.Id, "Estorno", "10", "2026-03-10", nature: "credit"));
        var invalida = await PostAsync(lia, Purchase(azul.Id, "Estorno", "10", "2026-03-10", nature: "outra"));

        credito.Purchases.Single().Nature.ShouldBe("credit");
        credito.Warnings.ShouldBeEmpty();
        invalida.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeOfAsync(invalida)).ShouldBe("nature.invalid");
    }

    [Fact]
    public async Task US2_3_valor_aceita_expressao()
    {
        var (lia, _, azul) = await AzulAsync();

        var created = await CreateAsync(lia, Purchase(azul.Id, "Mercado", "239,8/2", "2026-03-10"));

        created.Purchases.Single().AmountCents.ShouldBe(11990);
    }

    [Theory]
    [InlineData(18, 25, "2026-03-17", "2026-03")]
    [InlineData(18, 25, "2026-03-18", "2026-04")]
    [InlineData(18, 25, "2026-12-31", "2027-01")]
    [InlineData(1, 10, "2026-01-31", "2026-02")]
    [InlineData(1, 10, "2026-02-01", "2026-03")]
    [InlineData(28, 5, "2026-02-27", "2026-03")]
    [InlineData(28, 5, "2026-02-28", "2026-04")]
    [InlineData(30, 8, "2026-02-27", "2026-03")]
    [InlineData(30, 8, "2026-02-28", "2026-04")]
    public async Task US2_4_regra_de_fatura(int closing, int due, string date, string expectedMonth)
    {
        var (lia, _) = await NewSpaceAsync();
        var card = await NewCardAsync(lia, "Teste", closing, due);

        var created = await CreateAsync(lia, Purchase(card.Id, "Compra", "10", date));

        created.Purchases.Single().DueMonth.ShouldBe(expectedMonth);
    }

    [Theory]
    [InlineData("0", "amount.must_be_positive")]
    [InlineData("abc", "amount.invalid_expression")]
    [InlineData("10/0", "amount.division_by_zero")]
    [InlineData("10000000", "amount.too_large")]
    public async Task Valor_invalido_responde_422(string amount, string code)
    {
        var (lia, _, azul) = await AzulAsync();

        var response = await PostAsync(lia, Purchase(azul.Id, "Uber", amount, "2026-03-10"));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeOfAsync(response)).ShouldBe(code);
    }

    [Fact]
    public async Task Cartao_e_obrigatorio_ativo_e_do_proprio_espaco()
    {
        var (lia, _, azul) = await AzulAsync();
        var (leo, _) = await NewSpaceAsync("Léo");
        var deLeo = await NewCardAsync(leo, "Verde", 5, 12);
        (await lia.PostAsync($"/api/v1/credit-cards/{azul.Id}/archive", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var semCartao = await PostAsync(lia, new { description = "Uber", amount = "10", date = "2026-03-10" });
        var arquivado = await PostAsync(lia, Purchase(azul.Id, "Uber", "10", "2026-03-10"));
        var deOutro = await PostAsync(lia, Purchase(deLeo.Id, "Uber", "10", "2026-03-10"));

        semCartao.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeOfAsync(semCartao)).ShouldBe("credit_card.required");
        arquivado.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeOfAsync(arquivado)).ShouldBe("credit_card.archived");
        deOutro.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Data_e_descricao_sao_obrigatorias()
    {
        var (lia, _, azul) = await AzulAsync();

        var semData = await PostAsync(lia, new { description = "Uber", amount = "10", creditCardId = azul.Id });
        var foraDoIntervalo = await PostAsync(lia, Purchase(azul.Id, "Uber", "10", "1999-12-31"));
        var semDescricao = await PostAsync(lia, Purchase(azul.Id, " ", "10", "2026-03-10"));

        (await CodeOfAsync(semData)).ShouldBe("date.required");
        (await CodeOfAsync(foraDoIntervalo)).ShouldBe("date.out_of_range");
        semDescricao.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeOfAsync(semDescricao)).ShouldBe("description.required");
    }

    [Fact]
    public async Task Post_com_a_mesma_chave_de_idempotencia_nao_duplica_as_parcelas()
    {
        var (lia, _, azul) = await AzulAsync();
        var key = Guid.NewGuid().ToString();
        var body = Purchase(azul.Id, "Casa China", "100", "2026-01-28", Installments(3, "total_amount"));

        var first = await PostAsync(lia, body, key);
        var second = await PostAsync(lia, body, key);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ReadAsync<CreatedPurchasesDto>(second, HttpStatusCode.Created)).Purchases.Count.ShouldBe(3);
        (await CountPurchasesAsync(azul.Id)).ShouldBe(3);
    }

    /// <summary>Compras não excluídas do cartão, lidas direto do banco (todos os Espaços; o cartão é de um só).</summary>
    private async Task<int> CountPurchasesAsync(Guid cardId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMyDbContext>();
        return await db.Set<CardPurchase>().IgnoreQueryFilters().CountAsync(p => p.CreditCardId == cardId && p.DeletedAt == null);
    }

    // --- US3.1, US3.2 ---

    [Fact]
    public async Task US3_1_valor_total_em_3x_distribui_a_sobra_na_primeira_parcela()
    {
        var (lia, _, azul) = await AzulAsync();

        var created = await CreateAsync(lia, Purchase(azul.Id, "Casa China", "100,00", "2026-01-28", Installments(3, "total_amount")));

        created.Purchases.Select(p => (p.DisplayDescription, p.AmountCents, p.DueMonth, p.InstallmentNumber, p.InstallmentTotal, p.Date))
            .ShouldBe(
            [
                ("Casa China 1/3", 3334L, "2026-02", 1, 3, new DateOnly(2026, 1, 28)),
                ("Casa China 2/3", 3333L, "2026-03", 2, 3, new DateOnly(2026, 1, 28)),
                ("Casa China 3/3", 3333L, "2026-04", 3, 3, new DateOnly(2026, 1, 28)),
            ]);
        created.Purchases.Select(p => p.Origin).Distinct().ShouldBe(["installment"]);
        created.Purchases.Select(p => p.InstallmentPlanId).Distinct().Count().ShouldBe(1);
        created.Purchases.All(p => p.Description == "Casa China").ShouldBeTrue();
    }

    [Fact]
    public async Task US3_2_valor_da_parcela_em_8x_repete_o_valor()
    {
        var (lia, _, azul) = await AzulAsync();

        var created = await CreateAsync(lia, Purchase(azul.Id, "Shoppe Casa", "102,79", "2026-03-10", Installments(8, "installment_amount")));

        created.Purchases.Count.ShouldBe(8);
        created.Purchases.Select(p => p.AmountCents).Distinct().ShouldBe([10279L]);
        created.Purchases.Sum(p => p.AmountCents).ShouldBe(82232);
        created.Purchases.Select(p => p.DueMonth).ShouldBe(
            ["2026-03", "2026-04", "2026-05", "2026-06", "2026-07", "2026-08", "2026-09", "2026-10"]);
    }

    [Fact]
    public async Task Parcelado_em_1x_e_a_vista_e_parcelas_fora_de_1_a_48_sao_recusadas()
    {
        var (lia, _, azul) = await AzulAsync();

        var umaVez = await CreateAsync(lia, Purchase(azul.Id, "Uma vez", "50", "2026-03-10", Installments(1, "total_amount")));
        var demais = await PostAsync(lia, Purchase(azul.Id, "Demais", "50", "2026-03-10", Installments(49, "total_amount")));
        var zero = await PostAsync(lia, Purchase(azul.Id, "Zero", "50", "2026-03-10", Installments(0, "installment_amount")));
        var modo = await PostAsync(lia, Purchase(azul.Id, "Modo", "50", "2026-03-10", Installments(3, "outro")));
        var quarentaE8 = await CreateAsync(lia, Purchase(azul.Id, "Quarenta e oito", "480", "2026-03-10", Installments(48, "total_amount")));

        umaVez.Purchases.Single().InstallmentPlanId.ShouldBeNull();
        umaVez.Purchases.Single().Origin.ShouldBe("manual");
        (await CodeOfAsync(demais)).ShouldBe("installment.total_invalid");
        (await CodeOfAsync(zero)).ShouldBe("installment.total_invalid");
        (await CodeOfAsync(modo)).ShouldBe("installment.mode_invalid");
        quarentaE8.Purchases.Count.ShouldBe(48);
    }

    [Fact]
    public async Task Credito_parcelado_e_recusado()
    {
        var (lia, _, azul) = await AzulAsync();

        var negativo = await PostAsync(lia, Purchase(azul.Id, "Estorno", "-90", "2026-03-10", Installments(3, "total_amount")));
        var explicito = await PostAsync(lia, Purchase(azul.Id, "Estorno", "90", "2026-03-10", Installments(2, "installment_amount"), nature: "credit"));
        var emAndamento = await PostAsync(lia, Purchase(azul.Id, "Estorno", "-90", "2026-03-10", Installments(8, "in_progress", 5)));

        foreach (var response in new[] { negativo, explicito, emAndamento })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            (await CodeOfAsync(response)).ShouldBe("card_purchase.credit_must_be_single");
        }

        (await CountPurchasesAsync(azul.Id)).ShouldBe(0);
    }

    // --- US4 ---

    [Fact]
    public async Task US4_1_parcelamento_em_andamento_cria_so_as_restantes()
    {
        var (lia, _, azul) = await AzulAsync();

        var created = await CreateAsync(lia, Purchase(azul.Id, "Shoppe Casa", "102,74", "2026-06-20", Installments(8, "in_progress", 5, "2026-11")));

        created.Purchases.Select(p => (p.DisplayDescription, p.DueMonth, p.AmountCents, p.Date)).ShouldBe(
        [
            ("Shoppe Casa 5/8", "2026-11", 10274L, new DateOnly(2026, 6, 20)),
            ("Shoppe Casa 6/8", "2026-12", 10274L, new DateOnly(2026, 6, 20)),
            ("Shoppe Casa 7/8", "2027-01", 10274L, new DateOnly(2026, 6, 20)),
            ("Shoppe Casa 8/8", "2027-02", 10274L, new DateOnly(2026, 6, 20)),
        ]);
    }

    [Fact]
    public async Task US4_1_sem_fatura_informada_a_parcela_atual_cai_na_fatura_atual_do_cartao()
    {
        var (lia, _, azul) = await AzulAsync();

        var created = await CreateAsync(lia, Purchase(azul.Id, "Shoppe Casa", "102,74", "2026-02-10", Installments(8, "in_progress", 7)));

        // Hoje 20/03/2026, depois do fechamento de 18/03: a fatura atual é a de abril.
        created.Purchases.Select(p => (p.InstallmentNumber, p.DueMonth)).ShouldBe([(7, "2026-04"), (8, "2026-05")]);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(0)]
    [InlineData(null)]
    public async Task US4_2_proxima_parcela_fora_de_1_ate_o_total_e_recusada(int? next)
    {
        var (lia, _, azul) = await AzulAsync();

        var response = await PostAsync(lia, Purchase(azul.Id, "Shoppe Casa", "102,74", "2026-06-20", Installments(8, "in_progress", next, "2026-11")));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeOfAsync(response)).ShouldBe("card_purchase.invalid_next_installment");
        (await CountPurchasesAsync(azul.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task Fatura_inicial_do_parcelamento_em_andamento_invalida_e_recusada()
    {
        var (lia, _, azul) = await AzulAsync();

        var response = await PostAsync(lia, Purchase(azul.Id, "Shoppe Casa", "102,74", "2026-06-20", Installments(8, "in_progress", 5, "novembro")));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeOfAsync(response)).ShouldBe("firstInvoiceMonth.invalid");
    }

    // --- Confirmação de fatura paga (research R5) ---

    /// <summary>Fatura do mês com uma compra "Lanche" de R$ 200,00 já paga por inteiro; devolve a compra.</summary>
    private async Task<CardPurchase> SeedPaidInvoiceAsync(TestAccount account, CreditCardDto card, YearMonth month)
    {
        var invoice = Invoice.Create(card.Id, month, InvoiceCycle.ClosingDate(month, card.ClosingDay, card.DueDay), InvoiceCycle.DueDate(month, card.DueDay));
        var lanche = CardPurchase.CreateManual(card.Id, invoice.Id, "Lanche", Money.FromCents(20000), PurchaseNature.Debit, new DateOnly(2026, 3, 1), null);
        await Months.MonthSeeding.SeedAsync(
            Factory,
            account,
            invoice,
            lanche,
            InvoicePayment.Create(invoice.Id, card.PaymentWalletId, Money.FromCents(20000), new DateOnly(2026, 3, 10)));
        return lanche;
    }

    [Fact]
    public async Task US5_7_compra_em_fatura_paga_depois_do_fechamento_pede_confirmacao()
    {
        var (lia, account, azul) = await AzulAsync();
        await SeedPaidInvoiceAsync(account, azul, YearMonth.Of(2026, 3));

        var semConfirmar = await PostAsync(lia, Purchase(azul.Id, "Uber", "12,95", "2026-03-10"));
        var confirmada = await PostAsync(lia, Purchase(azul.Id, "Uber", "12,95", "2026-03-10", confirm: true));
        var credito = await PostAsync(lia, Purchase(azul.Id, "Estorno", "-5", "2026-03-10"));

        semConfirmar.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(semConfirmar)).ShouldBe("card_purchase.invoice_paid_confirmation_required");
        (await ProblemDetailAsync(semConfirmar)).ShouldBe("Esta fatura já foi paga e voltará a ter valor em aberto.");
        confirmada.StatusCode.ShouldBe(HttpStatusCode.Created);
        credito.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await CountPurchasesAsync(azul.Id)).ShouldBe(3);
    }

    [Fact]
    public async Task Pagamento_antecipado_antes_do_fechamento_nao_pede_confirmacao()
    {
        _today = new DateTimeOffset(2026, 3, 10, 15, 0, 0, TimeSpan.Zero);
        var (lia, account, azul) = await AzulAsync();
        await SeedPaidInvoiceAsync(account, azul, YearMonth.Of(2026, 3));

        var response = await PostAsync(lia, Purchase(azul.Id, "Uber", "12,95", "2026-03-10"));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Parcela_numa_fatura_paga_tambem_pede_confirmacao_e_nada_e_criado()
    {
        var (lia, account, azul) = await AzulAsync();
        await SeedPaidInvoiceAsync(account, azul, YearMonth.Of(2026, 3));

        var semConfirmar = await PostAsync(lia, Purchase(azul.Id, "Casa", "90", "2026-02-20", Installments(3, "total_amount")));

        // 1ª parcela em 2026-03 (paga): recusa o conjunto inteiro.
        semConfirmar.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CountPurchasesAsync(azul.Id)).ShouldBe(1);
    }

    private static async Task<string?> ProblemDetailAsync(HttpResponseMessage response) =>
        (await response.JsonAsync()).GetProperty("detail").GetString();

    // --- Obter, conferir ---

    [Fact]
    public async Task Obter_compra_de_outro_espaco_ou_inexistente_responde_404()
    {
        var (lia, _, azul) = await AzulAsync();
        var (leo, _) = await NewSpaceAsync("Léo");
        var uber = (await CreateAsync(lia, Purchase(azul.Id, "Uber", "10", "2026-03-10"))).Purchases.Single();

        var deOutro = await leo.GetAsync($"{PurchasesUrl}/{uber.Id}");
        var inexistente = await lia.GetAsync($"{PurchasesUrl}/{Guid.NewGuid()}");

        deOutro.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        inexistente.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOfAsync(deOutro)).ShouldBe("not_found");
    }

    [Fact]
    public async Task US7_1_marcar_como_conferida_nao_altera_o_total_da_fatura()
    {
        var (lia, _, azul) = await AzulAsync();
        var uber = (await CreateAsync(lia, Purchase(azul.Id, "Uber", "10", "2026-03-10"))).Purchases.Single();
        var antes = (await GetCardAsync(lia, azul.Id)).CurrentInvoice!;

        var marcada = await ReadAsync<PurchaseDto>(await lia.PatchAsJsonAsync($"{PurchasesUrl}/{uber.Id}/reconciled", new { reconciled = true }));
        var depois = (await GetCardAsync(lia, azul.Id)).CurrentInvoice!;
        var desmarcada = await ReadAsync<PurchaseDto>(await lia.PatchAsJsonAsync($"{PurchasesUrl}/{uber.Id}/reconciled", new { reconciled = false }));
        var semCorpo = await lia.PatchAsJsonAsync($"{PurchasesUrl}/{uber.Id}/reconciled", new { });

        marcada.Reconciled.ShouldBeTrue();
        depois.TotalCents.ShouldBe(antes.TotalCents);
        desmarcada.Reconciled.ShouldBeFalse();
        semCorpo.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    // --- Editar (FR-015, research R7) ---

    [Fact]
    public async Task Editar_compra_a_vista_altera_os_campos_e_move_a_fatura_pela_data()
    {
        var (lia, _, azul) = await AzulAsync();
        var uber = (await CreateAsync(lia, Purchase(azul.Id, "Uber", "10", "2026-03-10"))).Purchases.Single();
        uber.DueMonth.ShouldBe("2026-03");

        var editada = await ReadAsync<PurchaseDto>(await PutAsync(lia, uber, new { description = "Uber Eats", amount = "20,50", date = "2026-03-18", notes = "volta" }));

        editada.Description.ShouldBe("Uber Eats");
        editada.AmountCents.ShouldBe(2050);
        editada.Notes.ShouldBe("volta");
        editada.DueMonth.ShouldBe("2026-04");
        editada.ManuallyMoved.ShouldBeFalse();
        editada.Version.ShouldNotBe(uber.Version);
    }

    [Fact]
    public async Task Editar_compra_movida_a_mao_mantem_o_mes_ao_mudar_a_data()
    {
        var (lia, _, azul) = await AzulAsync();
        var uber = (await CreateAsync(lia, Purchase(azul.Id, "Uber", "10", "2026-03-10"))).Purchases.Single();
        var movida = (await ReadAsync<List<PurchaseDto>>(await lia.PostAsJsonAsync($"{PurchasesUrl}/{uber.Id}/move", new { direction = "next" }))).Single();

        var editada = await ReadAsync<PurchaseDto>(await PutAsync(lia, movida, new { date = "2026-03-11" }));

        editada.DueMonth.ShouldBe("2026-04");
        editada.ManuallyMoved.ShouldBeTrue();
    }

    [Fact]
    public async Task Editar_troca_de_cartao_leva_a_compra_para_a_fatura_do_novo_cartao()
    {
        var (lia, _, azul) = await AzulAsync();
        var verde = await NewCardAsync(lia, "Verde", 1, 10);
        var uber = (await CreateAsync(lia, Purchase(azul.Id, "Uber", "10", "2026-01-31"))).Purchases.Single();

        var editada = await ReadAsync<PurchaseDto>(await PutAsync(lia, uber, new { creditCardId = verde.Id }));

        editada.CreditCardId.ShouldBe(verde.Id);
        editada.DueMonth.ShouldBe("2026-02");
        (await GetCardAsync(lia, azul.Id)).OpenPastInvoices.ShouldBeEmpty();
        var arquivar = await lia.PostAsync($"/api/v1/credit-cards/{verde.Id}/archive", null);
        arquivar.StatusCode.ShouldBe(HttpStatusCode.OK);
        var paraArquivado = await PutAsync(lia, editada, new { creditCardId = verde.Id });
        paraArquivado.StatusCode.ShouldBe(HttpStatusCode.OK);
        var deVoltaParaArquivado = await PutAsync(lia, await GetAsync(lia, uber.Id), new { creditCardId = azul.Id });
        deVoltaParaArquivado.StatusCode.ShouldBe(HttpStatusCode.OK);
        var novamenteArquivado = await PutAsync(lia, await GetAsync(lia, uber.Id), new { creditCardId = verde.Id });
        novamenteArquivado.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeOfAsync(novamenteArquivado)).ShouldBe("credit_card.archived");
    }

    [Fact]
    public async Task Editar_exige_a_versao_atual()
    {
        var (lia, _, azul) = await AzulAsync();
        var uber = (await CreateAsync(lia, Purchase(azul.Id, "Uber", "10", "2026-03-10"))).Purchases.Single();
        (await PutAsync(lia, uber, new { description = "Uber 2" })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var velha = await PutAsync(lia, uber, new { description = "Uber 3" });
        var semVersao = await lia.PutAsJsonAsync(
            $"{PurchasesUrl}/{uber.Id}",
            new { description = "Uber", amount = "10", nature = "debit", date = "2026-03-10", creditCardId = azul.Id });

        velha.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(velha)).ShouldBe("concurrency_conflict");
        semVersao.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await GetAsync(lia, uber.Id)).Description.ShouldBe("Uber 2");
    }

    [Fact]
    public async Task Editar_para_fatura_paga_pede_confirmacao_so_quando_o_total_sobe()
    {
        var (lia, account, azul) = await AzulAsync();
        var lanche = await SeedPaidInvoiceAsync(account, azul, YearMonth.Of(2026, 3));
        var uber = (await CreateAsync(lia, Purchase(azul.Id, "Uber", "10", "2026-04-01"))).Purchases.Single();

        var paraFaturaPaga = await PutAsync(lia, uber, new { date = "2026-03-10" });
        var confirmada = await PutAsync(lia, uber, new { date = "2026-03-10", confirmPaidInvoice = true });

        paraFaturaPaga.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(paraFaturaPaga)).ShouldBe("card_purchase.invoice_paid_confirmation_required");
        confirmada.StatusCode.ShouldBe(HttpStatusCode.OK);

        // A fatura voltou a ter valor em aberto com a compra confirmada; a própria compra "Lanche" segue numa fatura sem pagamento total.
        var outraFatura = await SeedPaidInvoiceAsync(account, azul, YearMonth.Of(2026, 2));
        var paga = await GetAsync(lia, outraFatura.Id);
        var aumento = await PutAsync(lia, paga, new { amount = "250" });
        var reducao = await PutAsync(lia, paga, new { amount = "150" });
        aumento.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        reducao.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(lia, lanche.Id)).AmountCents.ShouldBe(20000);
    }

    [Fact]
    public async Task Escopo_invalido_na_edicao_e_recusado()
    {
        var (lia, _, azul) = await AzulAsync();
        var uber = (await CreateAsync(lia, Purchase(azul.Id, "Uber", "10", "2026-03-10"))).Purchases.Single();

        var response = await PutAsync(lia, uber, new { scope = "this_and_following" });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeOfAsync(response)).ShouldBe("scope.invalid");
    }

    private static async Task<List<PurchaseDto>> CreateCasaChinaAsync(HttpClient http, CreditCardDto card, string date = "2026-01-28") =>
        (await CreateAsync(http, Purchase(card.Id, "Casa China", "100", date, Installments(3, "total_amount")))).Purchases;

    [Fact]
    public async Task US3_4_descricao_e_observacao_valem_so_para_esta_ou_para_todas()
    {
        var (lia, _, azul) = await AzulAsync();
        var parcels = await CreateCasaChinaAsync(lia, azul);

        var soEsta = await ReadAsync<PurchaseDto>(await PutAsync(lia, parcels[1], new { description = "Casa Nova", notes = "so esta", scope = "this" }));
        var outras = await Task.WhenAll(parcels.Where(p => p.Id != parcels[1].Id).Select(p => GetAsync(lia, p.Id)));

        soEsta.DisplayDescription.ShouldBe("Casa Nova 2/3");
        soEsta.Notes.ShouldBe("so esta");
        outras.Select(p => p.DisplayDescription).ShouldBe(["Casa China 1/3", "Casa China 3/3"], ignoreOrder: true);
        outras.All(p => p.Notes is null).ShouldBeTrue();

        var todas = await ReadAsync<PurchaseDto>(await PutAsync(lia, soEsta, new { description = "Casa Linda", notes = "todas", scope = "all" }));

        todas.DisplayDescription.ShouldBe("Casa Linda 2/3");
        foreach (var p in parcels)
        {
            var atual = await GetAsync(lia, p.Id);
            atual.Description.ShouldBe("Casa Linda");
            atual.Notes.ShouldBe("todas");
        }
    }

    [Fact]
    public async Task US3_4_valor_so_afeta_a_parcela_editada_mesmo_com_escopo_todas()
    {
        var (lia, _, azul) = await AzulAsync();
        var parcels = await CreateCasaChinaAsync(lia, azul);

        var editada = await ReadAsync<PurchaseDto>(await PutAsync(lia, parcels[2], new { amount = "40,00", scope = "all" }));

        editada.AmountCents.ShouldBe(4000);
        (await GetAsync(lia, parcels[0].Id)).AmountCents.ShouldBe(3334);
        (await GetAsync(lia, parcels[1].Id)).AmountCents.ShouldBe(3333);
        var reconciled = await ReadAsync<PurchaseDto>(await lia.PatchAsJsonAsync($"{PurchasesUrl}/{parcels[0].Id}/reconciled", new { reconciled = true }));
        reconciled.Reconciled.ShouldBeTrue();
        (await GetAsync(lia, parcels[1].Id)).Reconciled.ShouldBeFalse();
    }

    [Fact]
    public async Task US3_4_data_da_compra_afeta_todas_as_parcelas_e_redistribui_as_faturas()
    {
        var (lia, _, azul) = await AzulAsync();
        var parcels = await CreateCasaChinaAsync(lia, azul);
        parcels.Select(p => p.DueMonth).ShouldBe(["2026-02", "2026-03", "2026-04"]);

        await ReadAsync<PurchaseDto>(await PutAsync(lia, parcels[1], new { date = "2026-03-10" }));

        var atuais = await Task.WhenAll(parcels.Select(p => GetAsync(lia, p.Id)));
        atuais.Select(p => p.Date).Distinct().ShouldBe([new DateOnly(2026, 3, 10)]);
        atuais.Select(p => p.DueMonth).ShouldBe(["2026-03", "2026-04", "2026-05"]);
    }

    [Fact]
    public async Task US3_4_parcela_movida_a_mao_nao_e_redistribuida_pela_data()
    {
        var (lia, _, azul) = await AzulAsync();
        var parcels = await CreateCasaChinaAsync(lia, azul);
        await ReadAsync<List<PurchaseDto>>(await lia.PostAsJsonAsync($"{PurchasesUrl}/{parcels[0].Id}/move", new { direction = "next" }));

        await ReadAsync<PurchaseDto>(await PutAsync(lia, parcels[1], new { date = "2026-03-10" }));

        var atuais = await Task.WhenAll(parcels.Select(p => GetAsync(lia, p.Id)));
        atuais.Select(p => p.DueMonth).ShouldBe(["2026-03", "2026-04", "2026-05"]);
        atuais[0].ManuallyMoved.ShouldBeTrue();
        atuais[0].Date.ShouldBe(new DateOnly(2026, 3, 10));
    }

    [Fact]
    public async Task US3_4_cartao_afeta_todas_as_parcelas_inclusive_as_movidas()
    {
        var (lia, _, azul) = await AzulAsync();
        var verde = await NewCardAsync(lia, "Verde", 1, 10);
        var parcels = await CreateCasaChinaAsync(lia, azul);
        await ReadAsync<List<PurchaseDto>>(await lia.PostAsJsonAsync($"{PurchasesUrl}/{parcels[2].Id}/move", new { direction = "next" }));

        await ReadAsync<PurchaseDto>(await PutAsync(lia, parcels[0], new { creditCardId = verde.Id }));

        var atuais = await Task.WhenAll(parcels.Select(p => GetAsync(lia, p.Id)));
        atuais.Select(p => p.CreditCardId).Distinct().ShouldBe([verde.Id]);
        // Verde fecha dia 1 e vence dia 10: compra de 28/01 → 1ª parcela em fev; a 3ª foi movida (abr → mai) e mantém o mês.
        atuais.Select(p => p.DueMonth).ShouldBe(["2026-02", "2026-03", "2026-05"]);
    }

    [Fact]
    public async Task Editar_parcela_para_credito_e_recusado()
    {
        var (lia, _, azul) = await AzulAsync();
        var parcels = await CreateCasaChinaAsync(lia, azul);

        var response = await PutAsync(lia, parcels[0], new { nature = "credit" });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeOfAsync(response)).ShouldBe("card_purchase.credit_must_be_single");
    }

    [Fact]
    public async Task Parcelamento_em_andamento_mantem_a_sequencia_ao_mudar_a_data()
    {
        var (lia, _, azul) = await AzulAsync();
        var parcels = (await CreateAsync(lia, Purchase(azul.Id, "Shoppe Casa", "102,74", "2026-06-20", Installments(8, "in_progress", 5, "2026-11")))).Purchases;

        await ReadAsync<PurchaseDto>(await PutAsync(lia, parcels[0], new { date = "2026-07-02" }));

        var atuais = await Task.WhenAll(parcels.Select(p => GetAsync(lia, p.Id)));
        atuais.Select(p => p.DueMonth).ShouldBe(["2026-11", "2026-12", "2027-01", "2027-02"]);
        atuais.Select(p => p.Date).Distinct().ShouldBe([new DateOnly(2026, 7, 2)]);
    }

    // --- Recriar o parcelamento (FR-015) ---

    [Fact]
    public async Task Plano_recria_as_parcelas_com_novo_total_e_exclui_as_antigas_no_mesmo_lote()
    {
        var (lia, _, azul) = await AzulAsync();
        var parcels = await CreateCasaChinaAsync(lia, azul);
        await lia.PatchAsJsonAsync($"{PurchasesUrl}/{parcels[0].Id}/reconciled", new { reconciled = true });
        var planId = parcels[0].InstallmentPlanId!.Value;

        var novas = await ReadAsync<List<PurchaseDto>>(
            await lia.PutAsJsonAsync($"/api/v1/installment-plans/{planId}", new { installmentTotal = 4, amount = "100,00", amountMode = "total_amount" }));

        novas.Select(p => (p.DisplayDescription, p.AmountCents, p.DueMonth)).ShouldBe(
        [
            ("Casa China 1/4", 2500L, "2026-02"),
            ("Casa China 2/4", 2500L, "2026-03"),
            ("Casa China 3/4", 2500L, "2026-04"),
            ("Casa China 4/4", 2500L, "2026-05"),
        ]);
        novas.Select(p => p.InstallmentPlanId).Distinct().ShouldBe([planId]);
        novas.Select(p => p.Id).Intersect(parcels.Select(p => p.Id)).ShouldBeEmpty();
        foreach (var antiga in parcels)
        {
            (await lia.GetAsync($"{PurchasesUrl}/{antiga.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        (await CountPurchasesAsync(azul.Id)).ShouldBe(4);
    }

    [Fact]
    public async Task Plano_pelo_valor_da_parcela_repete_o_valor()
    {
        var (lia, _, azul) = await AzulAsync();
        var parcels = await CreateCasaChinaAsync(lia, azul);

        var novas = await ReadAsync<List<PurchaseDto>>(
            await lia.PutAsJsonAsync($"/api/v1/installment-plans/{parcels[0].InstallmentPlanId}", new { installmentTotal = 2, amount = "70", amountMode = "installment_amount" }));

        novas.Select(p => p.AmountCents).ShouldBe([7000L, 7000L]);
    }

    [Fact]
    public async Task Plano_com_parcela_em_fatura_paga_e_recusado_e_nada_muda()
    {
        var (lia, account, azul) = await AzulAsync();
        var parcels = await CreateCasaChinaAsync(lia, azul);
        parcels[0].DueMonth.ShouldBe("2026-02");

        // Paga a fatura de fevereiro (R$ 33,34, a 1ª parcela).
        var invoiceId = await WithDbAsync(
            Factory,
            account,
            async (db, _) => (await db.Set<Invoice>().SingleAsync(i => i.CreditCardId == azul.Id && i.DueMonth == new DateOnly(2026, 2, 1))).Id);
        await Months.MonthSeeding.SeedAsync(Factory, account, InvoicePayment.Create(invoiceId, azul.PaymentWalletId, Money.FromCents(3334), new DateOnly(2026, 2, 20)));

        var response = await lia.PutAsJsonAsync(
            $"/api/v1/installment-plans/{parcels[0].InstallmentPlanId}", new { installmentTotal = 4, amount = "100", amountMode = "total_amount" });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(response)).ShouldBe("installment_plan.has_paid_invoice");
        (await RemainingAsync(lia, parcels)).ShouldBe(3);
        (await GetAsync(lia, parcels[0].Id)).DisplayDescription.ShouldBe("Casa China 1/3");
    }

    [Fact]
    public async Task Plano_em_andamento_so_aceita_valor_da_parcela_e_mantem_a_proxima_parcela()
    {
        var (lia, _, azul) = await AzulAsync();
        var parcels = (await CreateAsync(lia, Purchase(azul.Id, "Shoppe Casa", "102,74", "2026-06-20", Installments(8, "in_progress", 5, "2026-11")))).Purchases;
        var url = $"/api/v1/installment-plans/{parcels[0].InstallmentPlanId}";

        var total = await lia.PutAsJsonAsync(url, new { installmentTotal = 8, amount = "100", amountMode = "total_amount" });
        var menor = await lia.PutAsJsonAsync(url, new { installmentTotal = 4, amount = "100", amountMode = "installment_amount" });
        var novas = await ReadAsync<List<PurchaseDto>>(await lia.PutAsJsonAsync(url, new { installmentTotal = 9, amount = "100", amountMode = "installment_amount" }));

        total.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeOfAsync(total)).ShouldBe("installment_plan.in_progress_needs_installment_amount");
        menor.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeOfAsync(menor)).ShouldBe("installment.first_number_invalid");
        novas.Select(p => (p.InstallmentNumber, p.DueMonth)).ShouldBe([(5, "2026-11"), (6, "2026-12"), (7, "2027-01"), (8, "2027-02"), (9, "2027-03")]);
    }

    [Fact]
    public async Task Plano_invalido_inexistente_ou_de_outro_espaco()
    {
        var (lia, _, azul) = await AzulAsync();
        var (leo, _) = await NewSpaceAsync("Léo");
        var parcels = await CreateCasaChinaAsync(lia, azul);
        var url = $"/api/v1/installment-plans/{parcels[0].InstallmentPlanId}";

        var deOutro = await leo.PutAsJsonAsync(url, new { installmentTotal = 4, amount = "100", amountMode = "total_amount" });
        var modo = await lia.PutAsJsonAsync(url, new { installmentTotal = 4, amount = "100", amountMode = "in_progress" });
        var total = await lia.PutAsJsonAsync(url, new { installmentTotal = 49, amount = "100", amountMode = "total_amount" });
        var semTotal = await lia.PutAsJsonAsync(url, new { amount = "100", amountMode = "total_amount" });

        deOutro.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOfAsync(modo)).ShouldBe("installment.amount_mode_invalid");
        (await CodeOfAsync(total)).ShouldBe("installment.total_invalid");
        semTotal.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CountReadableAsync(lia, parcels)).ShouldBe(3);
    }

    private static async Task<int> CountReadableAsync(HttpClient http, List<PurchaseDto> parcels) =>
        (await Task.WhenAll(parcels.Select(async p => (await http.GetAsync($"{PurchasesUrl}/{p.Id}")).StatusCode))).Count(s => s == HttpStatusCode.OK);

    // --- Excluir (FR-016, RT-09) ---

    private static async Task<HttpResponseMessage> DeleteAsync(HttpClient http, Guid id, string? scope = null) =>
        await http.DeleteAsync($"{PurchasesUrl}/{id}{(scope is null ? string.Empty : $"?scope={scope}")}");

    private static async Task<int> RemainingAsync(HttpClient http, IEnumerable<PurchaseDto> parcels) =>
        await CountReadableAsync(http, parcels.ToList());

    [Fact]
    public async Task US3_5_excluir_so_esta_parcela()
    {
        var (lia, _, azul) = await AzulAsync();
        var parcels = await CreateCasaChinaAsync(lia, azul);

        var response = await DeleteAsync(lia, parcels[1].Id);

        (await ReadAsync<DeletionDto>(response)).DeletionBatchId.ShouldNotBe(Guid.Empty);
        (await lia.GetAsync($"{PurchasesUrl}/{parcels[1].Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await RemainingAsync(lia, parcels)).ShouldBe(2);
    }

    [Fact]
    public async Task US3_5_excluir_esta_e_as_seguintes_e_desfazer_pelo_lote()
    {
        var (lia, _, azul) = await AzulAsync();
        var parcels = await CreateCasaChinaAsync(lia, azul);

        var batch = (await ReadAsync<DeletionDto>(await DeleteAsync(lia, parcels[1].Id, "this_and_following"))).DeletionBatchId;

        (await RemainingAsync(lia, parcels)).ShouldBe(1);
        (await GetAsync(lia, parcels[0].Id)).DisplayDescription.ShouldBe("Casa China 1/3");

        var restore = await lia.PostAsync($"/api/v1/deletions/{batch}/restore", null);
        restore.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await RemainingAsync(lia, parcels)).ShouldBe(3);
    }

    [Fact]
    public async Task US3_5_excluir_todas_leva_o_plano_junto_e_o_desfazer_traz_tudo_de_volta()
    {
        var (lia, _, azul) = await AzulAsync();
        var parcels = await CreateCasaChinaAsync(lia, azul);

        var batch = (await ReadAsync<DeletionDto>(await DeleteAsync(lia, parcels[2].Id, "all"))).DeletionBatchId;

        (await RemainingAsync(lia, parcels)).ShouldBe(0);
        (await GetCardAsync(lia, azul.Id)).HasPurchases.ShouldBeTrue();
        (await lia.PostAsync($"/api/v1/deletions/{batch}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await RemainingAsync(lia, parcels)).ShouldBe(3);
        var recriar = await lia.PutAsJsonAsync(
            $"/api/v1/installment-plans/{parcels[0].InstallmentPlanId}", new { installmentTotal = 2, amount = "80", amountMode = "total_amount" });
        recriar.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Excluir_compra_a_vista_e_escopo_invalido()
    {
        var (lia, _, azul) = await AzulAsync();
        var uber = (await CreateAsync(lia, Purchase(azul.Id, "Uber", "10", "2026-03-10"))).Purchases.Single();

        var invalido = await DeleteAsync(lia, uber.Id, "metade");
        var ok = await DeleteAsync(lia, uber.Id, "this_and_following");
        var denovo = await DeleteAsync(lia, uber.Id);

        invalido.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        denovo.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await GetCardAsync(lia, azul.Id)).CurrentInvoice!.TotalCents.ShouldBe(0);
    }

    // --- Saldo levado (FR-020) ---

    private async Task<(CardPurchase Credit, CardPurchase Debit)> SeedCarryOverPairAsync(TestAccount account, CreditCardDto card)
    {
        var march = Invoice.Create(card.Id, YearMonth.Of(2026, 3), new DateOnly(2026, 3, 18), new DateOnly(2026, 3, 25));
        var april = Invoice.Create(card.Id, YearMonth.Of(2026, 4), new DateOnly(2026, 4, 18), new DateOnly(2026, 4, 25));
        var pair = Guid.CreateVersion7();
        var credit = CardPurchase.CreateCarryOver(card.Id, march.Id, "Saldo levado para 04/2026", Money.FromCents(5000), PurchaseNature.Credit, new DateOnly(2026, 3, 26), pair);
        var debit = CardPurchase.CreateCarryOver(card.Id, april.Id, "Saldo da fatura 03/2026", Money.FromCents(5000), PurchaseNature.Debit, new DateOnly(2026, 3, 26), pair);
        await Months.MonthSeeding.SeedAsync(Factory, account, march, april, credit, debit);
        return (credit, debit);
    }

    [Fact]
    public async Task Excluir_uma_compra_do_par_de_saldo_levado_exclui_as_duas_no_mesmo_lote()
    {
        var (lia, account, azul) = await AzulAsync();
        var (credit, debit) = await SeedCarryOverPairAsync(account, azul);

        var batch = (await ReadAsync<DeletionDto>(await DeleteAsync(lia, debit.Id))).DeletionBatchId;

        (await lia.GetAsync($"{PurchasesUrl}/{credit.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await lia.GetAsync($"{PurchasesUrl}/{debit.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await lia.PostAsync($"/api/v1/deletions/{batch}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetAsync(lia, credit.Id)).Origin.ShouldBe("carry_over");
        (await GetAsync(lia, debit.Id)).AmountCents.ShouldBe(5000);
    }

    [Fact]
    public async Task Compra_de_saldo_levado_nao_e_editavel_mas_pode_ser_conferida()
    {
        var (lia, account, azul) = await AzulAsync();
        var (credit, _) = await SeedCarryOverPairAsync(account, azul);
        var dto = await GetAsync(lia, credit.Id);

        var edicao = await PutAsync(lia, dto, new { amount = "60" });
        var conferida = await lia.PatchAsJsonAsync($"{PurchasesUrl}/{credit.Id}/reconciled", new { reconciled = true });

        edicao.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeOfAsync(edicao)).ShouldBe("card_purchase.carry_over_not_editable");
        conferida.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // --- Mover (FR-028) ---

    [Fact]
    public async Task US7_2_mover_compra_para_a_fatura_seguinte_e_voltar()
    {
        var (lia, _, azul) = await AzulAsync();
        var uber = (await CreateAsync(lia, Purchase(azul.Id, "Uber", "10", "2026-03-10"))).Purchases.Single();

        var next = (await ReadAsync<List<PurchaseDto>>(await lia.PostAsJsonAsync($"{PurchasesUrl}/{uber.Id}/move", new { direction = "next" }))).Single();
        var back = (await ReadAsync<List<PurchaseDto>>(await lia.PostAsJsonAsync($"{PurchasesUrl}/{uber.Id}/move", new { direction = "previous", scope = "this" }))).Single();

        next.DueMonth.ShouldBe("2026-04");
        next.ManuallyMoved.ShouldBeTrue();
        back.DueMonth.ShouldBe("2026-03");
        back.ManuallyMoved.ShouldBeTrue();
        back.Date.ShouldBe(uber.Date);
        (await GetCardAsync(lia, azul.Id)).NextInvoice!.TotalCents.ShouldBe(0);
    }

    [Fact]
    public async Task US7_3_mover_esta_e_as_seguintes_avanca_cada_parcela_uma_fatura()
    {
        var (lia, _, azul) = await AzulAsync();
        var parcels = (await CreateAsync(lia, Purchase(azul.Id, "Notebook", "400", "2026-03-10", Installments(4, "total_amount")))).Purchases;
        parcels.Select(p => p.DueMonth).ShouldBe(["2026-03", "2026-04", "2026-05", "2026-06"]);

        var moved = await ReadAsync<List<PurchaseDto>>(
            await lia.PostAsJsonAsync($"{PurchasesUrl}/{parcels[1].Id}/move", new { direction = "next", scope = "this_and_following" }));

        moved.Select(p => (p.InstallmentNumber, p.DueMonth, p.ManuallyMoved)).ShouldBe([(2, "2026-05", true), (3, "2026-06", true), (4, "2026-07", true)]);
        var primeira = await GetAsync(lia, parcels[0].Id);
        primeira.DueMonth.ShouldBe("2026-03");
        primeira.ManuallyMoved.ShouldBeFalse();
    }

    [Fact]
    public async Task Mover_so_esta_parcela_e_validacoes_do_movimento()
    {
        var (lia, _, azul) = await AzulAsync();
        var parcels = (await CreateAsync(lia, Purchase(azul.Id, "Notebook", "400", "2026-03-10", Installments(4, "total_amount")))).Purchases;

        var moved = (await ReadAsync<List<PurchaseDto>>(await lia.PostAsJsonAsync($"{PurchasesUrl}/{parcels[1].Id}/move", new { direction = "previous" }))).Single();
        var direcao = await lia.PostAsJsonAsync($"{PurchasesUrl}/{parcels[1].Id}/move", new { direction = "cima" });
        var todas = await lia.PostAsJsonAsync($"{PurchasesUrl}/{parcels[1].Id}/move", new { direction = "next", scope = "all" });
        var inexistente = await lia.PostAsJsonAsync($"{PurchasesUrl}/{Guid.NewGuid()}/move", new { direction = "next" });

        moved.DueMonth.ShouldBe("2026-03");
        (await GetAsync(lia, parcels[2].Id)).DueMonth.ShouldBe("2026-05");
        (await CodeOfAsync(direcao)).ShouldBe("direction.invalid");
        (await CodeOfAsync(todas)).ShouldBe("scope.invalid");
        inexistente.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // --- Sugestões (research R9) ---

    private sealed record SuggestionDto(string Description, int Uses, Guid? WalletId, Guid? CreditCardId);

    private static async Task<List<SuggestionDto>> SuggestAsync(HttpClient http, string q, string kind) =>
        (await http.GetFromJsonAsync<List<SuggestionDto>>($"/api/v1/entries/suggestions?q={q}&kind={kind}", JsonDefaults.Options))!;

    [Fact]
    public async Task Sugestoes_de_compra_no_cartao_trazem_o_cartao_da_compra_mais_recente()
    {
        var (lia, _, azul) = await AzulAsync();
        var verde = await NewCardAsync(lia, "Verde", 1, 10);
        await CreateAsync(lia, Purchase(azul.Id, "Uber", "10", "2026-03-01"));
        await CreateAsync(lia, Purchase(verde.Id, "Uber", "12", "2026-03-10"));
        await CreateAsync(lia, Purchase(azul.Id, "Casa China", "100", "2026-03-05", Installments(3, "total_amount")));

        var uber = (await SuggestAsync(lia, "ube", "card_purchase")).ShouldHaveSingleItem();
        var casa = (await SuggestAsync(lia, "casa", "card_purchase")).ShouldHaveSingleItem();

        uber.Description.ShouldBe("Uber");
        uber.Uses.ShouldBe(2);
        uber.CreditCardId.ShouldBe(verde.Id);
        uber.WalletId.ShouldBeNull();
        casa.Description.ShouldBe("Casa China");
        casa.Uses.ShouldBe(1); // um parcelamento é um uso, não uma parcela
        casa.CreditCardId.ShouldBe(azul.Id);
    }

    [Fact]
    public async Task Sugestao_de_cartao_arquivado_vem_sem_cartao_e_compras_contam_nos_usos_de_entrada_e_saida()
    {
        var (lia, _, azul) = await AzulAsync();
        await CreateAsync(lia, Purchase(azul.Id, "Mercado", "10", "2026-03-01"));
        (await lia.PostAsync($"/api/v1/credit-cards/{azul.Id}/archive", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var cartao = (await SuggestAsync(lia, "merc", "card_purchase")).ShouldHaveSingleItem();
        var saida = (await SuggestAsync(lia, "merc", "expense")).ShouldHaveSingleItem();
        var semNada = await SuggestAsync(lia, "zzz", "card_purchase");

        cartao.CreditCardId.ShouldBeNull();
        saida.Uses.ShouldBe(1);
        saida.WalletId.ShouldBeNull();
        saida.CreditCardId.ShouldBeNull();
        semNada.ShouldBeEmpty();
    }

    [Fact]
    public async Task Sugestoes_ignoram_compras_de_outro_espaco_e_com_mais_de_12_meses()
    {
        var (lia, _, azul) = await AzulAsync();
        var (leo, _) = await NewSpaceAsync("Léo");
        var deLeo = await NewCardAsync(leo, "Verde", 1, 10);
        await CreateAsync(leo, Purchase(deLeo.Id, "Padaria", "10", "2026-03-01"));
        await CreateAsync(lia, Purchase(azul.Id, "Padaria Velha", "10", "2025-01-01"));

        (await SuggestAsync(lia, "padaria", "card_purchase")).ShouldBeEmpty();
    }
}
