using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Api.Tests.Support;
using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;

namespace FinMy.Api.Tests.Entries;

/// <summary><c>GET /entries/search</c> (US6.5, FR-025, research R10). "Hoje" é 10/03/2026.</summary>
public class EntrySearchTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private static readonly DateOnly Today = new(2026, 3, 10);

    protected override IClock? Clock => new FixedTodayClock(Today);

    private sealed record Setup(HttpClient Http, TestAccount Account, Guid Azul, Guid Verde);

    private async Task<Setup> NewUserAsync(string name = "Lia")
    {
        using var anonymous = Factory.CreateClient();
        var account = await anonymous.SignUpAsync(name: name);
        var http = Factory.CreateClient().WithBearer(account.AccessToken);
        return new Setup(http, account, await Factory.SeedWalletAsync(account, "Azul"), await Factory.SeedWalletAsync(account, "Verde"));
    }

    private static async Task<Guid> AddAsync(Setup s, string description, string amount, string date, string kind = "expense", Guid? wallet = null, string? status = null)
    {
        var response = await s.Http.PostAsJsonAsync("/api/v1/entries", new { kind, description, amount, date, walletId = wallet ?? s.Azul, status });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> TransferAsync(Setup s, string description, string amount, string date, Guid from, Guid to)
    {
        var response = await s.Http.PostAsJsonAsync("/api/v1/transfers", new { fromWalletId = from, toWalletId = to, amount, date, description });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.JsonAsync()).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> SearchAsync(Setup s, string query = "")
    {
        var response = await s.Http.GetAsync($"/api/v1/entries/search{(query.Length == 0 ? string.Empty : "?" + query)}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.JsonAsync();
    }

    private static List<Guid> Ids(JsonElement result) =>
        result.GetProperty("items").EnumerateArray().Select(i => i.GetProperty(i.GetProperty("type").GetString() == "transfer" ? "transfer" : "entry").GetProperty("id").GetGuid()).ToList();

    [Fact]
    public async Task Acha_em_qualquer_mes_sem_diferenciar_maiusculas_e_acentos_com_o_total_US6_5()
    {
        var s = await NewUserAsync();
        var jan = await AddAsync(s, "Uber Lia", "20,00", "2026-01-15");
        var fev = await AddAsync(s, "UBER do Léo", "30,00", "2026-02-20");
        var mar = await AddAsync(s, "Pagamento de uber", "50,00", "2026-03-05");
        await AddAsync(s, "Padaria", "10,00", "2026-03-06");

        var result = await SearchAsync(s, "q=" + Uri.EscapeDataString("ÜBER"));

        Ids(result).ShouldBe([mar, fev, jan]); // data desc
        result.GetProperty("totalCount").GetInt32().ShouldBe(3);
        result.GetProperty("totalCents").GetInt64().ShouldBe(-10_000);
        result.GetProperty("nextCursor").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Acentos_na_descricao_casam_com_a_busca_sem_acento()
    {
        var s = await NewUserAsync();
        var id = await AddAsync(s, "Pão Açúcar", "12,00", "2026-03-01");

        Ids(await SearchAsync(s, "q=pao%20acucar")).ShouldBe([id]);
        Ids(await SearchAsync(s, "q=" + Uri.EscapeDataString("AÇÚCAR"))).ShouldBe([id]);
    }

    [Fact]
    public async Task Curingas_do_LIKE_na_busca_valem_como_texto_comum()
    {
        var s = await NewUserAsync();
        var percent = await AddAsync(s, "Desconto 10% Bruna", "5,00", "2026-03-01");
        var underscore = await AddAsync(s, "Plano_Carla", "6,00", "2026-03-02");
        await AddAsync(s, "Desconto 100 Bruna", "7,00", "2026-03-03");
        await AddAsync(s, "PlanoXCarla", "8,00", "2026-03-04");

        Ids(await SearchAsync(s, "q=" + Uri.EscapeDataString("10%"))).ShouldBe([percent]);
        Ids(await SearchAsync(s, "q=" + Uri.EscapeDataString("o_c"))).ShouldBe([underscore]);
        (await SearchAsync(s, "q=" + Uri.EscapeDataString("%"))).GetProperty("totalCount").GetInt32().ShouldBe(1);
        (await SearchAsync(s, "q=" + Uri.EscapeDataString("\\"))).GetProperty("totalCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Total_soma_entradas_e_subtrai_saidas_e_transferencias_valem_zero()
    {
        var s = await NewUserAsync();
        await AddAsync(s, "Duda reembolso", "100,00", "2026-03-01", kind: "income");
        await AddAsync(s, "Duda lanche", "30,00", "2026-03-02");
        await TransferAsync(s, "Reserva da Duda", "500,00", "2026-03-03", s.Azul, s.Verde);

        var result = await SearchAsync(s, "q=duda");

        result.GetProperty("totalCount").GetInt32().ShouldBe(3);
        result.GetProperty("totalCents").GetInt64().ShouldBe(7_000);
        result.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("type").GetString()).ShouldBe(["transfer", "expense", "income"]);
    }

    [Fact]
    public async Task Item_traz_so_o_objeto_do_seu_tipo_com_version()
    {
        var s = await NewUserAsync();
        await AddAsync(s, "Tim", "44,99", "2026-03-01");
        await TransferAsync(s, "Tim reserva", "10,00", "2026-03-02", s.Azul, s.Verde);

        var items = (await SearchAsync(s)).GetProperty("items").EnumerateArray().ToList();

        var transfer = items[0];
        transfer.GetProperty("type").GetString().ShouldBe("transfer");
        transfer.TryGetProperty("entry", out var noEntry).ShouldBeTrue();
        noEntry.ValueKind.ShouldBe(JsonValueKind.Null);
        transfer.GetProperty("transfer").GetProperty("version").GetInt64().ShouldBeGreaterThan(0);
        var entry = items[1];
        entry.GetProperty("type").GetString().ShouldBe("expense");
        entry.GetProperty("entry").GetProperty("amountCents").GetInt64().ShouldBe(4499);
        entry.GetProperty("transfer").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Filtra_por_tipo_carteira_situacao_e_periodo()
    {
        var s = await NewUserAsync();
        var salario = await AddAsync(s, "Salário Lia", "1000,00", "2026-02-05", kind: "income", wallet: s.Azul);
        var mercado = await AddAsync(s, "Mercado", "200,00", "2026-02-10", wallet: s.Verde);
        var prevista = await AddAsync(s, "Seguro", "80,00", "2026-04-10", wallet: s.Azul);
        var transfer = await TransferAsync(s, "Reserva", "50,00", "2026-02-12", s.Azul, s.Verde);

        Ids(await SearchAsync(s, "kinds=income")).ShouldBe([salario]);
        Ids(await SearchAsync(s, "kinds=expense,transfer")).ShouldBe([prevista, transfer, mercado]);
        Ids(await SearchAsync(s, $"walletIds={s.Verde}")).ShouldBe([transfer, mercado]); // transferência pela origem ou destino
        Ids(await SearchAsync(s, "status=planned")).ShouldBe([prevista]); // transferências não têm situação
        Ids(await SearchAsync(s, "from=2026-02-06&to=2026-02-11")).ShouldBe([mercado]);
        Ids(await SearchAsync(s, "from=2026-02-10&to=2026-02-12")).ShouldBe([transfer, mercado]); // limites inclusivos
        (await SearchAsync(s, "kinds=transfer&status=settled")).GetProperty("totalCount").GetInt32().ShouldBe(0);
    }

    [Fact]
    public async Task Paginacao_por_cursor_e_estavel_e_o_total_vale_para_todos_os_resultados()
    {
        var s = await NewUserAsync();
        var expected = new List<Guid>();
        for (var day = 1; day <= 7; day++)
        {
            expected.Add(await AddAsync(s, $"Lia item {day}", "10,00", $"2026-03-{day:00}"));
        }

        // mesma data e criados em sequência: o desempate é por criação e id
        expected.Add(await AddAsync(s, "Lia empate A", "10,00", "2026-03-07"));
        expected.Add(await AddAsync(s, "Lia empate B", "10,00", "2026-03-07"));
        expected.Reverse(); // mais recente primeiro: B, A, depois dia 7 .. 1 (o do dia 7 original criado antes do A)

        var seen = new List<Guid>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var result = await SearchAsync(s, "q=lia&limit=3" + (cursor is null ? string.Empty : "&cursor=" + Uri.EscapeDataString(cursor)));
            result.GetProperty("totalCount").GetInt32().ShouldBe(9);
            result.GetProperty("totalCents").GetInt64().ShouldBe(-9_000);
            seen.AddRange(Ids(result));
            cursor = result.GetProperty("nextCursor").GetString();
            pages++;
        }
        while (cursor is not null);

        pages.ShouldBe(3);
        seen.ShouldBe(expected);
        seen.Distinct().Count().ShouldBe(9);
    }

    [Fact]
    public async Task Item_novo_entre_paginas_nao_repete_nem_pula_itens_da_proxima_pagina()
    {
        var s = await NewUserAsync();
        var a = await AddAsync(s, "Léo 1", "10,00", "2026-03-05");
        var b = await AddAsync(s, "Léo 2", "10,00", "2026-03-04");
        var c = await AddAsync(s, "Léo 3", "10,00", "2026-03-03");

        var first = await SearchAsync(s, "q=leo&limit=2");
        await AddAsync(s, "Léo novo", "10,00", "2026-03-09"); // mais recente: fica antes do cursor
        var second = await SearchAsync(s, "q=leo&limit=2&cursor=" + Uri.EscapeDataString(first.GetProperty("nextCursor").GetString()!));

        Ids(first).ShouldBe([a, b]);
        Ids(second).ShouldBe([c]);
        second.GetProperty("nextCursor").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Sem_filtros_traz_tudo_do_espaco_e_nao_mostra_excluidos_nem_outro_espaco()
    {
        var lia = await NewUserAsync("Lia");
        var leo = await NewUserAsync("Léo");
        var keep = await AddAsync(lia, "Fica", "10,00", "2026-03-01");
        var gone = await AddAsync(lia, "Some", "10,00", "2026-03-02");
        await AddAsync(leo, "Do Léo", "10,00", "2026-03-03");
        (await lia.Http.DeleteAsync($"/api/v1/entries/{gone}")).EnsureSuccessStatusCode();

        var result = await SearchAsync(lia);

        Ids(result).ShouldBe([keep]);
        result.GetProperty("totalCount").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Sem_resultados_devolve_lista_vazia_e_total_zero()
    {
        var s = await NewUserAsync();
        await AddAsync(s, "Tim", "10,00", "2026-03-01");

        var result = await SearchAsync(s, "q=inexistente");

        result.GetProperty("items").GetArrayLength().ShouldBe(0);
        result.GetProperty("totalCents").GetInt64().ShouldBe(0);
        result.GetProperty("totalCount").GetInt32().ShouldBe(0);
    }

    [Theory]
    [InlineData("kinds=salary", "kinds.invalid")]
    [InlineData("walletIds=abc", "walletIds.invalid")]
    [InlineData("status=feito", "status.invalid")]
    [InlineData("limit=0", "limit.invalid")]
    [InlineData("limit=201", "limit.invalid")]
    [InlineData("cursor=lixo", "cursor.invalid")]
    [InlineData("from=2026-03-10&to=2026-03-01", "period.invalid")]
    public async Task Parametros_invalidos_dao_422_com_codigo_estavel(string query, string code)
    {
        var s = await NewUserAsync();

        var response = await s.Http.GetAsync($"/api/v1/entries/search?{query}");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.CodeAsync()).ShouldBe(code);
    }

    [Fact]
    public async Task Texto_acima_de_80_caracteres_da_422()
    {
        var s = await NewUserAsync();

        var response = await s.Http.GetAsync($"/api/v1/entries/search?q={new string('a', 81)}");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.CodeAsync()).ShouldBe("q.too_long");
    }

    [Fact]
    public async Task Sem_login_a_busca_da_401()
    {
        using var anonymous = Factory.CreateClient();

        (await anonymous.GetAsync("/api/v1/entries/search")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
