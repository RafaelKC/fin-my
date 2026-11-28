using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Api.Tests.Cards;
using FinMy.Api.Tests.OAuth;
using FinMy.Api.Tests.Support;
using FinMy.Application.Tests.Infrastructure;
using Shouldly;

namespace FinMy.Api.Tests.Widget;

/// <summary>Sugestões de lançamento lidas das notificações de banco (010): celular envia, app lista, lança ou descarta.</summary>
public class SuggestionTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private OAuthFlow Flow => field ??= new OAuthFlow(Factory);

    private async Task<HttpResponseMessage> SubmitAsync(string widget, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/widget/suggestions") { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", widget);
        return await Flow.Raw.SendAsync(request);
    }

    private static object Notificacao(string key, string bank = "380", string kind = "card_purchase") => new
    {
        source = "PicPay",
        bank,
        kind,
        description = "Padaria X",
        amountCents = 4590,
        occurredAt = "2026-03-20T14:05:00Z",
        cardLast4 = "1234",
        rawText = "Compra de R$ 45,90 em PADARIA X",
        dedupeKey = key,
    };

    private async Task<(HttpClient App, string Widget, Guid CardId)> SetupAsync()
    {
        var (app, account) = await CardTestSupport.NewSpaceAsync(Factory, "Lia");
        var wallet = await CardTestSupport.CreateWalletAsync(app, "Azul");
        var card = await app.PostAsJsonAsync("/api/v1/credit-cards", new { name = "PicPay", closingDay = 3, dueDay = 10, paymentWalletId = wallet.Id, color = "#11C76F", bank = "380" });
        card.StatusCode.ShouldBe(HttpStatusCode.Created);
        var cardId = (await card.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var widget = (await Flow.ConnectAndroidAsync(account.AccessToken)).AccessToken;
        return (app, widget, cardId);
    }

    [Fact]
    public async Task Celular_envia_e_app_lista_com_o_cartao_do_mesmo_banco_sem_repetir_a_notificacao()
    {
        var (app, widget, cardId) = await SetupAsync();

        var first = await SubmitAsync(widget, Notificacao("k1"));
        first.StatusCode.ShouldBe(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());
        (await SubmitAsync(widget, Notificacao("k1"))).StatusCode.ShouldBe(HttpStatusCode.OK); // mesma notificação

        var list = (await app.GetFromJsonAsync<JsonElement>("/api/v1/suggestions")).EnumerateArray().ToList();
        list.Count.ShouldBe(1);
        var s = list[0];
        s.GetProperty("description").GetString().ShouldBe("Padaria X");
        s.GetProperty("amountText").GetString().ShouldBe("R$ 45,90");
        s.GetProperty("kind").GetString().ShouldBe("card_purchase");
        s.GetProperty("cardLast4").GetString().ShouldBe("1234");
        s.GetProperty("suggestedCreditCardId").GetGuid().ShouldBe(cardId);
    }

    [Fact]
    public async Task Lancada_e_descartada_saem_da_lista()
    {
        var (app, widget, _) = await SetupAsync();
        var a = (await (await SubmitAsync(widget, Notificacao("a"))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var b = (await (await SubmitAsync(widget, Notificacao("b"))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        (await app.PostAsJsonAsync($"/api/v1/suggestions/{a}/added", new { linkedId = Guid.NewGuid() })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await app.PostAsync($"/api/v1/suggestions/{b}/dismiss", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await app.GetFromJsonAsync<JsonElement>("/api/v1/suggestions")).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Dados_invalidos_sao_422_e_outro_espaco_nao_ve_nem_resolve()
    {
        var (_, widget, _) = await SetupAsync();
        ((int)(await SubmitAsync(widget, new { kind = "x", amountCents = 10, dedupeKey = "z" })).StatusCode).ShouldBe(422);
        ((int)(await SubmitAsync(widget, new { kind = "expense", amountCents = 0, dedupeKey = "z" })).StatusCode).ShouldBe(422);
        var id = (await (await SubmitAsync(widget, Notificacao("c", kind: "expense"))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var (bruna, _) = await CardTestSupport.NewSpaceAsync(Factory, "Bruna");
        (await bruna.GetFromJsonAsync<JsonElement>("/api/v1/suggestions")).GetArrayLength().ShouldBe(0);
        (await bruna.PostAsync($"/api/v1/suggestions/{id}/dismiss", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await bruna.GetAsync($"/api/v1/suggestions/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
