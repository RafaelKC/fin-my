using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FinMy.Api.Tests.Support;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FinMy.Api.Tests.Isolation;

/// <summary>
/// docs/arquitetura.md §3.7 (parte b): para <b>cada operação do OpenAPI gerado pela API</b>, chama como B depois de A ter
/// criado dados e verifica que (1) nada de A aparece na resposta, (2) id de A em rota responde 404 <c>not_found</c> e
/// id de A em corpo/consulta responde 404/422, e (3) nada de A foi gravado. Operações novas entram sozinhas.
/// </summary>
public class ApiIsolationTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private const string OpenApiPath = "/openapi/v1.json";

    // Uma conta nova de B por operação (cada uma abre sessão): sem o limite por IP de 20 req/min.
    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?> { ["RateLimiting:Auth:PermitLimit"] = "100000" };

    private sealed record Caller(HttpClient Http, string AccessToken, string RefreshToken, string Email, string Password);

    private sealed record ContaDeA(TestAccount Account, string SecondAccessToken, Guid SessionId, IReadOnlyList<string> Secrets, IReadOnlyList<Guid> Ids, Guid WalletA, Guid WalletA2, Guid CardA);

    private async Task<string> OpenApiJsonAsync()
    {
        var response = await Client.GetAsync(OpenApiPath);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private HttpClient NewHttp() => Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private async Task<Caller> NewCallerAsync(string name)
    {
        var http = NewHttp();
        var email = SampleContext.NewEmail();
        var response = await http.PostAsJsonAsync("/api/v1/auth/signup", new { name, email, password = AccountHelpers.DefaultPassword });
        response.EnsureSuccessStatusCode();
        var session = (await response.Content.ReadFromJsonAsync<SessionResponse>(JsonDefaults.Options))!;
        var cookie = response.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("finmy_rt=", StringComparison.Ordinal)).Split(';')[0]["finmy_rt=".Length..];
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return new Caller(http, session.AccessToken, cookie, email, AccountHelpers.DefaultPassword);
    }

    /// <summary>A cria dados reconhecíveis: nome, nome do Espaço e uma segunda sessão (outro aparelho).</summary>
    private async Task<ContaDeA> CreateAAsync()
    {
        var a = await Client.SignUpAsync(name: "Alice Isolada Segredo", email: $"alice.segredo.{Guid.NewGuid():N}@exemplo.com");
        using var asA = NewHttp().WithBearer(a.AccessToken);
        (await asA.PatchAsJsonAsync("/api/v1/tenant", new { name = "Espaço Secreto da Alice" })).EnsureSuccessStatusCode();

        var second = await Client.LoginAsync(a.Email);
        var sessionId = Guid.Parse(JwtClaims.Get(a.AccessToken, "sid")!);

        // Dados de negócio de A (carteiras, lançamentos, transferência, exclusão desfazível): ids de A nas rotas
        // novas batem em linhas reais, não em ids inexistentes.
        var azul = await CreateJsonAsync(asA, "/api/v1/wallets", new { name = "Azul Segredo da Alice", type = "checking", color = "#1E88E5", initialBalance = "500", initialBalanceDate = "2026-03-01" });
        var verde = await CreateJsonAsync(asA, "/api/v1/wallets", new { name = "Verde Segredo da Alice", type = "savings", color = "#43A047", initialBalance = "0", initialBalanceDate = "2026-03-01" });
        var azulId = azul.GetProperty("id").GetGuid();
        var verdeId = verde.GetProperty("id").GetGuid();
        var entry = await CreateJsonAsync(asA, "/api/v1/entries", new { kind = "expense", description = "Tim Segredo da Alice", amount = "44,99", date = "2026-03-10", walletId = azulId, notes = "Nota Secreta da Alice" });
        var outra = await CreateJsonAsync(asA, "/api/v1/entries", new { kind = "income", description = "Salário Segredo da Alice", amount = "1000", date = "2026-03-05", walletId = azulId });
        var transfer = await CreateJsonAsync(asA, "/api/v1/transfers", new { fromWalletId = azulId, toWalletId = verdeId, amount = "20,00", date = "2026-03-12", description = "Reserva Segredo da Alice" });
        using var bulk = await asA.PostAsJsonAsync("/api/v1/entries/bulk", new { ids = new[] { outra.GetProperty("id").GetGuid() }, action = "delete" });
        bulk.EnsureSuccessStatusCode();
        var batchId = (await bulk.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("deletionBatchId").GetGuid();

        // Cartão de A com compra parcelada (3x) e um pagamento de fatura: ids de cartão, compra, plano e pagamento de A
        // em rotas novas batem em linhas reais (003).
        var cartao = await CreateJsonAsync(asA, "/api/v1/credit-cards", new { name = "Cartão Segredo da Alice", closingDay = 18, dueDay = 25, paymentWalletId = azulId, color = "#1E88E5", limit = "3000" });
        var cartaoId = cartao.GetProperty("id").GetGuid();
        var compras = await CreateJsonAsync(asA, "/api/v1/card-purchases", new { description = "Casa Segredo da Alice", amount = "100", date = "2026-03-10", creditCardId = cartaoId, notes = "Nota do Cartão da Alice", installments = new { total = 3, mode = "total_amount" } });
        var compra = compras.GetProperty("purchases")[0];
        var compraId = compra.GetProperty("id").GetGuid();
        var planoId = compra.GetProperty("installmentPlanId").GetGuid();
        var fatura = await CreateJsonAsync(asA, $"/api/v1/credit-cards/{cartaoId}/invoices/2026-03/payments", new { amount = "10,00", date = "2026-03-25", walletId = azulId });
        var pagamentoId = fatura.GetProperty("payments")[0].GetProperty("id").GetGuid();

        var secrets = new[]
        {
            a.Email, "Alice Isolada Segredo", "Espaço Secreto da Alice", a.AccessToken, second.AccessToken,
            "Azul Segredo da Alice", "Verde Segredo da Alice", "Tim Segredo da Alice", "Nota Secreta da Alice", "Salário Segredo da Alice", "Reserva Segredo da Alice",
            "Cartão Segredo da Alice", "Casa Segredo da Alice", "Nota do Cartão da Alice",
        };
        var ids = new[]
        {
            a.Session.User.Id, a.Session.Tenant.Id, sessionId, Guid.Parse(JwtClaims.Get(second.AccessToken, "sid")!),
            azulId, verdeId, entry.GetProperty("id").GetGuid(), outra.GetProperty("id").GetGuid(), transfer.GetProperty("id").GetGuid(), batchId,
            cartaoId, compraId, planoId, pagamentoId,
        };
        return new ContaDeA(a, second.AccessToken, sessionId, secrets, ids, azulId, verdeId, cartaoId);
    }

    private static async Task<JsonElement> CreateJsonAsync(HttpClient http, string path, object body)
    {
        using var response = await http.PostAsJsonAsync(path, body);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<string> SnapshotOfAAsync(ContaDeA a)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await AccountSnapshot.TakeAsync(scope.ServiceProvider.GetRequiredService<FinMyDbContext>(), a.Account.Session.User.Id, a.Account.Session.Tenant.Id);
    }

    [Fact]
    public async Task O_documento_OpenAPI_gerado_lista_as_operacoes_da_feature()
    {
        var operations = OpenApiOperations.Read(await OpenApiJsonAsync());

        string[] esperadas =
        [
            "POST /api/v1/auth/signup",
            "POST /api/v1/auth/login",
            "POST /api/v1/auth/refresh",
            "POST /api/v1/auth/logout",
            "POST /api/v1/auth/password-reset/request",
            "POST /api/v1/auth/password-reset/confirm",
            "PATCH /api/v1/me",
            "POST /api/v1/me/password",
            "PATCH /api/v1/tenant",
            "POST /api/v1/deletions/{batchId}/restore",
            "GET /api/v1/wallets",
            "POST /api/v1/wallets",
            "GET /api/v1/wallets/{id}",
            "PUT /api/v1/wallets/{id}",
            "DELETE /api/v1/wallets/{id}",
            "POST /api/v1/wallets/{id}/make-primary",
            "POST /api/v1/wallets/{id}/archive",
            "POST /api/v1/wallets/{id}/unarchive",
            "GET /api/v1/credit-cards",
            "POST /api/v1/credit-cards",
            "GET /api/v1/credit-cards/{id}",
            "PUT /api/v1/credit-cards/{id}",
            "DELETE /api/v1/credit-cards/{id}",
            "POST /api/v1/credit-cards/{id}/archive",
            "POST /api/v1/credit-cards/{id}/unarchive",
            "GET /api/v1/credit-cards/{id}/invoice-for",
            "GET /api/v1/credit-cards/{id}/invoices/{month}",
            "POST /api/v1/credit-cards/{id}/invoices/{month}/payments",
            "DELETE /api/v1/credit-cards/{id}/invoices/{month}/payments",
            "POST /api/v1/credit-cards/{id}/invoices/{month}/carry-over",
            "PUT /api/v1/invoice-payments/{id}",
            "DELETE /api/v1/invoice-payments/{id}",
            "POST /api/v1/card-purchases",
            "GET /api/v1/card-purchases/{id}",
            "PUT /api/v1/card-purchases/{id}",
            "DELETE /api/v1/card-purchases/{id}",
            "PATCH /api/v1/card-purchases/{id}/reconciled",
            "POST /api/v1/card-purchases/{id}/move",
            "PUT /api/v1/installment-plans/{id}",
            "POST /api/v1/transfers",
            "GET /api/v1/transfers/{id}",
            "PUT /api/v1/transfers/{id}",
            "DELETE /api/v1/transfers/{id}",
            "POST /api/v1/entries",
            "GET /api/v1/entries/suggestions",
            "GET /api/v1/entries/pending",
            "POST /api/v1/entries/bulk",
            "GET /api/v1/entries/search",
            "GET /api/v1/entries/{id}",
            "PUT /api/v1/entries/{id}",
            "PATCH /api/v1/entries/{id}/status",
            "DELETE /api/v1/entries/{id}",
            "POST /api/v1/entries/{id}/duplicate",
            "GET /api/v1/months/{month}",
        ];
        esperadas.ShouldBeSubsetOf(operations.Select(o => o.Key));
    }

    [Fact]
    public async Task Toda_operacao_chamada_como_B_nao_expoe_nem_grava_dados_de_A()
    {
        var a = await CreateAAsync();
        var doc = JsonNode.Parse(await OpenApiJsonAsync())!.AsObject();
        var operations = OpenApiOperations.Read(doc.ToJsonString());
        var before = await SnapshotOfAAsync(a);

        var falhas = new List<string>();
        var statuses = new Dictionary<string, int>();

        foreach (var operation in operations)
        {
            // Variante base (ids inexistentes) e uma por id de A, quando a operação aceita um uuid.
            var variants = new List<Guid?> { null };
            if (AcceptsGuid(operation))
            {
                variants.AddRange(a.Ids.Cast<Guid?>());
            }

            foreach (var idOfA in variants)
            {
                var caller = await NewCallerAsync("Beto Isolado");
                var (status, text, code, injected) = await SendAsync(doc, operation, caller, idOfA, a);
                var label = $"{operation.Key} (id de A: {(idOfA is null ? "não" : idOfA.ToString())})";

                if (idOfA is null)
                {
                    statuses[operation.Key] = status;
                }

                if (status >= 500)
                {
                    falhas.Add($"{label}: respondeu {status}.");
                }

                foreach (var secret in a.Secrets.Concat(a.Ids.Where(id => id != idOfA).Select(id => id.ToString())))
                {
                    if (text.Contains(secret, StringComparison.OrdinalIgnoreCase))
                    {
                        falhas.Add($"{label}: a resposta expôs dado de A ({Truncate(secret)}).");
                    }
                }

                if (injected == Where.Path && (status != 404 || !IsNotFoundCode(code)))
                {
                    falhas.Add($"{label}: id de A na rota deveria dar 404 not_found (ou <recurso>.not_found), deu {status} ({code}).");
                }
                else if (injected == Where.Other && status is not (404 or 422))
                {
                    falhas.Add($"{label}: id de A em corpo/consulta deveria dar 404 ou 422, deu {status}.");
                }

                if (await SnapshotOfAAsync(a) != before)
                {
                    falhas.Add($"{label}: os dados de A foram alterados.");
                    before = await SnapshotOfAAsync(a);
                }
            }
        }

        falhas.ShouldBeEmpty(string.Join(Environment.NewLine, falhas));

        // Controle: B autenticado opera de fato no próprio Espaço (a varredura não passou só por falhar).
        statuses["PATCH /api/v1/tenant"].ShouldBe(200);
        statuses["PATCH /api/v1/me"].ShouldBe(200);
    }

    [Fact]
    public async Task Id_de_carteira_de_A_em_corpo_ou_consulta_de_B_nao_vaza_nem_grava()
    {
        var a = await CreateAAsync();
        var before = await SnapshotOfAAsync(a);
        var b = await NewCallerAsync("Beto Isolado");
        var own = await CreateJsonAsync(b.Http, "/api/v1/wallets", new { name = "Azul do Beto", type = "checking", color = "#1E88E5", initialBalance = "0", initialBalanceDate = "2026-03-01" });
        var ownId = own.GetProperty("id").GetGuid();
        var ownCard = await CreateJsonAsync(b.Http, "/api/v1/credit-cards", new { name = "Cartão do Beto", closingDay = 18, dueDay = 25, paymentWalletId = ownId, color = "#1E88E5" });
        var ownCardId = ownCard.GetProperty("id").GetGuid();
        await CreateJsonAsync(b.Http, "/api/v1/card-purchases", new { description = "Uber do Beto", amount = "12,95", date = "2026-03-10", creditCardId = ownCardId });

        var respostas = new Dictionary<string, HttpResponseMessage>
        {
            ["POST /entries"] = await b.Http.PostAsJsonAsync("/api/v1/entries", new { kind = "expense", description = "Tim", amount = "1", date = "2026-03-10", walletId = a.WalletA }),
            ["POST /transfers (origem de A)"] = await b.Http.PostAsJsonAsync("/api/v1/transfers", new { fromWalletId = a.WalletA, toWalletId = ownId, amount = "1", date = "2026-03-10" }),
            ["POST /transfers (destino de A)"] = await b.Http.PostAsJsonAsync("/api/v1/transfers", new { fromWalletId = ownId, toWalletId = a.WalletA2, amount = "1", date = "2026-03-10" }),
            ["POST /card-purchases (cartão de A)"] = await b.Http.PostAsJsonAsync("/api/v1/card-purchases", new { description = "Uber", amount = "12,95", date = "2026-03-10", creditCardId = a.CardA }),
            ["POST /credit-cards (carteira de A)"] = await b.Http.PostAsJsonAsync("/api/v1/credit-cards", new { name = "Cartão do Beto", closingDay = 18, dueDay = 25, paymentWalletId = a.WalletA, color = "#1E88E5" }),
            ["POST /invoices/payments (carteira de A)"] = await b.Http.PostAsJsonAsync($"/api/v1/credit-cards/{ownCardId}/invoices/2026-03/payments", new { amount = "1", date = "2026-03-25", walletId = a.WalletA }),
            ["GET /months (filtro)"] = await b.Http.GetAsync($"/api/v1/months/2026-03?walletIds={a.WalletA}"),
        };

        foreach (var (label, response) in respostas)
        {
            using (response)
            {
                // O filtro do mês (contracts: só 200/422) trata carteira desconhecida como filtro vazio: 200 sem dado de A.
                var aceitos = label.StartsWith("GET /months", StringComparison.Ordinal) ? new[] { 200 } : new[] { 404, 422 };
                ((int)response.StatusCode).ShouldBeOneOf(aceitos, $"{label}: status inesperado {(int)response.StatusCode}.");
                var text = await response.Content.ReadAsStringAsync();
                foreach (var secret in a.Secrets)
                {
                    text.ShouldNotContain(secret, Case.Insensitive, $"{label} expôs dado de A.");
                }
            }
        }

        (await SnapshotOfAAsync(a)).ShouldBe(before);
        b.Http.Dispose();
    }

    // Erros de negócio de recurso usam o prefixo do recurso (ex.: deletion.not_found, contracts/openapi.yaml).
    private static bool IsNotFoundCode(string? code) =>
        code is not null && (code == "not_found" || code.EndsWith(".not_found", StringComparison.Ordinal));

    private enum Where { None, Path, Other }

    private static bool AcceptsGuid(OpenApiOperation operation) =>
        operation.Parameters.Any(p => IsUuid(p.Schema)) || (operation.BodySchema?.ToJsonString().Contains("uuid", StringComparison.Ordinal) ?? false);

    private static bool IsUuid(JsonNode? schema) => schema?["format"]?.GetValue<string>() == "uuid";

    private static async Task<(int Status, string Text, string? Code, Where Injected)> SendAsync(
        JsonObject doc, OpenApiOperation operation, Caller b, Guid? idOfA, ContaDeA a)
    {
        var id = idOfA ?? Guid.NewGuid();
        var injected = Where.None;

        var path = operation.Path;
        var query = new List<string>();
        var cookies = new List<string>();
        foreach (var parameter in operation.Parameters)
        {
            var value = IsUuid(parameter.Schema) ? id.ToString() : "x";
            switch (parameter.In)
            {
                case "path":
                    path = path.Replace("{" + parameter.Name + "}", value, StringComparison.Ordinal);
                    injected = idOfA is not null && IsUuid(parameter.Schema) ? Where.Path : injected;
                    break;
                case "query" when parameter.Required || IsUuid(parameter.Schema):
                    query.Add($"{Uri.EscapeDataString(parameter.Name)}={Uri.EscapeDataString(value)}");
                    injected = idOfA is not null && IsUuid(parameter.Schema) && injected == Where.None ? Where.Other : injected;
                    break;
                case "cookie" when parameter.Name == "finmy_rt":
                    cookies.Add($"finmy_rt={b.RefreshToken}"); // o cookie é o de B: a sessão é dele
                    break;
            }
        }

        using var message = new HttpRequestMessage(new HttpMethod(operation.Method), query.Count == 0 ? path : $"{path}?{string.Join('&', query)}");
        if (cookies.Count > 0)
        {
            message.Headers.Add("Cookie", string.Join("; ", cookies));
        }

        if (operation.BodySchema is not null)
        {
            var body = OperationSamples.Bodies.TryGetValue(operation.Key, out var sample)
                ? JsonSerializer.Serialize(sample(new SampleContext(b.Email, b.Password)))
                : SchemaSampler.Sample(doc, operation.BodySchema, id)!.ToJsonString();
            message.Content = new StringContent(body, Encoding.UTF8, "application/json");
            if (idOfA is not null && injected == Where.None && body.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                injected = Where.Other;
            }
        }

        using var response = await b.Http.SendAsync(message);
        var text = await response.Content.ReadAsStringAsync();
        string? code = null;
        try
        {
            using var document = JsonDocument.Parse(text);
            // Listas (ex.: GET /wallets) vêm como array JSON, sem code.
            code = document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
        }
        catch (JsonException)
        {
            // Resposta sem corpo JSON (204, texto): sem code.
        }

        b.Http.Dispose();
        return ((int)response.StatusCode, text, code, injected);
    }

    private static string Truncate(string value) => value.Length <= 24 ? value : value[..24] + "…";
}
