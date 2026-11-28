using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Api.Tests.Support;
using FinMy.Application.Tests.Infrastructure;
using Shouldly;

namespace FinMy.Api.Tests.Cards;

/// <summary>Banco (código COMPE) da carteira e do cartão: guardado, devolvido, trocado e apagado; código inválido é 422.</summary>
public class BankTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private static object Wallet(string? bank, long? version = null) =>
        new { name = "Inter", type = "checking", color = "#FF7A00", initialBalance = "0", initialBalanceDate = "2026-01-01", bank, version };

    [Fact]
    public async Task Carteira_guarda_troca_e_apaga_o_banco()
    {
        var (http, _) = await CardTestSupport.NewSpaceAsync(Factory);

        var created = await http.PostAsJsonAsync("/api/v1/wallets", Wallet("077"));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var wallet = await created.Content.ReadFromJsonAsync<JsonElement>();
        wallet.GetProperty("bank").GetString().ShouldBe("077");
        var id = wallet.GetProperty("id").GetGuid();

        var changed = await http.PutAsJsonAsync($"/api/v1/wallets/{id}", Wallet("748", wallet.GetProperty("version").GetInt64()));
        changed.StatusCode.ShouldBe(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());
        var again = await changed.Content.ReadFromJsonAsync<JsonElement>();
        again.GetProperty("bank").GetString().ShouldBe("748");

        var cleared = await http.PutAsJsonAsync($"/api/v1/wallets/{id}", Wallet(null, again.GetProperty("version").GetInt64()));
        (await cleared.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("bank").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Cartao_guarda_o_banco_e_codigo_invalido_e_422()
    {
        var (http, _) = await CardTestSupport.NewSpaceAsync(Factory);
        var wallet = await CardTestSupport.CreateWalletAsync(http, "Azul");

        var created = await http.PostAsJsonAsync("/api/v1/credit-cards", new { name = "PicPay", closingDay = 3, dueDay = 10, paymentWalletId = wallet.Id, color = "#11C76F", bank = "380" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var card = await created.Content.ReadFromJsonAsync<JsonElement>();
        card.GetProperty("bank").GetString().ShouldBe("380");
        (await http.GetFromJsonAsync<JsonElement>($"/api/v1/credit-cards/{card.GetProperty("id").GetGuid()}")).GetProperty("bank").GetString().ShouldBe("380");

        var invalid = await http.PostAsJsonAsync("/api/v1/credit-cards", new { name = "X", closingDay = 3, dueDay = 10, paymentWalletId = wallet.Id, color = "#11C76F", bank = "38a" });
        ((int)invalid.StatusCode).ShouldBe(422);
        ((int)(await http.PostAsJsonAsync("/api/v1/wallets", Wallet("1234"))).StatusCode).ShouldBe(422);
    }
}
