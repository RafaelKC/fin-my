using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FinMy.Api.Idempotency;
using FinMy.Api.Tests.Support;
using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Entries;
using FinMy.Infrastructure.Context;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinMy.Api.Tests.Entries;

/// <summary>
/// <c>POST /entries</c>, <c>GET|PUT /entries/{id}</c> e <c>PATCH /entries/{id}/status</c> (US2.1–US2.6 pelo lado do
/// servidor, US6.1 e US6.2). "Hoje" é 10/03/2026, como nos exemplos da spec; carteiras são criadas direto no banco.
/// </summary>
public class EntryEndpointsTests(PostgresFixture postgres) : ApiTestBase(postgres)
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

    private static Task<HttpResponseMessage> PostAsync(HttpClient http, object body, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/entries")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation(IdempotencyFilter.HeaderName, key);
        }

        return http.SendAsync(request);
    }

    private static Dictionary<string, object?> Saida(Guid wallet, string amount = "44,99", string date = "2026-03-10", string description = "Tim", object? extra = null) =>
        Merge(new Dictionary<string, object?> { ["kind"] = "expense", ["description"] = description, ["amount"] = amount, ["date"] = date, ["walletId"] = wallet }, extra);

    private static Dictionary<string, object?> Merge(Dictionary<string, object?> body, object? extra)
    {
        foreach (var property in extra?.GetType().GetProperties() ?? [])
        {
            body[property.Name] = property.GetValue(extra);
        }

        return body;
    }

    private static Dictionary<string, object?> Update(JsonElement entry, object? extra = null) =>
        Merge(
            new Dictionary<string, object?>
            {
                ["kind"] = entry.GetProperty("kind").GetString(),
                ["description"] = entry.GetProperty("description").GetString(),
                ["amount"] = "44,99",
                ["date"] = entry.GetProperty("date").GetString(),
                ["walletId"] = entry.GetProperty("walletId").GetGuid(),
                ["status"] = entry.GetProperty("status").GetString(),
                ["version"] = entry.GetProperty("version").GetInt64(),
            },
            extra);

    private static async Task<JsonElement> CreateAsync(HttpClient http, object body)
    {
        var response = await PostAsync(http, body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await response.JsonAsync();
    }

    // ---- US2.1 a US2.3: criação ----

    [Fact]
    public async Task Cria_saida_com_os_padroes_da_US2_1()
    {
        var (lia, _, azul) = await NewUserAsync();

        var response = await PostAsync(lia, Saida(azul));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var entry = await response.JsonAsync();
        response.Headers.Location!.ToString().ShouldBe($"/api/v1/entries/{entry.GetProperty("id").GetGuid()}");
        entry.GetProperty("kind").GetString().ShouldBe("expense");
        entry.GetProperty("description").GetString().ShouldBe("Tim");
        entry.GetProperty("amountCents").GetInt64().ShouldBe(4499);
        entry.GetProperty("date").GetString().ShouldBe("2026-03-10");
        entry.GetProperty("walletId").GetGuid().ShouldBe(azul);
        entry.GetProperty("status").GetString().ShouldBe("settled");
        entry.GetProperty("referenceMonth").GetString().ShouldBe("2026-03");
        entry.GetProperty("version").GetInt64().ShouldBeGreaterThan(0);
        entry.TryGetProperty("createdAt", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Avalia_a_expressao_do_valor_no_servidor_US2_2()
    {
        var (lia, _, azul) = await NewUserAsync();

        var entry = await CreateAsync(lia, Saida(azul, amount: "29+73,2"));

        entry.GetProperty("amountCents").GetInt64().ShouldBe(10220);
    }

    [Fact]
    public async Task Data_futura_sem_situacao_fica_prevista_US2_3()
    {
        var (lia, _, azul) = await NewUserAsync();

        var future = await CreateAsync(lia, Saida(azul, date: "2026-03-25"));
        var sameDay = await CreateAsync(lia, Saida(azul, date: "2026-03-10"));
        var past = await CreateAsync(lia, Saida(azul, date: "2026-03-09"));

        future.GetProperty("status").GetString().ShouldBe("planned");
        sameDay.GetProperty("status").GetString().ShouldBe("settled");
        past.GetProperty("status").GetString().ShouldBe("settled");
    }

    [Fact]
    public async Task Situacao_e_mes_informados_valem_sobre_os_padroes()
    {
        var (lia, _, azul) = await NewUserAsync();

        var entry = await CreateAsync(lia, Saida(azul, date: "2026-03-25", extra: new { status = "settled", referenceMonth = "2026-04", notes = "  Fatura  " }));

        entry.GetProperty("status").GetString().ShouldBe("settled");
        entry.GetProperty("referenceMonth").GetString().ShouldBe("2026-04");
        entry.GetProperty("notes").GetString().ShouldBe("Fatura");
    }

    [Fact]
    public async Task Cria_entrada()
    {
        var (lia, _, azul) = await NewUserAsync();

        var entry = await CreateAsync(lia, Merge(new() { ["kind"] = "income", ["description"] = "Salário", ["amount"] = "1.500,00", ["date"] = "2026-03-05", ["walletId"] = azul }, null));

        entry.GetProperty("kind").GetString().ShouldBe("income");
        entry.GetProperty("amountCents").GetInt64().ShouldBe(150000);
    }

    // ---- US2.6: validação ----

    [Theory]
    [InlineData("0", "amount.must_be_positive")]
    [InlineData("-5", "amount.must_be_positive")]
    [InlineData("10-10", "amount.must_be_positive")]
    [InlineData("1/0", "amount.division_by_zero")]
    [InlineData("abc", "amount.invalid_expression")]
    [InlineData("", "amount.invalid_expression")]
    [InlineData("10000000", "amount.too_large")]
    public async Task Valor_invalido_responde_422_com_o_codigo_do_parser_US2_6(string amount, string code)
    {
        var (lia, _, azul) = await NewUserAsync();

        var response = await PostAsync(lia, Saida(azul, amount: amount));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await response.JsonAsync();
        problem.GetProperty("code").GetString().ShouldBe(code);
        problem.GetProperty("errors").TryGetProperty("amount", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Valor_ausente_responde_422()
    {
        var (lia, _, azul) = await NewUserAsync();

        var response = await PostAsync(lia, new { kind = "expense", description = "Tim", date = "2026-03-10", walletId = azul });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.CodeAsync()).ShouldBe("amount.invalid_expression");
    }

    [Theory]
    [InlineData("", "description.required")]
    [InlineData("   ", "description.required")]
    public async Task Descricao_vazia_responde_422(string description, string code)
    {
        var (lia, _, azul) = await NewUserAsync();

        var response = await PostAsync(lia, Saida(azul, description: description));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.CodeAsync()).ShouldBe(code);
    }

    [Fact]
    public async Task Descricao_e_observacao_longas_demais_respondem_422()
    {
        var (lia, _, azul) = await NewUserAsync();

        var longDescription = await PostAsync(lia, Saida(azul, description: new string('a', 81)));
        var longNotes = await PostAsync(lia, Saida(azul, extra: new { notes = new string('b', 501) }));

        longDescription.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await longDescription.CodeAsync()).ShouldBe("description.too_long");
        longNotes.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await longNotes.CodeAsync()).ShouldBe("notes.too_long");
    }

    [Theory]
    [InlineData("1999-12-31")]
    [InlineData("2101-01-01")]
    public async Task Data_fora_do_intervalo_responde_422(string date)
    {
        var (lia, _, azul) = await NewUserAsync();

        var response = await PostAsync(lia, Saida(azul, date: date));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.CodeAsync()).ShouldBe("date.out_of_range");
    }

    [Fact]
    public async Task Campos_obrigatorios_e_formatos_invalidos_respondem_422()
    {
        var (lia, _, azul) = await NewUserAsync();

        var semData = await PostAsync(lia, new { kind = "expense", description = "Tim", amount = "1", walletId = azul });
        var semCarteira = await PostAsync(lia, new { kind = "expense", description = "Tim", amount = "1", date = "2026-03-10" });
        var tipoInvalido = await PostAsync(lia, Saida(azul, extra: new { kind = "transfer" }));
        var situacaoInvalida = await PostAsync(lia, Saida(azul, extra: new { status = "pago" }));
        var mesInvalido = await PostAsync(lia, Saida(azul, extra: new { referenceMonth = "2026-13" }));

        (await semData.CodeAsync()).ShouldBe("date.required");
        (await semCarteira.CodeAsync()).ShouldBe("wallet.required");
        (await tipoInvalido.CodeAsync()).ShouldBe("kind.invalid");
        (await situacaoInvalida.CodeAsync()).ShouldBe("status.invalid");
        (await mesInvalido.CodeAsync()).ShouldBe("referenceMonth.invalid");
        foreach (var response in new[] { semData, semCarteira, tipoInvalido, situacaoInvalida, mesInvalido })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }
    }

    // ---- carteira ----

    [Fact]
    public async Task Carteira_arquivada_nao_recebe_lancamento_novo()
    {
        var (lia, account, _) = await NewUserAsync();
        var verde = await Factory.SeedWalletAsync(account, "Verde", archived: true);

        var response = await PostAsync(lia, Saida(verde));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await response.JsonAsync();
        problem.GetProperty("code").GetString().ShouldBe("wallet.archived");
        problem.GetProperty("errors").TryGetProperty("walletId", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Carteira_inexistente_ou_de_outro_Espaco_responde_404()
    {
        var (lia, _, _) = await NewUserAsync("Lia");
        var (_, _, azulDeLeo) = await NewUserAsync("Léo");

        var inexistente = await PostAsync(lia, Saida(Guid.NewGuid()));
        var deOutro = await PostAsync(lia, Saida(azulDeLeo));

        inexistente.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        deOutro.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await deOutro.CodeAsync()).ShouldBe("not_found");
    }

    // ---- idempotência ----

    [Fact]
    public async Task Idempotency_Key_repetida_devolve_o_mesmo_lancamento_sem_duplicar()
    {
        var (lia, account, azul) = await NewUserAsync();
        var key = Guid.NewGuid().ToString();

        var first = await PostAsync(lia, Saida(azul), key);
        var second = await PostAsync(lia, Saida(azul), key);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.Headers.GetValues(IdempotencyFilter.ReplayedHeaderName).ShouldBe(["true"]);
        (await second.JsonAsync()).GetProperty("id").GetGuid().ShouldBe((await first.JsonAsync()).GetProperty("id").GetGuid());
        (await CountEntriesAsync(account)).ShouldBe(1);
    }

    [Fact]
    public async Task Idempotency_Key_com_corpo_diferente_responde_422()
    {
        var (lia, _, azul) = await NewUserAsync();
        var key = Guid.NewGuid().ToString();
        await PostAsync(lia, Saida(azul), key);

        var response = await PostAsync(lia, Saida(azul, amount: "50"), key);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.CodeAsync()).ShouldBe("idempotency.key_reused");
    }

    [Fact]
    public async Task Sem_a_chave_cada_requisicao_cria_um_lancamento()
    {
        var (lia, account, azul) = await NewUserAsync();

        await PostAsync(lia, Saida(azul));
        await PostAsync(lia, Saida(azul));

        (await CountEntriesAsync(account)).ShouldBe(2);
    }

    // ---- GET ----

    [Fact]
    public async Task Busca_o_lancamento_pelo_id_e_nao_enxerga_o_de_outro_Espaco()
    {
        var (lia, _, azul) = await NewUserAsync("Lia");
        var (leo, _, _) = await NewUserAsync("Léo");
        var created = await CreateAsync(lia, Saida(azul));
        var id = created.GetProperty("id").GetGuid();

        var own = await lia.GetAsync($"/api/v1/entries/{id}");
        var other = await leo.GetAsync($"/api/v1/entries/{id}");
        var missing = await lia.GetAsync($"/api/v1/entries/{Guid.NewGuid()}");

        own.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await own.JsonAsync()).GetProperty("version").GetInt64().ShouldBe(created.GetProperty("version").GetInt64());
        other.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.CodeAsync()).ShouldBe("not_found");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Exige_autenticacao()
    {
        using var anonymous = Factory.CreateClient();

        var response = await anonymous.GetAsync($"/api/v1/entries/{Guid.NewGuid()}");
        var suggestions = await anonymous.GetAsync("/api/v1/entries/suggestions?q=a&kind=expense");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        suggestions.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---- US6.1 e US6.2: edição e Mês de referência (RT-04) ----

    [Fact]
    public async Task Alterar_a_data_leva_o_mes_junto_quando_ele_nunca_foi_alterado_US6_1()
    {
        var (lia, _, azul) = await NewUserAsync();
        var created = await CreateAsync(lia, Saida(azul, date: "2026-03-02"));
        var id = created.GetProperty("id").GetGuid();

        var response = await lia.PutAsJsonAsync($"/api/v1/entries/{id}", Update(created, new { date = "2026-04-05" }));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await response.JsonAsync();
        updated.GetProperty("date").GetString().ShouldBe("2026-04-05");
        updated.GetProperty("referenceMonth").GetString().ShouldBe("2026-04");
    }

    [Fact]
    public async Task Mes_alterado_a_mao_nao_acompanha_a_data_US6_2()
    {
        var (lia, _, azul) = await NewUserAsync();
        var created = await CreateAsync(lia, Saida(azul, date: "2026-02-25", extra: new { referenceMonth = "2026-03" }));
        var id = created.GetProperty("id").GetGuid();

        var response = await lia.PutAsJsonAsync($"/api/v1/entries/{id}", Update(created, new { date = "2026-02-26", referenceMonth = "2026-03" }));
        var semMes = await lia.PutAsJsonAsync($"/api/v1/entries/{id}", Update(await response.JsonAsync(), new { date = "2026-02-27" }));

        (await response.JsonAsync()).GetProperty("referenceMonth").GetString().ShouldBe("2026-03");
        (await semMes.JsonAsync()).GetProperty("referenceMonth").GetString().ShouldBe("2026-03");
    }

    [Fact]
    public async Task Informar_outro_mes_no_PUT_define_o_mes_a_mao()
    {
        var (lia, _, azul) = await NewUserAsync();
        var created = await CreateAsync(lia, Saida(azul, date: "2026-03-02"));
        var id = created.GetProperty("id").GetGuid();

        var response = await lia.PutAsJsonAsync($"/api/v1/entries/{id}", Update(created, new { referenceMonth = "2026-06" }));
        var depois = await lia.PutAsJsonAsync($"/api/v1/entries/{id}", Update(await response.JsonAsync(), new { date = "2026-03-20", referenceMonth = "2026-06" }));

        (await response.JsonAsync()).GetProperty("referenceMonth").GetString().ShouldBe("2026-06");
        var final = await depois.JsonAsync();
        final.GetProperty("date").GetString().ShouldBe("2026-03-20");
        final.GetProperty("referenceMonth").GetString().ShouldBe("2026-06");
    }

    [Fact]
    public async Task Edita_todos_os_campos_e_a_versao_muda()
    {
        var (lia, account, azul) = await NewUserAsync();
        var verde = await Factory.SeedWalletAsync(account, "Verde");
        var created = await CreateAsync(lia, Saida(azul));
        var id = created.GetProperty("id").GetGuid();

        var response = await lia.PutAsJsonAsync($"/api/v1/entries/{id}", Update(created, new
        {
            kind = "income",
            description = "Duda",
            amount = "18,27",
            walletId = verde,
            status = "planned",
            notes = "Volta do jantar",
        }));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await response.JsonAsync();
        updated.GetProperty("kind").GetString().ShouldBe("income");
        updated.GetProperty("description").GetString().ShouldBe("Duda");
        updated.GetProperty("amountCents").GetInt64().ShouldBe(1827);
        updated.GetProperty("walletId").GetGuid().ShouldBe(verde);
        updated.GetProperty("status").GetString().ShouldBe("planned");
        updated.GetProperty("notes").GetString().ShouldBe("Volta do jantar");
        updated.GetProperty("version").GetInt64().ShouldNotBe(created.GetProperty("version").GetInt64());
        updated.GetProperty("id").GetGuid().ShouldBe(id);
        (await (await lia.GetAsync($"/api/v1/entries/{id}")).JsonAsync()).GetProperty("description").GetString().ShouldBe("Duda");
    }

    [Fact]
    public async Task Versao_desatualizada_responde_409_e_nada_muda()
    {
        var (lia, _, azul) = await NewUserAsync();
        var created = await CreateAsync(lia, Saida(azul));
        var id = created.GetProperty("id").GetGuid();
        (await lia.PutAsJsonAsync($"/api/v1/entries/{id}", Update(created, new { description = "Tim Pós" }))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var stale = await lia.PutAsJsonAsync($"/api/v1/entries/{id}", Update(created, new { description = "Outro aparelho" }));

        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await stale.JsonAsync();
        problem.GetProperty("code").GetString().ShouldBe("concurrency_conflict");
        problem.GetProperty("detail").GetString().ShouldBe("Este lançamento foi alterado em outro aparelho. Recarregue.");
        (await (await lia.GetAsync($"/api/v1/entries/{id}")).JsonAsync()).GetProperty("description").GetString().ShouldBe("Tim Pós");
    }

    [Fact]
    public async Task Versao_desatualizada_conflita_mesmo_sem_mudar_nada()
    {
        var (lia, _, azul) = await NewUserAsync();
        var created = await CreateAsync(lia, Saida(azul));
        var id = created.GetProperty("id").GetGuid();
        await lia.PatchAsJsonAsync($"/api/v1/entries/{id}/status", new { status = "planned" });

        var stale = await lia.PutAsJsonAsync($"/api/v1/entries/{id}", Update(created));

        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task PUT_sem_versao_responde_422()
    {
        var (lia, _, azul) = await NewUserAsync();
        var created = await CreateAsync(lia, Saida(azul));

        var response = await lia.PutAsJsonAsync($"/api/v1/entries/{created.GetProperty("id").GetGuid()}", Update(created, new { version = (long?)null }));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.CodeAsync()).ShouldBe("version.required");
    }

    [Fact]
    public async Task PUT_valida_os_campos_como_a_criacao()
    {
        var (lia, _, azul) = await NewUserAsync();
        var created = await CreateAsync(lia, Saida(azul));
        var url = $"/api/v1/entries/{created.GetProperty("id").GetGuid()}";

        var valor = await lia.PutAsJsonAsync(url, Update(created, new { amount = "0" }));
        var status = await lia.PutAsJsonAsync(url, Update(created, new { status = (string?)null }));
        var data = await lia.PutAsJsonAsync(url, Update(created, new { date = "2101-01-01" }));

        (await valor.CodeAsync()).ShouldBe("amount.must_be_positive");
        (await status.CodeAsync()).ShouldBe("status.invalid");
        (await data.CodeAsync()).ShouldBe("date.out_of_range");
    }

    [Fact]
    public async Task PUT_de_outro_Espaco_ou_inexistente_responde_404()
    {
        var (lia, _, azul) = await NewUserAsync("Lia");
        var (leo, _, azulDeLeo) = await NewUserAsync("Léo");
        var created = await CreateAsync(lia, Saida(azul));

        var other = await leo.PutAsJsonAsync($"/api/v1/entries/{created.GetProperty("id").GetGuid()}", Update(created, new { walletId = azulDeLeo }));
        var missing = await lia.PutAsJsonAsync($"/api/v1/entries/{Guid.NewGuid()}", Update(created));

        other.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Trocar_para_carteira_arquivada_ou_de_outro_Espaco_e_recusado()
    {
        var (lia, account, azul) = await NewUserAsync();
        var (_, _, azulDeLeo) = await NewUserAsync("Léo");
        var verde = await Factory.SeedWalletAsync(account, "Verde", archived: true);
        var created = await CreateAsync(lia, Saida(azul));
        var url = $"/api/v1/entries/{created.GetProperty("id").GetGuid()}";

        var arquivada = await lia.PutAsJsonAsync(url, Update(created, new { walletId = verde }));
        var deOutro = await lia.PutAsJsonAsync(url, Update(created, new { walletId = azulDeLeo }));

        arquivada.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await arquivada.CodeAsync()).ShouldBe("wallet.archived");
        deOutro.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Editar_outros_campos_de_lancamento_de_carteira_arquivada_e_permitido()
    {
        var (lia, account, azul) = await NewUserAsync();
        var created = await CreateAsync(lia, Saida(azul));
        await Factory.ArchiveWalletAsync(account, azul);

        var response = await lia.PutAsJsonAsync($"/api/v1/entries/{created.GetProperty("id").GetGuid()}", Update(created, new { description = "Tim Pós", amount = "50" }));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.JsonAsync()).GetProperty("amountCents").GetInt64().ShouldBe(5000);
    }

    // ---- PATCH /status ----

    [Fact]
    public async Task Troca_a_situacao_com_um_toque_nos_dois_sentidos()
    {
        var (lia, _, azul) = await NewUserAsync();
        var created = await CreateAsync(lia, Saida(azul));
        var id = created.GetProperty("id").GetGuid();

        var planned = await lia.PatchAsJsonAsync($"/api/v1/entries/{id}/status", new { status = "planned" });
        var settled = await lia.PatchAsJsonAsync($"/api/v1/entries/{id}/status", new { status = "settled" });

        planned.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await planned.JsonAsync()).GetProperty("status").GetString().ShouldBe("planned");
        (await settled.JsonAsync()).GetProperty("status").GetString().ShouldBe("settled");
    }

    [Fact]
    public async Task Trocar_a_situacao_nao_exige_versao_e_muda_a_versao()
    {
        var (lia, _, azul) = await NewUserAsync();
        var created = await CreateAsync(lia, Saida(azul));

        var response = await lia.PatchAsJsonAsync($"/api/v1/entries/{created.GetProperty("id").GetGuid()}/status", new { status = "planned" });

        (await response.JsonAsync()).GetProperty("version").GetInt64().ShouldNotBe(created.GetProperty("version").GetInt64());
    }

    [Fact]
    public async Task Situacao_invalida_responde_422_e_inexistente_404()
    {
        var (lia, _, azul) = await NewUserAsync("Lia");
        var (leo, _, _) = await NewUserAsync("Léo");
        var created = await CreateAsync(lia, Saida(azul));
        var id = created.GetProperty("id").GetGuid();

        var invalid = await lia.PatchAsJsonAsync($"/api/v1/entries/{id}/status", new { status = "pago" });
        var other = await leo.PatchAsJsonAsync($"/api/v1/entries/{id}/status", new { status = "planned" });
        var missing = await lia.PatchAsJsonAsync($"/api/v1/entries/{Guid.NewGuid()}/status", new { status = "planned" });

        invalid.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        other.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await (await lia.GetAsync($"/api/v1/entries/{id}")).JsonAsync()).GetProperty("status").GetString().ShouldBe("settled");
    }

    private async Task<int> CountEntriesAsync(TestAccount account)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentContext>().Populate(account.Session.User.Id, account.Session.Tenant.Id, FinMy.Domain.Common.Channel.App);
        return await scope.ServiceProvider.GetRequiredService<FinMyDbContext>().Set<Entry>().CountAsync();
    }
}
