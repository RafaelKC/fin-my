using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FinMy.Api.Idempotency;
using FinMy.Api.Tests.Entries;
using FinMy.Api.Tests.Support;
using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Common;
using FinMy.Domain.Monetary;
using FinMy.Domain.Transfers;
using FinMy.Domain.Wallets;
using FinMy.Infrastructure.Context;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinMy.Api.Tests.Transfers;

/// <summary>
/// <c>/api/v1/transfers</c> (US5.1 e US5.3 pelo servidor, exclusão e desfazer). Carteiras são criadas direto no banco
/// (a API de carteiras é outra tarefa); o saldo é conferido pela mesma regra de research R5: saldo inicial + transferências
/// recebidas − enviadas.
/// </summary>
public class TransferEndpointsTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private static readonly DateOnly Today = new(2026, 6, 29);

    protected override IClock? Clock => new FixedTodayClock(Today);

    private sealed record Setup(HttpClient Http, TestAccount Account, Guid Azul, Guid Poupanca);

    private async Task<Setup> NewUserAsync(string name = "Lia")
    {
        using var anonymous = Factory.CreateClient();
        var account = await anonymous.SignUpAsync(name: name);
        var http = Factory.CreateClient().WithBearer(account.AccessToken);
        var azul = await SeedWalletAsync(account, "Azul", 100_000);
        var poupanca = await SeedWalletAsync(account, "Poupança", 0);
        return new Setup(http, account, azul, poupanca);
    }

    private async Task<Guid> SeedWalletAsync(TestAccount owner, string name, long initialCents, bool archived = false)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentContext>().Populate(owner.Session.User.Id, owner.Session.Tenant.Id, Channel.App);
        var db = scope.ServiceProvider.GetRequiredService<FinMyDbContext>();
        var wallet = Wallet.Create(name, WalletType.Checking, "#1E88E5", Money.FromCents(initialCents), new DateOnly(2026, 1, 1));
        if (archived)
        {
            wallet.Archive(DateTimeOffset.UtcNow);
        }

        db.Add(wallet);
        await db.SaveChangesAsync();
        return wallet.Id;
    }

    /// <summary>Saldo atual (research R5, sem lançamentos): saldo inicial + transferências recebidas − enviadas, ignorando as excluídas.</summary>
    private async Task<long> BalanceAsync(TestAccount owner, Guid walletId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentContext>().Populate(owner.Session.User.Id, owner.Session.Tenant.Id, Channel.App);
        var db = scope.ServiceProvider.GetRequiredService<FinMyDbContext>();
        var initial = await db.Set<Wallet>().Where(w => w.Id == walletId).Select(w => w.InitialBalanceCents).SingleAsync();
        var received = await db.Set<Transfer>().Where(t => t.ToWalletId == walletId).SumAsync(t => (long?)t.AmountCents) ?? 0;
        var sent = await db.Set<Transfer>().Where(t => t.FromWalletId == walletId).SumAsync(t => (long?)t.AmountCents) ?? 0;
        return initial + received - sent;
    }

    private async Task<int> CountTransfersAsync(TestAccount owner)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentContext>().Populate(owner.Session.User.Id, owner.Session.Tenant.Id, Channel.App);
        return await scope.ServiceProvider.GetRequiredService<FinMyDbContext>().Set<Transfer>().CountAsync();
    }

    private static Dictionary<string, object?> Body(Guid from, Guid to, string amount = "20,00", string date = "2026-06-29", object? extra = null)
    {
        var body = new Dictionary<string, object?> { ["fromWalletId"] = from, ["toWalletId"] = to, ["amount"] = amount, ["date"] = date };
        foreach (var property in extra?.GetType().GetProperties() ?? [])
        {
            body[property.Name] = property.GetValue(extra);
        }

        return body;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient http, object body, string? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/transfers")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation(IdempotencyFilter.HeaderName, key);
        }

        return http.SendAsync(request);
    }

    private static async Task<JsonElement> CreateAsync(Setup s, string amount = "20,00", string? description = null)
    {
        var response = await PostAsync(s.Http, Body(s.Azul, s.Poupanca, amount, extra: description is null ? null : new { description }));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await response.JsonAsync();
    }

    // ---- US5.1 ----

    [Fact]
    public async Task Transferir_20_de_Azul_para_Poupanca_move_o_saldo_das_duas_carteiras()
    {
        var s = await NewUserAsync();
        (await BalanceAsync(s.Account, s.Azul)).ShouldBe(100_000);
        (await BalanceAsync(s.Account, s.Poupanca)).ShouldBe(0);

        var response = await PostAsync(s.Http, Body(s.Azul, s.Poupanca));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await response.JsonAsync();
        response.Headers.Location!.ToString().ShouldEndWith($"/api/v1/transfers/{created.GetProperty("id").GetGuid()}");
        created.GetProperty("fromWalletId").GetGuid().ShouldBe(s.Azul);
        created.GetProperty("toWalletId").GetGuid().ShouldBe(s.Poupanca);
        created.GetProperty("amountCents").GetInt64().ShouldBe(2_000);
        created.GetProperty("date").GetString().ShouldBe("2026-06-29");
        created.GetProperty("description").GetString().ShouldBe("Transferência");
        created.GetProperty("notes").ValueKind.ShouldBe(JsonValueKind.Null);
        created.GetProperty("version").GetInt64().ShouldBeGreaterThan(0);
        created.TryGetProperty("createdAt", out _).ShouldBeTrue();
        (await BalanceAsync(s.Account, s.Azul)).ShouldBe(98_000);
        (await BalanceAsync(s.Account, s.Poupanca)).ShouldBe(2_000);
    }

    [Fact]
    public async Task O_valor_e_avaliado_pelo_servidor_e_a_descricao_e_a_observacao_sao_guardadas()
    {
        var s = await NewUserAsync();

        var response = await PostAsync(s.Http, Body(s.Azul, s.Poupanca, "10+10,5", extra: new { description = "  Reserva da Lia ", notes = "Para a viagem" }));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await response.JsonAsync();
        created.GetProperty("amountCents").GetInt64().ShouldBe(2_050);
        created.GetProperty("description").GetString().ShouldBe("Reserva da Lia");
        created.GetProperty("notes").GetString().ShouldBe("Para a viagem");
    }

    [Theory]
    [InlineData("0", "amount.must_be_positive")]
    [InlineData("abc", "amount.invalid_expression")]
    [InlineData("1/0", "amount.division_by_zero")]
    [InlineData("10000000", "amount.too_large")]
    public async Task Valor_invalido_responde_422_com_o_codigo_do_campo_de_valor(string amount, string code)
    {
        var s = await NewUserAsync();

        var response = await PostAsync(s.Http, Body(s.Azul, s.Poupanca, amount));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.CodeAsync()).ShouldBe(code);
        (await CountTransfersAsync(s.Account)).ShouldBe(0);
    }

    [Fact]
    public async Task Campos_obrigatorios_ausentes_respondem_422_com_erro_por_campo()
    {
        var s = await NewUserAsync();

        var response = await PostAsync(s.Http, new { amount = "10" });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var errors = (await response.JsonAsync()).GetProperty("errors");
        errors.TryGetProperty("fromWalletId", out _).ShouldBeTrue();
        errors.TryGetProperty("toWalletId", out _).ShouldBeTrue();
        errors.TryGetProperty("date", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Data_e_descricao_fora_das_regras_respondem_422()
    {
        var s = await NewUserAsync();

        var data = await PostAsync(s.Http, Body(s.Azul, s.Poupanca, date: "2101-01-01"));
        var descricao = await PostAsync(s.Http, Body(s.Azul, s.Poupanca, extra: new { description = new string('a', 81) }));

        data.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await data.CodeAsync()).ShouldBe("date.out_of_range");
        descricao.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await descricao.CodeAsync()).ShouldBe("description.too_long");
    }

    // ---- US5.3 ----

    [Fact]
    public async Task Origem_e_destino_iguais_respondem_422_transfer_same_wallet_e_nada_e_gravado()
    {
        var s = await NewUserAsync();

        var response = await PostAsync(s.Http, Body(s.Azul, s.Azul));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.CodeAsync()).ShouldBe("transfer.same_wallet");
        (await response.JsonAsync()).GetProperty("errors").TryGetProperty("toWalletId", out _).ShouldBeTrue();
        (await CountTransfersAsync(s.Account)).ShouldBe(0);
    }

    [Fact]
    public async Task Carteira_arquivada_na_origem_ou_no_destino_responde_422_wallet_archived()
    {
        var s = await NewUserAsync();
        var verde = await SeedWalletAsync(s.Account, "Verde", 0, archived: true);

        var destino = await PostAsync(s.Http, Body(s.Azul, verde));
        var origem = await PostAsync(s.Http, Body(verde, s.Azul));

        destino.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await destino.CodeAsync()).ShouldBe("wallet.archived");
        (await destino.JsonAsync()).GetProperty("errors").TryGetProperty("toWalletId", out _).ShouldBeTrue();
        origem.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await origem.JsonAsync()).GetProperty("errors").TryGetProperty("fromWalletId", out _).ShouldBeTrue();
        (await CountTransfersAsync(s.Account)).ShouldBe(0);
    }

    [Fact]
    public async Task Carteira_inexistente_ou_de_outro_Espaco_responde_404_not_found()
    {
        var lia = await NewUserAsync("Lia");
        var leo = await NewUserAsync("Léo");

        var inexistente = await PostAsync(lia.Http, Body(lia.Azul, Guid.NewGuid()));
        var deOutro = await PostAsync(leo.Http, Body(leo.Azul, lia.Poupanca));

        inexistente.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await inexistente.CodeAsync()).ShouldBe("not_found");
        deOutro.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await deOutro.CodeAsync()).ShouldBe("not_found");
        (await CountTransfersAsync(lia.Account)).ShouldBe(0);
        (await CountTransfersAsync(leo.Account)).ShouldBe(0);
    }

    [Fact]
    public async Task Sem_token_todas_as_rotas_respondem_401()
    {
        var id = Guid.NewGuid();

        (await Client.PostAsJsonAsync("/api/v1/transfers", new { })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Client.GetAsync($"/api/v1/transfers/{id}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Client.PutAsJsonAsync($"/api/v1/transfers/{id}", new { })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Client.DeleteAsync($"/api/v1/transfers/{id}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---- Idempotência ----

    [Fact]
    public async Task Mesma_Idempotency_Key_devolve_a_mesma_transferencia_sem_duplicar_e_corpo_diferente_da_422()
    {
        var s = await NewUserAsync();
        var key = Guid.NewGuid().ToString();

        var first = await PostAsync(s.Http, Body(s.Azul, s.Poupanca), key);
        var second = await PostAsync(s.Http, Body(s.Azul, s.Poupanca), key);
        var other = await PostAsync(s.Http, Body(s.Azul, s.Poupanca, "30,00"), key);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await second.JsonAsync()).GetProperty("id").GetGuid().ShouldBe((await first.JsonAsync()).GetProperty("id").GetGuid());
        other.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await other.CodeAsync()).ShouldBe("idempotency.key_reused");
        (await CountTransfersAsync(s.Account)).ShouldBe(1);
        (await BalanceAsync(s.Account, s.Azul)).ShouldBe(98_000);
    }

    // ---- GET ----

    [Fact]
    public async Task Obter_devolve_a_transferencia_e_outro_Espaco_ou_id_inexistente_da_404()
    {
        var lia = await NewUserAsync("Lia");
        var leo = await NewUserAsync("Léo");
        var created = await CreateAsync(lia);
        var id = created.GetProperty("id").GetGuid();

        var own = await lia.Http.GetAsync($"/api/v1/transfers/{id}");
        var foreign = await leo.Http.GetAsync($"/api/v1/transfers/{id}");
        var missing = await lia.Http.GetAsync($"/api/v1/transfers/{Guid.NewGuid()}");

        own.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await own.JsonAsync()).GetRawText().ShouldBe(created.GetRawText());
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await foreign.CodeAsync()).ShouldBe("not_found");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---- PUT ----

    private static Task<HttpResponseMessage> PutAsync(HttpClient http, Guid id, Dictionary<string, object?> body, long? version)
    {
        if (version is not null)
        {
            body["version"] = version;
        }

        return http.PutAsJsonAsync($"/api/v1/transfers/{id}", body);
    }

    [Fact]
    public async Task Editar_com_a_versao_atual_grava_tudo_e_devolve_nova_versao()
    {
        var s = await NewUserAsync();
        var verde = await SeedWalletAsync(s.Account, "Verde", 5_000);
        var created = await CreateAsync(s);
        var id = created.GetProperty("id").GetGuid();
        var version = created.GetProperty("version").GetInt64();

        var response = await PutAsync(
            s.Http, id, Body(s.Poupanca, verde, "35,50", "2026-07-01", new { description = "Para o Verde", notes = "Ajuste" }), version);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await response.JsonAsync();
        updated.GetProperty("id").GetGuid().ShouldBe(id);
        updated.GetProperty("fromWalletId").GetGuid().ShouldBe(s.Poupanca);
        updated.GetProperty("toWalletId").GetGuid().ShouldBe(verde);
        updated.GetProperty("amountCents").GetInt64().ShouldBe(3_550);
        updated.GetProperty("date").GetString().ShouldBe("2026-07-01");
        updated.GetProperty("description").GetString().ShouldBe("Para o Verde");
        updated.GetProperty("notes").GetString().ShouldBe("Ajuste");
        updated.GetProperty("createdAt").GetString().ShouldBe(created.GetProperty("createdAt").GetString());
        updated.GetProperty("version").GetInt64().ShouldNotBe(version);
        (await Reload(s, id)).GetRawText().ShouldBe(updated.GetRawText());
        (await BalanceAsync(s.Account, s.Azul)).ShouldBe(100_000);
        (await BalanceAsync(s.Account, s.Poupanca)).ShouldBe(-3_550);
        (await BalanceAsync(s.Account, verde)).ShouldBe(8_550);
    }

    private static async Task<JsonElement> Reload(Setup s, Guid id) =>
        await (await s.Http.GetAsync($"/api/v1/transfers/{id}")).JsonAsync();

    [Fact]
    public async Task Versao_antiga_responde_409_concurrency_conflict_e_nao_altera_nada()
    {
        var s = await NewUserAsync();
        var created = await CreateAsync(s);
        var id = created.GetProperty("id").GetGuid();
        var version = created.GetProperty("version").GetInt64();
        (await PutAsync(s.Http, id, Body(s.Azul, s.Poupanca, "25,00"), version)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var stale = await PutAsync(s.Http, id, Body(s.Azul, s.Poupanca, "99,00"), version);

        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await stale.CodeAsync()).ShouldBe("concurrency_conflict");
        (await Reload(s, id)).GetProperty("amountCents").GetInt64().ShouldBe(2_500);
    }

    [Fact]
    public async Task Duas_edicoes_com_a_mesma_versao_so_a_primeira_vence()
    {
        var s = await NewUserAsync();
        var created = await CreateAsync(s);
        var id = created.GetProperty("id").GetGuid();
        var version = created.GetProperty("version").GetInt64();

        var a = await PutAsync(s.Http, id, Body(s.Azul, s.Poupanca, "11,00"), version);
        var b = await PutAsync(s.Http, id, Body(s.Azul, s.Poupanca, "12,00"), version);

        a.StatusCode.ShouldBe(HttpStatusCode.OK);
        b.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Editar_sem_version_responde_422()
    {
        var s = await NewUserAsync();
        var id = (await CreateAsync(s)).GetProperty("id").GetGuid();

        var response = await PutAsync(s.Http, id, Body(s.Azul, s.Poupanca), version: null);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.JsonAsync()).GetProperty("errors").TryGetProperty("version", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Editar_com_origem_igual_ao_destino_responde_422_same_wallet()
    {
        var s = await NewUserAsync();
        var created = await CreateAsync(s);

        var response = await PutAsync(s.Http, created.GetProperty("id").GetGuid(), Body(s.Azul, s.Azul), created.GetProperty("version").GetInt64());

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.CodeAsync()).ShouldBe("transfer.same_wallet");
    }

    [Fact]
    public async Task Editar_so_exige_carteira_ativa_nas_carteiras_trocadas()
    {
        var s = await NewUserAsync();
        var created = await CreateAsync(s);
        var id = created.GetProperty("id").GetGuid();
        await Factory.ArchiveWalletAsync(s.Account, s.Poupanca);
        var verde = await SeedWalletAsync(s.Account, "Verde", 0, archived: true);

        var mantendo = await PutAsync(s.Http, id, Body(s.Azul, s.Poupanca, "22,00"), created.GetProperty("version").GetInt64());
        var trocando = await PutAsync(
            s.Http, id, Body(s.Azul, verde, "22,00"), (await mantendo.JsonAsync()).GetProperty("version").GetInt64());

        mantendo.StatusCode.ShouldBe(HttpStatusCode.OK);
        trocando.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await trocando.CodeAsync()).ShouldBe("wallet.archived");
    }

    [Fact]
    public async Task Editar_transferencia_de_outro_Espaco_ou_para_carteira_de_outro_Espaco_responde_404()
    {
        var lia = await NewUserAsync("Lia");
        var leo = await NewUserAsync("Léo");
        var created = await CreateAsync(lia);
        var id = created.GetProperty("id").GetGuid();
        var version = created.GetProperty("version").GetInt64();

        var foreign = await PutAsync(leo.Http, id, Body(leo.Azul, leo.Poupanca), version);
        var walletOfOther = await PutAsync(lia.Http, id, Body(lia.Azul, leo.Poupanca), version);

        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        walletOfOther.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Reload(lia, id)).GetProperty("toWalletId").GetGuid().ShouldBe(lia.Poupanca);
    }

    // ---- DELETE + restaurar ----

    [Fact]
    public async Task Excluir_devolve_o_lote_a_transferencia_some_e_os_saldos_voltam()
    {
        var s = await NewUserAsync();
        var id = (await CreateAsync(s)).GetProperty("id").GetGuid();

        var response = await s.Http.DeleteAsync($"/api/v1/transfers/{id}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.JsonAsync()).GetProperty("deletionBatchId").GetGuid().ShouldNotBe(Guid.Empty);
        (await s.Http.GetAsync($"/api/v1/transfers/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await BalanceAsync(s.Account, s.Azul)).ShouldBe(100_000);
        (await BalanceAsync(s.Account, s.Poupanca)).ShouldBe(0);
        (await s.Http.DeleteAsync($"/api/v1/transfers/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Desfazer_a_exclusao_restaura_a_transferencia_identica_e_os_saldos()
    {
        var s = await NewUserAsync();
        var created = await CreateAsync(s, "20,00", "Reserva da Lia");
        var id = created.GetProperty("id").GetGuid();
        var batch = (await (await s.Http.DeleteAsync($"/api/v1/transfers/{id}")).JsonAsync()).GetProperty("deletionBatchId").GetGuid();

        var restore = await s.Http.PostAsync($"/api/v1/deletions/{batch}/restore", content: null);

        restore.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var back = await s.Http.GetAsync($"/api/v1/transfers/{id}");
        back.StatusCode.ShouldBe(HttpStatusCode.OK);
        var restored = await back.JsonAsync();
        foreach (var field in new[] { "id", "fromWalletId", "toWalletId", "amountCents", "date", "description", "notes", "createdAt" })
        {
            restored.GetProperty(field).GetRawText().ShouldBe(created.GetProperty(field).GetRawText(), field);
        }

        (await BalanceAsync(s.Account, s.Azul)).ShouldBe(98_000);
        (await BalanceAsync(s.Account, s.Poupanca)).ShouldBe(2_000);
    }

    [Fact]
    public async Task Excluir_transferencia_de_outro_Espaco_responde_404_e_ela_continua()
    {
        var lia = await NewUserAsync("Lia");
        var leo = await NewUserAsync("Léo");
        var id = (await CreateAsync(lia)).GetProperty("id").GetGuid();

        var response = await leo.Http.DeleteAsync($"/api/v1/transfers/{id}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.CodeAsync()).ShouldBe("not_found");
        (await lia.Http.GetAsync($"/api/v1/transfers/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
