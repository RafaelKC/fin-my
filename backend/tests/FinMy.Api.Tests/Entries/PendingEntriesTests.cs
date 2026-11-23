using System.Net;
using System.Net.Http.Json;
using FinMy.Api.Tests.Support;
using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;

namespace FinMy.Api.Tests.Entries;

/// <summary>
/// <c>GET /entries/pending</c> e <c>POST /entries/bulk</c> (US4.1–US4.4, research R9). "Hoje" é 10/03/2026; pendência é
/// Prevista com Mês de referência anterior a março de 2026.
/// </summary>
public class PendingEntriesTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private static readonly DateOnly Today = new(2026, 3, 10);

    protected override IClock? Clock => new FixedTodayClock(Today);

    private async Task<(HttpClient Http, TestAccount Account, Guid Azul)> NewUserAsync(string name = "Lia")
    {
        using var anonymous = Factory.CreateClient();
        var account = await anonymous.SignUpAsync(name: name);
        var http = Factory.CreateClient().WithBearer(account.AccessToken);
        return (http, account, await Factory.SeedWalletAsync(account, "Azul"));
    }

    private static async Task<Guid> CreateAsync(
        HttpClient http, Guid wallet, string kind, string description, string amount, string date, string status, string? referenceMonth = null)
    {
        var response = await http.PostAsJsonAsync(
            "/api/v1/entries",
            new { kind, description, amount, date, walletId = wallet, status, referenceMonth });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> BulkAsync(HttpClient http, IEnumerable<Guid> ids, string action) =>
        http.PostAsJsonAsync("/api/v1/entries/bulk", new { ids, action });

    private static async Task<Guid[]> PendingIdsAsync(HttpClient http)
    {
        var response = await http.GetAsync("/api/v1/entries/pending");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return [.. (await response.JsonAsync()).EnumerateArray().Select(e => e.GetProperty("id").GetGuid())];
    }

    [Fact]
    public async Task Pending_lists_only_planned_entries_of_previous_months_in_rt12_order()
    {
        var (http, _, azul) = await NewUserAsync();
        var carla = await CreateAsync(http, azul, "income", "Carla atrasado", "300,00", "2026-02-20", "planned");
        var luz = await CreateAsync(http, azul, "expense", "Luz", "120,00", "2026-01-15", "planned");
        var tim = await CreateAsync(http, azul, "expense", "Tim", "44,99", "2026-02-20", "planned");
        await CreateAsync(http, azul, "expense", "Aluguel", "900,00", "2026-02-05", "settled");
        await CreateAsync(http, azul, "expense", "Mercado", "80,00", "2026-03-20", "planned");
        await CreateAsync(http, azul, "income", "Bônus", "50,00", "2026-03-05", "planned", "2026-04");

        (await PendingIdsAsync(http)).ShouldBe([luz, carla, tim]);

        var first = (await (await http.GetAsync("/api/v1/entries/pending")).JsonAsync()).EnumerateArray().Last();
        first.GetProperty("description").GetString().ShouldBe("Tim");
        first.GetProperty("amountCents").GetInt64().ShouldBe(4499);
        first.GetProperty("referenceMonth").GetString().ShouldBe("2026-02");
    }

    [Fact]
    public async Task Pending_uses_the_reference_month_not_the_date_and_covers_all_wallets()
    {
        var (http, account, azul) = await NewUserAsync();
        var verde = await Factory.SeedWalletAsync(account, "Verde");
        var atrasadaDeFev = await CreateAsync(http, azul, "income", "Carla atrasado", "300,00", "2026-03-02", "planned", "2026-02");
        var arquivada = await CreateAsync(http, verde, "expense", "Seguro", "70,00", "2026-02-10", "planned");
        await Factory.ArchiveWalletAsync(account, verde);

        (await PendingIdsAsync(http)).ShouldBe([arquivada, atrasadaDeFev]);
    }

    [Fact]
    public async Task Pending_is_scoped_to_the_space()
    {
        var (lia, _, azulLia) = await NewUserAsync("Lia");
        var (leo, _, azulLeo) = await NewUserAsync("Léo");
        var daLia = await CreateAsync(lia, azulLia, "income", "Carla atrasado", "300,00", "2026-02-20", "planned");
        var doLeo = await CreateAsync(leo, azulLeo, "expense", "Luz", "90,00", "2026-02-20", "planned");

        (await PendingIdsAsync(lia)).ShouldBe([daLia]);
        (await BulkAsync(lia, [doLeo], "settle")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PendingIdsAsync(leo)).ShouldBe([doLeo]);
    }

    [Fact]
    public async Task Bulk_move_to_current_month_changes_only_the_reference_month()
    {
        var (http, _, azul) = await NewUserAsync();
        var carla = await CreateAsync(http, azul, "income", "Carla atrasado", "300,00", "2026-02-20", "planned");
        var luz = await CreateAsync(http, azul, "expense", "Luz", "120,00", "2026-01-15", "planned");

        var response = await BulkAsync(http, [carla, luz], "move_to_current_month");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.JsonAsync();
        body.GetProperty("affected").GetInt32().ShouldBe(2);
        body.GetProperty("deletionBatchId").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        (await PendingIdsAsync(http)).ShouldBeEmpty();

        var entry = await (await http.GetAsync($"/api/v1/entries/{carla}")).JsonAsync();
        entry.GetProperty("referenceMonth").GetString().ShouldBe("2026-03");
        entry.GetProperty("date").GetString().ShouldBe("2026-02-20");
        entry.GetProperty("status").GetString().ShouldBe("planned");
    }

    [Fact]
    public async Task Bulk_settle_marks_as_settled_and_keeps_the_reference_month()
    {
        var (http, _, azul) = await NewUserAsync();
        var carla = await CreateAsync(http, azul, "income", "Carla atrasado", "300,00", "2026-02-20", "planned");
        var luz = await CreateAsync(http, azul, "expense", "Luz", "120,00", "2026-02-12", "planned");

        var response = await BulkAsync(http, [carla, luz], "settle");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.JsonAsync()).GetProperty("affected").GetInt32().ShouldBe(2);
        (await PendingIdsAsync(http)).ShouldBeEmpty();

        var entry = await (await http.GetAsync($"/api/v1/entries/{carla}")).JsonAsync();
        entry.GetProperty("status").GetString().ShouldBe("settled");
        entry.GetProperty("referenceMonth").GetString().ShouldBe("2026-02");
    }

    [Fact]
    public async Task Bulk_delete_uses_a_single_batch_that_can_be_undone()
    {
        var (http, _, azul) = await NewUserAsync();
        var carla = await CreateAsync(http, azul, "income", "Carla atrasado", "300,00", "2026-02-20", "planned");
        var luz = await CreateAsync(http, azul, "expense", "Luz", "120,00", "2026-02-12", "planned");

        var response = await BulkAsync(http, [carla, luz, carla], "delete");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.JsonAsync();
        body.GetProperty("affected").GetInt32().ShouldBe(2);
        var batch = body.GetProperty("deletionBatchId").GetGuid();
        (await PendingIdsAsync(http)).ShouldBeEmpty();
        (await http.GetAsync($"/api/v1/entries/{carla}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await http.PostAsync($"/api/v1/deletions/{batch}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await PendingIdsAsync(http)).ShouldBe([luz, carla]);
        (await http.GetAsync($"/api/v1/entries/{carla}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("settle")]
    [InlineData("move_to_current_month")]
    [InlineData("delete")]
    public async Task Bulk_with_an_unknown_id_is_all_or_nothing(string action)
    {
        var (http, _, azul) = await NewUserAsync();
        var carla = await CreateAsync(http, azul, "income", "Carla atrasado", "300,00", "2026-02-20", "planned");

        var response = await BulkAsync(http, [carla, Guid.NewGuid()], action);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.CodeAsync()).ShouldBe("not_found");
        (await PendingIdsAsync(http)).ShouldBe([carla]);
        var entry = await (await http.GetAsync($"/api/v1/entries/{carla}")).JsonAsync();
        entry.GetProperty("status").GetString().ShouldBe("planned");
        entry.GetProperty("referenceMonth").GetString().ShouldBe("2026-02");
    }

    [Fact]
    public async Task Bulk_rejects_invalid_requests()
    {
        var (http, _, azul) = await NewUserAsync();
        var carla = await CreateAsync(http, azul, "income", "Carla atrasado", "300,00", "2026-02-20", "planned");

        var semIds = await BulkAsync(http, [], "settle");
        semIds.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await semIds.CodeAsync()).ShouldBe("ids.required");

        var demais = await BulkAsync(http, Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()), "settle");
        demais.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await demais.CodeAsync()).ShouldBe("ids.too_many");

        var acao = await BulkAsync(http, [carla], "archive");
        acao.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await acao.CodeAsync()).ShouldBe("action.invalid");

        (await PendingIdsAsync(http)).ShouldBe([carla]);
    }

    [Fact]
    public async Task Pending_and_bulk_require_authentication()
    {
        using var anonymous = Factory.CreateClient();

        (await anonymous.GetAsync("/api/v1/entries/pending")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await BulkAsync(anonymous, [Guid.NewGuid()], "settle")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
