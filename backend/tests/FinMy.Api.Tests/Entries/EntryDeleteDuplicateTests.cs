using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FinMy.Api.Idempotency;
using FinMy.Api.Tests.Support;
using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;

namespace FinMy.Api.Tests.Entries;

/// <summary><c>DELETE /entries/{id}</c> (US6.3) e <c>POST /entries/{id}/duplicate</c> (US6.4). "Hoje" é 10/03/2026.</summary>
public class EntryDeleteDuplicateTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private static readonly DateOnly Today = new(2026, 3, 10);

    protected override IClock? Clock => new FixedTodayClock(Today);

    private sealed record Setup(HttpClient Http, TestAccount Account, Guid Azul);

    private async Task<Setup> NewUserAsync(string name = "Lia")
    {
        using var anonymous = Factory.CreateClient();
        var account = await anonymous.SignUpAsync(name: name);
        var http = Factory.CreateClient().WithBearer(account.AccessToken);
        return new Setup(http, account, await Factory.SeedWalletAsync(account, "Azul"));
    }

    private static async Task<JsonElement> CreateAsync(Setup s, string description = "Tim", string date = "2026-03-10", string kind = "expense", string? notes = null)
    {
        var response = await s.Http.PostAsJsonAsync("/api/v1/entries", new { kind, description, amount = "44,99", date, walletId = s.Azul, notes });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Relê pelo GET: é a forma que o banco guarda (o POST devolve createdAt com mais casas que o PostgreSQL).
        var id = (await response.JsonAsync()).GetProperty("id").GetGuid();
        return await (await s.Http.GetAsync($"/api/v1/entries/{id}")).JsonAsync();
    }

    private static Task<HttpResponseMessage> DuplicateAsync(HttpClient http, Guid id, object body, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/entries/{id}/duplicate")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation(IdempotencyFilter.HeaderName, key);
        }

        return http.SendAsync(request);
    }

    // ---- DELETE ----

    [Fact]
    public async Task Excluir_devolve_o_lote_e_o_lancamento_some()
    {
        var s = await NewUserAsync();
        var id = (await CreateAsync(s)).GetProperty("id").GetGuid();

        var response = await s.Http.DeleteAsync($"/api/v1/entries/{id}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.JsonAsync()).GetProperty("deletionBatchId").GetGuid().ShouldNotBe(Guid.Empty);
        (await s.Http.GetAsync($"/api/v1/entries/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await s.Http.DeleteAsync($"/api/v1/entries/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Desfazer_a_exclusao_restaura_o_lancamento_identico_US6_3()
    {
        var s = await NewUserAsync();
        var created = await CreateAsync(s, notes: "Plano da Duda");
        var id = created.GetProperty("id").GetGuid();
        var batch = (await (await s.Http.DeleteAsync($"/api/v1/entries/{id}")).JsonAsync()).GetProperty("deletionBatchId").GetGuid();

        var restore = await s.Http.PostAsync($"/api/v1/deletions/{batch}/restore", content: null);

        restore.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var back = await s.Http.GetAsync($"/api/v1/entries/{id}");
        back.StatusCode.ShouldBe(HttpStatusCode.OK);
        var restored = await back.JsonAsync();
        foreach (var field in new[] { "id", "kind", "description", "amountCents", "date", "referenceMonth", "status", "walletId", "notes", "createdAt" })
        {
            restored.GetProperty(field).GetRawText().ShouldBe(created.GetProperty(field).GetRawText(), field);
        }
    }

    [Fact]
    public async Task Excluir_lancamento_de_outro_Espaco_ou_inexistente_da_404_e_nao_apaga()
    {
        var lia = await NewUserAsync("Lia");
        var leo = await NewUserAsync("Léo");
        var id = (await CreateAsync(lia)).GetProperty("id").GetGuid();

        (await leo.Http.DeleteAsync($"/api/v1/entries/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await lia.Http.DeleteAsync($"/api/v1/entries/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await lia.Http.GetAsync($"/api/v1/entries/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Sem_login_excluir_e_duplicar_dao_401()
    {
        var id = Guid.NewGuid();
        using var anonymous = Factory.CreateClient();

        (await anonymous.DeleteAsync($"/api/v1/entries/{id}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await DuplicateAsync(anonymous, id, new { date = "2026-04-10" })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---- DUPLICATE ----

    [Fact]
    public async Task Duplicar_cria_igual_com_a_nova_data_mes_e_situacao_recalculados_US6_4()
    {
        var s = await NewUserAsync();
        var original = await CreateAsync(s, "Tim", "2026-03-10", notes: "Plano da Duda");
        var originalId = original.GetProperty("id").GetGuid();

        var response = await DuplicateAsync(s.Http, originalId, new { date = "2026-04-10" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var copy = await response.JsonAsync();
        copy.GetProperty("id").GetGuid().ShouldNotBe(originalId);
        response.Headers.Location!.ToString().ShouldEndWith($"/api/v1/entries/{copy.GetProperty("id").GetGuid()}");
        copy.GetProperty("date").GetString().ShouldBe("2026-04-10");
        copy.GetProperty("referenceMonth").GetString().ShouldBe("2026-04");
        copy.GetProperty("status").GetString().ShouldBe("planned"); // data futura
        foreach (var field in new[] { "kind", "description", "amountCents", "walletId", "notes" })
        {
            copy.GetProperty(field).GetRawText().ShouldBe(original.GetProperty(field).GetRawText(), field);
        }

        // o original continua como estava
        (await (await s.Http.GetAsync($"/api/v1/entries/{originalId}")).JsonAsync()).GetRawText().ShouldBe(original.GetRawText());
    }

    [Fact]
    public async Task Duplicar_para_data_passada_nasce_recebida_ou_paga_mesmo_se_o_original_estava_prevista()
    {
        var s = await NewUserAsync();
        var planned = await CreateAsync(s, "Tim", "2026-05-10");
        planned.GetProperty("status").GetString().ShouldBe("planned");

        var copy = await (await DuplicateAsync(s.Http, planned.GetProperty("id").GetGuid(), new { date = "2026-02-01" })).JsonAsync();

        copy.GetProperty("status").GetString().ShouldBe("settled");
        copy.GetProperty("referenceMonth").GetString().ShouldBe("2026-02");
    }

    [Fact]
    public async Task Duplicar_ignora_o_mes_de_referencia_ajustado_do_original()
    {
        var s = await NewUserAsync();
        var original = await CreateAsync(s, "Tim", "2026-02-25");
        var id = original.GetProperty("id").GetGuid();
        var put = await s.Http.PutAsJsonAsync($"/api/v1/entries/{id}", new
        {
            kind = "expense",
            description = "Tim",
            amount = "44,99",
            date = "2026-02-25",
            walletId = s.Azul,
            status = "settled",
            referenceMonth = "2026-03",
            version = original.GetProperty("version").GetInt64(),
        });
        put.StatusCode.ShouldBe(HttpStatusCode.OK);

        var copy = await (await DuplicateAsync(s.Http, id, new { date = "2026-06-02" })).JsonAsync();

        copy.GetProperty("referenceMonth").GetString().ShouldBe("2026-06");
    }

    [Fact]
    public async Task Duplicar_com_a_mesma_Idempotency_Key_nao_cria_outra_copia()
    {
        var s = await NewUserAsync();
        var id = (await CreateAsync(s)).GetProperty("id").GetGuid();
        var key = Guid.NewGuid().ToString();

        var first = await DuplicateAsync(s.Http, id, new { date = "2026-04-10" }, key);
        var second = await DuplicateAsync(s.Http, id, new { date = "2026-04-10" }, key);
        var other = await DuplicateAsync(s.Http, id, new { date = "2026-05-10" }, key);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await second.JsonAsync()).GetProperty("id").GetGuid().ShouldBe((await first.JsonAsync()).GetProperty("id").GetGuid());
        other.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await other.CodeAsync()).ShouldBe("idempotency.key_reused");

        var all = await (await s.Http.GetAsync("/api/v1/entries/search")).JsonAsync();
        all.GetProperty("totalCount").GetInt32().ShouldBe(2); // original + uma cópia
    }

    [Fact]
    public async Task Duplicar_sem_data_da_422_e_original_inexistente_ou_de_outro_Espaco_da_404()
    {
        var lia = await NewUserAsync("Lia");
        var leo = await NewUserAsync("Léo");
        var id = (await CreateAsync(lia)).GetProperty("id").GetGuid();

        var noDate = await DuplicateAsync(lia.Http, id, new { });
        var foreign = await DuplicateAsync(leo.Http, id, new { date = "2026-04-10" });
        var missing = await DuplicateAsync(lia.Http, Guid.NewGuid(), new { date = "2026-04-10" });

        noDate.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await noDate.CodeAsync()).ShouldBe("date.required");
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Duplicar_com_data_fora_do_intervalo_da_422()
    {
        var s = await NewUserAsync();
        var id = (await CreateAsync(s)).GetProperty("id").GetGuid();

        var response = await DuplicateAsync(s.Http, id, new { date = "1999-12-31" });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Duplicar_com_a_carteira_original_arquivada_da_422_wallet_archived()
    {
        var s = await NewUserAsync();
        var id = (await CreateAsync(s)).GetProperty("id").GetGuid();
        await Factory.ArchiveWalletAsync(s.Account, s.Azul);

        var response = await DuplicateAsync(s.Http, id, new { date = "2026-04-10" });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.CodeAsync()).ShouldBe("wallet.archived");
    }
}
