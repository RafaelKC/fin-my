using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Api.Tests.Cards;
using FinMy.Api.Tests.Entries;
using FinMy.Api.Tests.OAuth;
using FinMy.Api.Tests.Support;
using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace FinMy.Api.Tests.Mcp;

/// <summary>Conexão MCP de teste: o cliente oficial do SDK contra o <c>/mcp</c> da API em memória, com o token do conector.</summary>
internal sealed class McpSession(McpClient client) : IAsyncDisposable
{
    public McpClient Client { get; } = client;

    public static async Task<McpSession> ConnectAsync(ApiFactory factory, string accessToken)
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp }, http, ownsHttpClient: true);
        return new McpSession(await McpClient.CreateAsync(transport));
    }

    /// <summary>Chama a ferramenta; devolve o JSON do texto e se veio como erro de ferramenta.</summary>
    public async Task<(JsonElement Body, bool IsError)> CallAsync(string tool, object? arguments = null)
    {
        var args = (arguments is null ? new Dictionary<string, object?>() : arguments.GetType().GetProperties()
            .ToDictionary(p => p.Name, p => p.GetValue(arguments), StringComparer.Ordinal)) as IReadOnlyDictionary<string, object?>;
        var result = await Client.CallToolAsync(tool, args);
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        using var document = JsonDocument.Parse(text);
        return (document.RootElement.Clone(), result.IsError == true);
    }

    public async Task<JsonElement> OkAsync(string tool, object? arguments = null)
    {
        var (body, isError) = await CallAsync(tool, arguments);
        isError.ShouldBeFalse(body.ToString());
        return body;
    }

    public async Task<JsonElement> ErrorAsync(string tool, object? arguments = null)
    {
        var (body, isError) = await CallAsync(tool, arguments);
        isError.ShouldBeTrue(body.ToString());
        return body;
    }

    public async ValueTask DisposeAsync() => await Client.DisposeAsync();
}

/// <summary>
/// O Espaço "Lia" (dados fictícios) usado pelos testes do servidor MCP, semeado pela API REST do app: carteiras Azul e Caixa,
/// cartão Verde (fecha dia 18, vence dia 25), tags Bruna (acerto) e Lazer, lançamentos, uma transferência, compras e um pagamento.
/// "Hoje" é 2026-03-20.
/// </summary>
internal sealed record LiaSpace(HttpClient Http, Guid Azul, Guid Caixa, Guid Verde, Guid Bruna, Guid Lazer, Guid PendingEntry);

public abstract class McpTestBase(PostgresFixture postgres) : ApiTestBase(postgres)
{
    protected const string AppUrl = "http://finmy.exemplo.com";

    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?>
    {
        ["APP_URL"] = AppUrl,
        ["RateLimiting:Auth:PermitLimit"] = "100000",
    };

    protected override IClock? Clock => new FixedTodayClock(new DateOnly(2026, 3, 20));

    /// <summary>Cria a conta, semeia o Espaço pelo REST e conecta o Claude (OAuth) a ele.</summary>
    internal async Task<(LiaSpace Space, McpSession Mcp)> ConnectLiaAsync()
    {
        var (http, account) = await CardTestSupport.NewSpaceAsync(Factory, "Lia");
        var space = await SeedAsync(http);
        var (_, tokens) = await new OAuthFlow(Factory).ConnectAsync(account.AccessToken);
        return (space, await McpSession.ConnectAsync(Factory, tokens.AccessToken));
    }

    private static async Task<JsonElement> PostAsync(HttpClient http, string url, object body)
    {
        var response = await http.PostAsJsonAsync(url, body);
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    private static async Task<Guid> TagAsync(HttpClient http, string name, bool isSettlement) =>
        (await PostAsync(http, "/api/v1/tags", new { name, isSettlement })).GetProperty("id").GetGuid();

    internal static async Task<LiaSpace> SeedAsync(HttpClient http)
    {
        var azul = await CardTestSupport.CreateWalletAsync(http, "Azul");
        var caixa = await CardTestSupport.CreateWalletAsync(http, "Caixa");
        var velha = await CardTestSupport.CreateWalletAsync(http, "Velha");
        (await http.PostAsync($"/api/v1/wallets/{velha.Id}/archive", null)).EnsureSuccessStatusCode();
        var verde = await CardTestSupport.CreateCardAsync(http, "Verde", azul.Id);
        var bruna = await TagAsync(http, "Bruna", isSettlement: true);
        var lazer = await TagAsync(http, "Lazer", isSettlement: false);
        var antiga = await TagAsync(http, "Antiga", isSettlement: false);
        (await http.PostAsync($"/api/v1/tags/{antiga}/archive", null)).EnsureSuccessStatusCode();

        async Task EntryAsync(string kind, string description, string amount, string date, string status, string referenceMonth, params Guid[] tagIds) =>
            await PostAsync(http, "/api/v1/entries", new { kind, description, amount, date, walletId = azul.Id, status, referenceMonth, tagIds });

        await EntryAsync("income", "Salário", "5000,00", "2026-03-05", "settled", "2026-03");
        await EntryAsync("expense", "Aluguel", "1500,00", "2026-03-10", "settled", "2026-03");
        await EntryAsync("expense", "Mercado", "320,50", "2026-03-18", "planned", "2026-03", lazer);
        await EntryAsync("expense", "Presente Bruna", "40,00", "2026-03-08", "settled", "2026-03", bruna);
        await EntryAsync("income", "Bruna Pix", "30,00", "2026-03-09", "settled", "2026-03", bruna);
        await EntryAsync("expense", "Luz", "120,00", "2026-02-10", "planned", "2026-02");
        var pending = (await http.GetFromJsonAsync<JsonElement>("/api/v1/entries/pending")).EnumerateArray().Single().GetProperty("id").GetGuid();

        await PostAsync(http, "/api/v1/transfers", new { fromWalletId = azul.Id, toWalletId = caixa.Id, amount = "200,00", date = "2026-03-12", description = "Reserva" });

        async Task PurchaseAsync(string description, string amount, string date, string nature, params Guid[] tagIds) =>
            await PostAsync(http, "/api/v1/card-purchases", new { description, amount, date, nature, creditCardId = verde.Id, tagIds });

        await PurchaseAsync("Ifood", "156,81", "2026-03-05", "debit", bruna);
        await PurchaseAsync("Uber", "12,72", "2026-03-06", "debit", bruna);
        await PurchaseAsync("Estorno", "9,53", "2026-03-07", "credit", bruna);
        await PurchaseAsync("Cinema", "60,00", "2026-03-08", "debit", lazer);
        await PostAsync(http, $"/api/v1/credit-cards/{verde.Id}/invoices/2026-03/payments", new { amount = "50,00", date = "2026-03-19" });

        return new LiaSpace(http, azul.Id, caixa.Id, verde.Id, bruna, lazer, pending);
    }
}
