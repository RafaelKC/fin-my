using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Api.Endpoints;
using FinMy.Api.Tests.Cards;
using FinMy.Api.Tests.Entries;
using FinMy.Api.Tests.OAuth;
using FinMy.Api.Tests.Support;
using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;

namespace FinMy.Api.Tests.Widget;

/// <summary>
/// 009 T001 / US2: <c>GET /api/v1/widget/*</c> (somente leitura) com o token do cliente <c>finmy-android</c>. Espaço A = "Lia", com o
/// cartão Verde (fecha dia 18, vence dia 25) e a carteira Azul; B = "Léo". "Hoje" é 2026-03-20, depois do fechamento de março:
/// a fatura atual é a de vencimento em 2026-04.
/// </summary>
public class WidgetApiTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private const string AppUrl = "http://finmy.exemplo.com";
    private const string CardsPath = "/api/v1/widget/credit-cards";

    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?>
    {
        ["APP_URL"] = AppUrl,
        ["RateLimiting:Auth:PermitLimit"] = "100000",
    };

    protected override IEndpointModule[] ExtraEndpoints => [new OAuthProbeEndpoints()];

    protected override IClock? Clock => new FixedTodayClock(new DateOnly(2026, 3, 20));

    private OAuthFlow Flow => field ??= new OAuthFlow(Factory);

    private async Task<HttpResponseMessage> GetAsync(string? bearer, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        return await Flow.Raw.SendAsync(request);
    }

    private async Task<JsonElement> OkAsync(string bearer, string path)
    {
        var response = await GetAsync(bearer, path);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>Espaço com a carteira Azul, o cartão Verde e a conexão do widget: o app (REST) para semear e o token do widget.</summary>
    private async Task<(HttpClient App, TestAccount Account, Guid Wallet, CreditCardDto Verde, string Widget)> NewLiaAsync()
    {
        var (app, account) = await CardTestSupport.NewSpaceAsync(Factory, "Lia");
        var azul = await CardTestSupport.CreateWalletAsync(app, "Azul");
        var verde = await CardTestSupport.CreateCardAsync(app, "Verde", azul.Id);
        var widget = (await Flow.ConnectAndroidAsync(account.AccessToken)).AccessToken;
        return (app, account, azul.Id, verde, widget);
    }

    private static async Task PurchaseAsync(HttpClient app, Guid card, string description, string amount, string date)
    {
        var response = await app.PostAsJsonAsync("/api/v1/card-purchases", new { description, amount, date, nature = "debit", creditCardId = card });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private static async Task<string> CodeOfAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;

    // ---- Lista de cartões ----

    [Fact]
    public async Task Lista_so_os_cartoes_ativos_do_espaco_em_ordem_de_nome_com_today()
    {
        var (app, _, azul, verde, widget) = await NewLiaAsync();
        var amarelo = await CardTestSupport.CreateCardAsync(app, "Amarelo", azul, closingDay: 5, dueDay: 12);
        var velho = await CardTestSupport.CreateCardAsync(app, "Velho", azul);
        (await app.PostAsync($"/api/v1/credit-cards/{velho.Id}/archive", null)).EnsureSuccessStatusCode();

        var body = await OkAsync(widget, CardsPath);

        body.GetProperty("today").GetString().ShouldBe("2026-03-20");
        var cards = body.GetProperty("cards").EnumerateArray().ToList();
        cards.Select(c => c.GetProperty("name").GetString()).ShouldBe(["Amarelo", "Verde"]);
        cards[0].GetProperty("id").GetGuid().ShouldBe(amarelo.Id);
        cards[0].GetProperty("closingDay").GetInt32().ShouldBe(5);
        cards[0].GetProperty("dueDay").GetInt32().ShouldBe(12);
        cards[1].GetProperty("id").GetGuid().ShouldBe(verde.Id);
        cards[1].GetProperty("color").GetString().ShouldBe(verde.Color);
    }

    [Fact]
    public async Task Sem_cartoes_a_lista_vem_vazia()
    {
        var (_, account) = await CardTestSupport.NewSpaceAsync(Factory, "Duda");
        var widget = (await Flow.ConnectAndroidAsync(account.AccessToken)).AccessToken;

        (await OkAsync(widget, CardsPath)).GetProperty("cards").GetArrayLength().ShouldBe(0);
    }

    // ---- Fatura atual ----

    [Fact]
    public async Task Fatura_atual_e_a_do_vencimento_em_que_cairia_uma_compra_de_hoje_com_os_valores_do_contrato()
    {
        var (app, _, _, verde, widget) = await NewLiaAsync();
        await PurchaseAsync(app, verde.Id, "Mercado", "300,00", "2026-03-20");
        await PurchaseAsync(app, verde.Id, "Cinema", "60,00", "2026-03-05"); // fatura de março (já fechada)
        (await app.PutAsJsonAsync($"/api/v1/credit-cards/{verde.Id}/invoices/2026-04/estimate", new { amount = "800,00" })).EnsureSuccessStatusCode();

        var body = await OkAsync(widget, $"{CardsPath}/{verde.Id}/current-invoice");

        body.GetProperty("today").GetString().ShouldBe("2026-03-20");
        var card = body.GetProperty("card");
        card.GetProperty("id").GetGuid().ShouldBe(verde.Id);
        card.GetProperty("name").GetString().ShouldBe("Verde");
        card.GetProperty("color").GetString().ShouldBe(verde.Color);
        var invoice = body.GetProperty("invoice");
        invoice.GetProperty("dueMonth").GetString().ShouldBe("2026-04");
        invoice.GetProperty("closingDate").GetString().ShouldBe("2026-04-18");
        invoice.GetProperty("dueDate").GetString().ShouldBe("2026-04-25");
        invoice.GetProperty("status").GetString().ShouldBe("open");
        invoice.GetProperty("totalCents").GetInt64().ShouldBe(30000);
        invoice.GetProperty("totalText").GetString().ShouldBe("R$ 300,00");
        invoice.GetProperty("plannedCents").GetInt64().ShouldBe(80000);
        invoice.GetProperty("plannedText").GetString().ShouldBe("R$ 800,00");
        invoice.GetProperty("estimateCents").GetInt64().ShouldBe(80000);
        invoice.GetProperty("paidCents").GetInt64().ShouldBe(0);
        invoice.GetProperty("remainingCents").GetInt64().ShouldBe(30000);
        invoice.GetProperty("remainingText").GetString().ShouldBe("R$ 300,00");
        body.GetProperty("appPath").GetString().ShouldBe($"/cartoes/{verde.Id}/faturas/2026-04");
    }

    [Fact]
    public async Task Sem_compras_nem_estimativa_tudo_zerado_e_estimativa_nula()
    {
        var (_, _, _, verde, widget) = await NewLiaAsync();

        var invoice = (await OkAsync(widget, $"{CardsPath}/{verde.Id}/current-invoice")).GetProperty("invoice");

        invoice.GetProperty("totalCents").GetInt64().ShouldBe(0);
        invoice.GetProperty("plannedCents").GetInt64().ShouldBe(0);
        invoice.GetProperty("remainingText").GetString().ShouldBe("R$ 0,00");
        invoice.GetProperty("estimateCents").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    /// <summary>SC-002: os números do widget são os da tela da fatura (<c>GET /credit-cards/{id}/invoices/{mês}</c>) para o mesmo cartão e mês.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Valores_do_widget_sao_identicos_aos_da_rota_da_fatura(bool withEstimate, bool withPayment)
    {
        var (app, _, _, verde, widget) = await NewLiaAsync();
        await PurchaseAsync(app, verde.Id, "Mercado", "300,00", "2026-03-20");
        await PurchaseAsync(app, verde.Id, "Farmácia", "45,90", "2026-03-21");
        if (withEstimate)
        {
            (await app.PutAsJsonAsync($"/api/v1/credit-cards/{verde.Id}/invoices/2026-04/estimate", new { amount = "200,00" })).EnsureSuccessStatusCode();
        }

        if (withPayment)
        {
            var payment = await app.PostAsJsonAsync($"/api/v1/credit-cards/{verde.Id}/invoices/2026-04/payments", new { amount = "100,00", date = "2026-03-20" });
            payment.StatusCode.ShouldBe(HttpStatusCode.Created, await payment.Content.ReadAsStringAsync());
        }

        var invoice = (await OkAsync(widget, $"{CardsPath}/{verde.Id}/current-invoice")).GetProperty("invoice");
        var screen = await app.GetFromJsonAsync<JsonElement>($"/api/v1/credit-cards/{verde.Id}/invoices/{invoice.GetProperty("dueMonth").GetString()}");

        foreach (var field in new[] { "dueMonth", "closingDate", "dueDate", "status" })
        {
            invoice.GetProperty(field).GetString().ShouldBe(screen.GetProperty(field).GetString(), field);
        }

        foreach (var field in new[] { "totalCents", "paidCents", "remainingCents" })
        {
            invoice.GetProperty(field).GetInt64().ShouldBe(screen.GetProperty(field).GetInt64(), field);
        }

        invoice.GetProperty("totalCents").GetInt64().ShouldBe(34590);
        invoice.GetProperty("paidCents").GetInt64().ShouldBe(withPayment ? 10000 : 0);

        // Planejado: a mesma regra da 007 (estimativa vigente × total × pago).
        var estimate = screen.GetProperty("estimateActive").GetBoolean() ? screen.GetProperty("estimateCents").GetInt64() : (long?)null;
        var expectedPlanned = Math.Max(Math.Max(Math.Max(screen.GetProperty("totalCents").GetInt64(), screen.GetProperty("paidCents").GetInt64()), estimate ?? 0), 0);
        invoice.GetProperty("plannedCents").GetInt64().ShouldBe(expectedPlanned);
        if (estimate is { } active)
        {
            invoice.GetProperty("estimateCents").GetInt64().ShouldBe(active);
        }
        else
        {
            invoice.GetProperty("estimateCents").ValueKind.ShouldBe(JsonValueKind.Null);
        }
    }

    /// <summary>Gráfico do widget: as 6 últimas faturas até a atual, com os mesmos totais da rota da fatura.</summary>
    [Fact]
    public async Task Historico_traz_as_seis_ultimas_faturas_ate_a_atual_com_os_totais_da_rota_da_fatura()
    {
        var (app, _, _, verde, widget) = await NewLiaAsync();
        await PurchaseAsync(app, verde.Id, "Mercado", "300,00", "2026-03-20");
        await PurchaseAsync(app, verde.Id, "Farmácia", "45,90", "2026-01-21");
        await PurchaseAsync(app, verde.Id, "Livro", "80,00", "2025-12-05");

        var body = await OkAsync(widget, $"{CardsPath}/{verde.Id}/current-invoice");
        var current = body.GetProperty("invoice").GetProperty("dueMonth").GetString()!;
        var history = body.GetProperty("history").EnumerateArray().ToList();

        history.Count.ShouldBe(6);
        history[^1].GetProperty("dueMonth").GetString().ShouldBe(current);
        var months = history.Select(h => DateOnly.ParseExact(h.GetProperty("dueMonth").GetString()! + "-01", "yyyy-MM-dd")).ToList();
        for (var i = 1; i < months.Count; i++)
        {
            months[i].ShouldBe(months[i - 1].AddMonths(1));
        }

        foreach (var point in history)
        {
            var month = point.GetProperty("dueMonth").GetString();
            var screen = await app.GetFromJsonAsync<JsonElement>($"/api/v1/credit-cards/{verde.Id}/invoices/{month}");
            point.GetProperty("totalCents").GetInt64().ShouldBe(screen.GetProperty("totalCents").GetInt64(), month);
        }

        history.Sum(h => h.GetProperty("totalCents").GetInt64()).ShouldBe(30000 + 4590 + 8000);
    }

    [Fact]
    public async Task Cartao_arquivado_ou_excluido_ou_desconhecido_e_404_credit_card_not_found()
    {
        var (app, _, azul, verde, widget) = await NewLiaAsync();
        var excluido = await CardTestSupport.CreateCardAsync(app, "Rosa", azul);
        (await app.DeleteAsync($"/api/v1/credit-cards/{excluido.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await app.PostAsync($"/api/v1/credit-cards/{verde.Id}/archive", null)).EnsureSuccessStatusCode();

        foreach (var id in new[] { verde.Id, excluido.Id, Guid.NewGuid() })
        {
            var response = await GetAsync(widget, $"{CardsPath}/{id}/current-invoice");

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await CodeOfAsync(response)).ShouldBe("credit_card.not_found");
        }
    }

    // ---- Isolamento (FR-008) ----

    [Fact]
    public async Task Token_do_widget_so_enxerga_o_espaco_de_quem_autorizou()
    {
        var (_, _, _, verde, liaWidget) = await NewLiaAsync();
        var (leoApp, leo) = await CardTestSupport.NewSpaceAsync(Factory, "Léo");
        var rosa = await CardTestSupport.CreateWalletAsync(leoApp, "Rosa");
        var roxo = await CardTestSupport.CreateCardAsync(leoApp, "Roxo", rosa.Id);
        var leoWidget = (await Flow.ConnectAndroidAsync(leo.AccessToken)).AccessToken;

        (await OkAsync(leoWidget, CardsPath)).GetProperty("cards").EnumerateArray().Select(c => c.GetProperty("name").GetString()).ShouldBe(["Roxo"]);
        (await OkAsync(liaWidget, CardsPath)).GetProperty("cards").EnumerateArray().Select(c => c.GetProperty("name").GetString()).ShouldBe(["Verde"]);

        var other = await GetAsync(leoWidget, $"{CardsPath}/{verde.Id}/current-invoice");
        other.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOfAsync(other)).ShouldBe("credit_card.not_found");
        (await GetAsync(liaWidget, $"{CardsPath}/{roxo.Id}/current-invoice")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Token_do_widget_tem_canal_android_widget_e_so_leitura()
    {
        var (_, account, _, verde, widget) = await NewLiaAsync();

        var who = await OkAsync(widget, OAuthProbeEndpoints.WhoAmIPath);
        who.GetProperty("channel").GetString().ShouldBe("AndroidWidget");
        who.GetProperty("tenantId").GetGuid().ShouldBe(account.Session.Tenant.Id);

        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete })
        {
            using var request = new HttpRequestMessage(method, $"{CardsPath}/{verde.Id}/current-invoice");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", widget);
            ((int)(await Flow.Raw.SendAsync(request)).StatusCode).ShouldBeOneOf(404, 405);
        }
    }

    // ---- Quem não pode ----

    [Fact]
    public async Task Sem_token_com_jwt_do_app_ou_token_do_claude_e_401()
    {
        var (_, account, _, verde, _) = await NewLiaAsync();
        var (_, claude) = await Flow.ConnectAsync(account.AccessToken);

        foreach (var path in new[] { CardsPath, $"{CardsPath}/{verde.Id}/current-invoice" })
        {
            (await GetAsync(null, path)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await GetAsync("token-inventado", path)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await GetAsync(account.AccessToken, path)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            using var comClaude = await GetAsync(claude.AccessToken, path);
            comClaude.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            ShouldBeAsciiHeaders(comClaude);
        }
    }

    [Fact]
    public async Task Token_do_widget_e_negado_no_mcp_e_na_api_do_app()
    {
        var (_, _, _, _, widget) = await NewLiaAsync();

        (await GetAsync(widget, "/api/v1/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await GetAsync(widget, "/api/v1/credit-cards")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var mcp = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"teste","version":"1"}}}""", System.Text.Encoding.UTF8, "application/json"),
        };
        mcp.Headers.Authorization = new AuthenticationHeaderValue("Bearer", widget);
        mcp.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        mcp.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var response = await Flow.Raw.SendAsync(mcp);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().ShouldContain("resource_metadata");
        ShouldBeAsciiHeaders(response);
    }

    /// <summary>O TestServer aceita qualquer caractere em cabeçalho, mas o Kestrel só aceita ASCII e responderia 500 (achado na validação 009 contra a stack real).</summary>
    private static void ShouldBeAsciiHeaders(HttpResponseMessage response)
    {
        foreach (var header in response.Headers)
        {
            foreach (var value in header.Value)
            {
                value.ShouldAllBe(c => c < 128, $"cabeçalho {header.Key} com caractere não-ASCII: {value}");
            }
        }
    }

    [Fact]
    public async Task Token_do_claude_continua_valendo_no_mcp()
    {
        var (_, account, _, _, _) = await NewLiaAsync();
        var (_, claude) = await Flow.ConnectAsync(account.AccessToken);

        await using var session = await Mcp.McpSession.ConnectAsync(Factory, claude.AccessToken);

        (await session.OkAsync("list_catalog")).GetProperty("creditCards").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Desconectar_o_app_android_derruba_o_widget_na_proxima_chamada()
    {
        var (app, _, _, verde, widget) = await NewLiaAsync();
        (await GetAsync(widget, $"{CardsPath}/{verde.Id}/current-invoice")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var connections = await app.GetFromJsonAsync<JsonElement>("/api/v1/me/connected-apps");
        var connection = connections.EnumerateArray().Single();
        connection.GetProperty("name").GetString().ShouldBe("App Fin-My (Android)");
        (await app.DeleteAsync($"/api/v1/me/connected-apps/{connection.GetProperty("id").GetString()}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await GetAsync(widget, $"{CardsPath}/{verde.Id}/current-invoice")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await GetAsync(widget, CardsPath)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
