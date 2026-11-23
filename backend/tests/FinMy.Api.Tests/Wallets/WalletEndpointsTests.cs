using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FinMy.Api.Idempotency;
using FinMy.Api.Tests.Support;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Common;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;
using FinMy.Domain.Transfers;
using FinMy.Infrastructure.Context;
using FinMy.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace FinMy.Api.Tests.Wallets;

/// <summary>Carteiras pela API (002 US1.1–US1.4, research R5/R6/R11 e códigos de erro de contracts/openapi.yaml).</summary>
public class WalletEndpointsTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private const string Base = "/api/v1/wallets";
    private static readonly DateOnly March1 = new(2026, 3, 1);

    // Cada teste abre uma conta nova; o limite por IP do /auth não deve atrapalhar.
    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?> { ["RateLimiting:Auth:PermitLimit"] = "100000" };

    private sealed record WalletDto(
        Guid Id, string Name, string Type, string Color, long InitialBalanceCents, DateOnly InitialBalanceDate,
        bool IsPrimary, bool Archived, long CurrentBalanceCents, bool HasEntries, long Version);

    private sealed record ProblemDto(string? Code, int Status, Dictionary<string, string[]>? Errors);

    private async Task<(HttpClient Http, TestAccount Account)> NewSpaceAsync(string name = "Lia")
    {
        using var anonymous = Factory.CreateClient();
        var account = await anonymous.SignUpAsync(name: name);
        return (Factory.CreateClient().WithBearer(account.AccessToken), account);
    }

    private static object Body(string name, string balance = "0", string type = "checking", string color = "#1E88E5", string date = "2026-03-01") =>
        new { name, type, color, initialBalance = balance, initialBalanceDate = date };

    private static async Task<WalletDto> ReadAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<WalletDto>(JsonDefaults.Options))!;

    private static async Task<ProblemDto> ProblemAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemDto>(JsonDefaults.Options))!;

    private static async Task<WalletDto> CreateAsync(HttpClient http, string name, string balance = "0", string type = "checking")
    {
        var response = await http.PostAsJsonAsync(Base, Body(name, balance, type));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await ReadAsync(response);
    }

    private static async Task<List<WalletDto>> ListAsync(HttpClient http, string query = "") =>
        (await http.GetFromJsonAsync<List<WalletDto>>($"{Base}{query}", JsonDefaults.Options))!;

    private static Task<HttpResponseMessage> PutAsync(HttpClient http, Guid id, object body) => http.PutAsJsonAsync($"{Base}/{id}", body);

    private static Task<HttpResponseMessage> PostActionAsync(HttpClient http, Guid id, string action, object? body = null) =>
        http.PostAsJsonAsync($"{Base}/{id}/{action}", body);

    private async Task SeedAsync(TestAccount account, Func<FinMyDbContext, Task> seed)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentContext>().Populate(account.Session.User.Id, account.Session.Tenant.Id, Channel.App);
        var db = scope.ServiceProvider.GetRequiredService<FinMyDbContext>();
        await seed(db);
        await db.SaveChangesAsync();
    }

    private static Entry NewEntry(EntryKind kind, long cents, Guid walletId, EntryStatus status = EntryStatus.Settled) =>
        Entry.Create(kind, "Mercado", Money.FromCents(cents), March1, walletId, status, null, null, March1);

    // --- US1.1 ---

    [Fact]
    public async Task US1_1_primeira_carteira_ativa_vira_principal_com_saldo_inicial_como_saldo_atual()
    {
        var (lia, _) = await NewSpaceAsync();

        var response = await lia.PostAsJsonAsync(Base, Body("Azul", "500,00"));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location!.ToString().ShouldStartWith($"{Base}/");
        var azul = await ReadAsync(response);
        azul.Name.ShouldBe("Azul");
        azul.Type.ShouldBe("checking");
        azul.Color.ShouldBe("#1E88E5");
        azul.InitialBalanceCents.ShouldBe(50000);
        azul.InitialBalanceDate.ShouldBe(March1);
        azul.CurrentBalanceCents.ShouldBe(50000);
        azul.IsPrimary.ShouldBeTrue();
        azul.Archived.ShouldBeFalse();
        azul.HasEntries.ShouldBeFalse();
        azul.Version.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task US1_1_segunda_carteira_nao_e_principal_e_a_lista_traz_os_saldos()
    {
        var (lia, _) = await NewSpaceAsync();
        var azul = await CreateAsync(lia, "Azul", "500,00");
        var verde = await CreateAsync(lia, "Verde", "1.234,56", "savings");

        verde.IsPrimary.ShouldBeFalse();

        var lista = await ListAsync(lia);
        lista.Select(w => w.Id).ShouldBe([azul.Id, verde.Id]);
        lista.Select(w => w.CurrentBalanceCents).ShouldBe([50000L, 123456L]);
        lista.Single(w => w.IsPrimary).Id.ShouldBe(azul.Id);
    }

    [Theory]
    [InlineData("-150,00", -15000)]
    [InlineData("0", 0)]
    [InlineData("100+50,5", 15050)]
    [InlineData("=-(10+5)", -1500)]
    public async Task Saldo_inicial_aceita_expressao_zero_e_negativo(string text, long cents)
    {
        var (lia, _) = await NewSpaceAsync();

        var wallet = await CreateAsync(lia, "Dinheiro", text, "cash");

        wallet.InitialBalanceCents.ShouldBe(cents);
        wallet.CurrentBalanceCents.ShouldBe(cents);
        wallet.Type.ShouldBe("cash");
    }

    [Theory]
    [InlineData("checking")]
    [InlineData("digital")]
    [InlineData("savings")]
    [InlineData("cash")]
    [InlineData("meal_voucher")]
    [InlineData("other")]
    public async Task Todos_os_tipos_do_contrato_sao_aceitos_e_devolvidos_em_snake_case(string type)
    {
        var (lia, _) = await NewSpaceAsync();

        (await CreateAsync(lia, "VR/VA", "10", type)).Type.ShouldBe(type);
    }

    [Fact]
    public async Task Saldo_atual_soma_entradas_recebidas_saidas_pagas_e_transferencias()
    {
        var (lia, account) = await NewSpaceAsync();
        var azul = await CreateAsync(lia, "Azul", "500,00");
        var verde = await CreateAsync(lia, "Verde", "100,00");

        await SeedAsync(account, db =>
        {
            db.AddRange(
                NewEntry(EntryKind.Income, 20000, azul.Id),
                NewEntry(EntryKind.Expense, 4499, azul.Id),
                NewEntry(EntryKind.Expense, 99900, azul.Id, EntryStatus.Planned), // prevista não conta
                NewEntry(EntryKind.Income, 5000, verde.Id),
                Transfer.Create(azul.Id, verde.Id, Money.FromCents(3000), March1, null, null));
            return Task.CompletedTask;
        });

        var lista = await ListAsync(lia);
        lista.Single(w => w.Id == azul.Id).CurrentBalanceCents.ShouldBe(50000 + 20000 - 4499 - 3000);
        lista.Single(w => w.Id == verde.Id).CurrentBalanceCents.ShouldBe(10000 + 5000 + 3000);
        lista.ShouldAllBe(w => w.HasEntries);
        (await lia.GetFromJsonAsync<WalletDto>($"{Base}/{azul.Id}", JsonDefaults.Options))!.CurrentBalanceCents.ShouldBe(62501);
    }

    // --- US1.2 ---

    [Theory]
    [InlineData("azul")]
    [InlineData("AZUL")]
    [InlineData("  Azúl ")]
    public async Task US1_2_nome_repetido_sem_diferenciar_caixa_e_acento_e_409(string name)
    {
        var (lia, _) = await NewSpaceAsync();
        await CreateAsync(lia, "Azul");

        var response = await lia.PostAsJsonAsync(Base, Body(name));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await ProblemAsync(response);
        problem.Code.ShouldBe("wallet.name_taken");
        problem.Errors!.ShouldContainKey("name");
        (await ListAsync(lia)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Espacos_diferentes_podem_ter_carteiras_com_o_mesmo_nome()
    {
        var (lia, _) = await NewSpaceAsync("Lia");
        var (leo, _) = await NewSpaceAsync("Léo");

        await CreateAsync(lia, "Azul");
        var deLeo = await CreateAsync(leo, "Azul");

        deLeo.IsPrimary.ShouldBeTrue();
        (await ListAsync(leo)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Carteira_arquivada_continua_ocupando_o_nome()
    {
        var (lia, _) = await NewSpaceAsync();
        var azul = await CreateAsync(lia, "Azul");
        await CreateAsync(lia, "Verde");
        (await PostActionAsync(lia, azul.Id, "archive", new { newPrimaryWalletId = (await ListAsync(lia)).Single(w => w.Name == "Verde").Id }))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await lia.PostAsJsonAsync(Base, Body("azul"));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemAsync(response)).Code.ShouldBe("wallet.name_taken");
    }

    // --- US1.3 ---

    [Fact]
    public async Task US1_3_carteira_com_lancamentos_nao_pode_ser_excluida_so_arquivada()
    {
        var (lia, account) = await NewSpaceAsync();
        var azul = await CreateAsync(lia, "Azul", "500,00");
        await SeedAsync(account, db =>
        {
            db.Add(NewEntry(EntryKind.Expense, 4499, azul.Id));
            return Task.CompletedTask;
        });

        (await ListAsync(lia)).Single().HasEntries.ShouldBeTrue();

        var response = await lia.DeleteAsync($"{Base}/{azul.Id}");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemAsync(response)).Code.ShouldBe("wallet.has_entries_archive_instead");
        (await lia.GetAsync($"{Base}/{azul.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Transferencia_tambem_impede_excluir_nas_duas_pontas()
    {
        var (lia, account) = await NewSpaceAsync();
        var azul = await CreateAsync(lia, "Azul");
        var verde = await CreateAsync(lia, "Verde");
        await SeedAsync(account, db =>
        {
            db.Add(Transfer.Create(azul.Id, verde.Id, Money.FromCents(1000), March1, null, null));
            return Task.CompletedTask;
        });

        (await lia.DeleteAsync($"{Base}/{verde.Id}")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await lia.DeleteAsync($"{Base}/{azul.Id}?newPrimaryWalletId={verde.Id}")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Lancamento_excluido_e_ainda_nao_expurgado_continua_impedindo_a_exclusao()
    {
        var (lia, account) = await NewSpaceAsync();
        var azul = await CreateAsync(lia, "Azul");
        var entry = NewEntry(EntryKind.Expense, 1000, azul.Id);
        await SeedAsync(account, db =>
        {
            db.Add(entry);
            return Task.CompletedTask;
        });
        await SeedAsync(account, async db =>
        {
            db.Remove(await db.Set<Entry>().FindAsync(entry.Id) ?? throw new InvalidOperationException());
        });

        (await ListAsync(lia)).Single().HasEntries.ShouldBeTrue();
        (await lia.DeleteAsync($"{Base}/{azul.Id}")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Carteira_sem_lancamentos_e_excluida_e_libera_o_nome()
    {
        var (lia, _) = await NewSpaceAsync();
        var azul = await CreateAsync(lia, "Azul");
        var verde = await CreateAsync(lia, "Verde");

        var response = await lia.DeleteAsync($"{Base}/{verde.Id}");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await lia.GetAsync($"{Base}/{verde.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ListAsync(lia, "?archived=all")).Select(w => w.Id).ShouldBe([azul.Id]);
        (await CreateAsync(lia, "verde")).IsPrimary.ShouldBeFalse();
    }

    [Fact]
    public async Task Excluir_a_principal_exige_a_nova_principal_quando_ha_outra_ativa()
    {
        var (lia, _) = await NewSpaceAsync();
        var azul = await CreateAsync(lia, "Azul");
        var verde = await CreateAsync(lia, "Verde");

        var semNova = await lia.DeleteAsync($"{Base}/{azul.Id}");
        semNova.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ProblemAsync(semNova)).Code.ShouldBe("wallet.new_primary_required");

        (await lia.DeleteAsync($"{Base}/{azul.Id}?newPrimaryWalletId={azul.Id}")).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await lia.DeleteAsync($"{Base}/{azul.Id}?newPrimaryWalletId={Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await lia.DeleteAsync($"{Base}/{azul.Id}?newPrimaryWalletId={verde.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var lista = await ListAsync(lia);
        lista.Single().Id.ShouldBe(verde.Id);
        lista.Single().IsPrimary.ShouldBeTrue();
    }

    [Fact]
    public async Task Excluir_a_unica_carteira_ativa_e_permitido_sem_nova_principal()
    {
        var (lia, _) = await NewSpaceAsync();
        var azul = await CreateAsync(lia, "Azul");

        (await lia.DeleteAsync($"{Base}/{azul.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ListAsync(lia, "?archived=all")).ShouldBeEmpty();
        (await CreateAsync(lia, "Azul")).IsPrimary.ShouldBeTrue();
    }

    // --- US1.4 ---

    [Fact]
    public async Task US1_4_arquivar_a_principal_pede_outra_carteira_ativa_como_principal()
    {
        var (lia, _) = await NewSpaceAsync();
        var azul = await CreateAsync(lia, "Azul");
        var verde = await CreateAsync(lia, "Verde");

        var semNova = await PostActionAsync(lia, azul.Id, "archive");
        semNova.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ProblemAsync(semNova)).Code.ShouldBe("wallet.new_primary_required");
        (await ListAsync(lia)).Single(w => w.Id == azul.Id).Archived.ShouldBeFalse();

        var arquivada = await PostActionAsync(lia, azul.Id, "archive", new { newPrimaryWalletId = verde.Id });

        arquivada.StatusCode.ShouldBe(HttpStatusCode.OK);
        var azulArquivada = await ReadAsync(arquivada);
        azulArquivada.Archived.ShouldBeTrue();
        azulArquivada.IsPrimary.ShouldBeFalse();
        var ativas = await ListAsync(lia);
        ativas.Single().Id.ShouldBe(verde.Id);
        ativas.Single().IsPrimary.ShouldBeTrue();
    }

    [Fact]
    public async Task US1_4_a_unica_carteira_ativa_nao_pode_ser_arquivada()
    {
        var (lia, _) = await NewSpaceAsync();
        var azul = await CreateAsync(lia, "Azul");

        var response = await PostActionAsync(lia, azul.Id, "archive", new { newPrimaryWalletId = azul.Id });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ProblemAsync(response)).Code.ShouldBe("wallet.last_active_cannot_be_archived");
        (await PostActionAsync(lia, azul.Id, "archive")).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ListAsync(lia)).Single().Archived.ShouldBeFalse();
    }

    [Fact]
    public async Task Nova_principal_invalida_e_a_mesma_ou_arquivada_ou_inexistente()
    {
        var (lia, _) = await NewSpaceAsync();
        var azul = await CreateAsync(lia, "Azul");
        var verde = await CreateAsync(lia, "Verde");
        var carla = await CreateAsync(lia, "Carla");
        (await PostActionAsync(lia, carla.Id, "archive")).StatusCode.ShouldBe(HttpStatusCode.OK);

        foreach (var invalida in new[] { azul.Id, carla.Id })
        {
            var response = await PostActionAsync(lia, azul.Id, "archive", new { newPrimaryWalletId = invalida });
            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            (await ProblemAsync(response)).Code.ShouldBe("wallet.invalid_new_primary");
        }

        (await PostActionAsync(lia, azul.Id, "archive", new { newPrimaryWalletId = Guid.NewGuid() })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ListAsync(lia)).Single(w => w.Id == azul.Id).IsPrimary.ShouldBeTrue();
        (await ListAsync(lia)).Single(w => w.Id == verde.Id).IsPrimary.ShouldBeFalse();
    }

    [Fact]
    public async Task Arquivar_carteira_que_nao_e_principal_nao_pede_nova_principal()
    {
        var (lia, _) = await NewSpaceAsync();
        var azul = await CreateAsync(lia, "Azul");
        var verde = await CreateAsync(lia, "Verde");

        var response = await PostActionAsync(lia, verde.Id, "archive");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(response)).Archived.ShouldBeTrue();
        (await ListAsync(lia)).Single().Id.ShouldBe(azul.Id);
    }

    [Fact]
    public async Task Arquivar_aceita_corpo_vazio_sem_tipo_de_conteudo()
    {
        var (lia, _) = await NewSpaceAsync();
        await CreateAsync(lia, "Azul");
        var verde = await CreateAsync(lia, "Verde");

        var response = await lia.PostAsync($"{Base}/{verde.Id}/archive", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // --- filtro archived ---

    [Fact]
    public async Task Filtro_archived_separa_ativas_arquivadas_e_todas()
    {
        var (lia, _) = await NewSpaceAsync();
        var azul = await CreateAsync(lia, "Azul");
        var verde = await CreateAsync(lia, "Verde");
        await PostActionAsync(lia, verde.Id, "archive");

        (await ListAsync(lia)).Select(w => w.Id).ShouldBe([azul.Id]);
        (await ListAsync(lia, "?archived=false")).Select(w => w.Id).ShouldBe([azul.Id]);
        (await ListAsync(lia, "?archived=true")).Select(w => w.Id).ShouldBe([verde.Id]);
        (await ListAsync(lia, "?archived=all")).Select(w => w.Id).ShouldBe([azul.Id, verde.Id]);

        var invalido = await lia.GetAsync($"{Base}?archived=talvez");
        invalido.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    // --- principal ---

    [Fact]
    public async Task Tornar_principal_troca_a_principal_e_mantem_exatamente_uma()
    {
        var (lia, _) = await NewSpaceAsync();
        var azul = await CreateAsync(lia, "Azul");
        var verde = await CreateAsync(lia, "Verde");

        var response = await PostActionAsync(lia, verde.Id, "make-primary");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(response)).IsPrimary.ShouldBeTrue();
        var lista = await ListAsync(lia);
        lista.Single(w => w.IsPrimary).Id.ShouldBe(verde.Id);
        lista.Single(w => w.Id == azul.Id).IsPrimary.ShouldBeFalse();

        // Repetir é inofensivo.
        (await PostActionAsync(lia, verde.Id, "make-primary")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ListAsync(lia)).Count(w => w.IsPrimary).ShouldBe(1);
    }

    [Fact]
    public async Task Carteira_arquivada_nao_pode_ser_principal()
    {
        var (lia, _) = await NewSpaceAsync();
        await CreateAsync(lia, "Azul");
        var verde = await CreateAsync(lia, "Verde");
        await PostActionAsync(lia, verde.Id, "archive");

        var response = await PostActionAsync(lia, verde.Id, "make-primary");

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ProblemAsync(response)).Code.ShouldBe("wallet.archived");
    }

    [Fact]
    public async Task Desarquivar_nao_torna_principal_exceto_quando_nao_ha_nenhuma_ativa()
    {
        var (lia, _) = await NewSpaceAsync();
        var azul = await CreateAsync(lia, "Azul");
        var verde = await CreateAsync(lia, "Verde");
        await PostActionAsync(lia, verde.Id, "archive");

        var desarquivada = await ReadAsync(await PostActionAsync(lia, verde.Id, "unarchive"));
        desarquivada.Archived.ShouldBeFalse();
        desarquivada.IsPrimary.ShouldBeFalse();

        // Sem nenhuma ativa (a principal foi excluída), a desarquivada assume a principal.
        await PostActionAsync(lia, verde.Id, "archive");
        (await lia.DeleteAsync($"{Base}/{azul.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var unica = await ReadAsync(await PostActionAsync(lia, verde.Id, "unarchive"));
        unica.IsPrimary.ShouldBeTrue();
    }

    // --- edição ---

    [Fact]
    public async Task Editar_altera_os_campos_e_devolve_nova_versao()
    {
        var (lia, account) = await NewSpaceAsync();
        var azul = await CreateAsync(lia, "Azul", "500,00");
        await SeedAsync(account, db =>
        {
            db.Add(NewEntry(EntryKind.Income, 1000, azul.Id));
            return Task.CompletedTask;
        });

        var response = await PutAsync(lia, azul.Id, new
        {
            name = "Banco Azul",
            type = "digital",
            color = "#43A047",
            initialBalance = "-20,50",
            initialBalanceDate = "2026-02-01",
            version = azul.Version,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var editada = await ReadAsync(response);
        editada.Name.ShouldBe("Banco Azul");
        editada.Type.ShouldBe("digital");
        editada.Color.ShouldBe("#43A047");
        editada.InitialBalanceCents.ShouldBe(-2050);
        editada.InitialBalanceDate.ShouldBe(new DateOnly(2026, 2, 1));
        editada.CurrentBalanceCents.ShouldBe(-2050 + 1000);
        editada.IsPrimary.ShouldBeTrue();
        editada.Archived.ShouldBeFalse();
        editada.Version.ShouldNotBe(azul.Version);
    }

    [Fact]
    public async Task Editar_com_versao_antiga_e_409_concurrency_conflict()
    {
        var (lia, _) = await NewSpaceAsync();
        var azul = await CreateAsync(lia, "Azul");
        var corpo = new { name = "Azul 2", type = "checking", color = "#1E88E5", initialBalance = "0", initialBalanceDate = "2026-03-01", version = azul.Version };

        (await PutAsync(lia, azul.Id, corpo)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var segunda = await PutAsync(lia, azul.Id, corpo with { });

        segunda.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemAsync(segunda)).Code.ShouldBe("concurrency_conflict");
        (await lia.GetFromJsonAsync<WalletDto>($"{Base}/{azul.Id}", JsonDefaults.Options))!.Name.ShouldBe("Azul 2");
    }

    [Fact]
    public async Task Editar_sem_versao_e_422()
    {
        var (lia, _) = await NewSpaceAsync();
        var azul = await CreateAsync(lia, "Azul");

        var response = await PutAsync(lia, azul.Id, Body("Azul 2"));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ProblemAsync(response)).Errors!.ShouldContainKey("version");
    }

    [Fact]
    public async Task Editar_para_o_nome_de_outra_carteira_e_409_e_manter_o_proprio_nome_e_ok()
    {
        var (lia, _) = await NewSpaceAsync();
        var azul = await CreateAsync(lia, "Azul");
        var verde = await CreateAsync(lia, "Verde");

        var conflito = await PutAsync(lia, verde.Id, new { name = "AZUL", type = "checking", color = "#1E88E5", initialBalance = "0", initialBalanceDate = "2026-03-01", version = verde.Version });
        conflito.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ProblemAsync(conflito)).Code.ShouldBe("wallet.name_taken");

        var proprio = await PutAsync(lia, azul.Id, new { name = "azul", type = "checking", color = "#1E88E5", initialBalance = "0", initialBalanceDate = "2026-03-01", version = azul.Version });
        proprio.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(proprio)).Name.ShouldBe("azul");
    }

    // --- validação ---

    public static TheoryData<string, object, string> Invalidos => new()
    {
        { "name", new { name = "", type = "checking", color = "#1E88E5", initialBalance = "0", initialBalanceDate = "2026-03-01" }, "name" },
        { "name", new { name = new string('a', 41), type = "checking", color = "#1E88E5", initialBalance = "0", initialBalanceDate = "2026-03-01" }, "name" },
        { "type", new { name = "Azul", type = "poupanca", color = "#1E88E5", initialBalance = "0", initialBalanceDate = "2026-03-01" }, "type" },
        { "color", new { name = "Azul", type = "checking", color = "azul", initialBalance = "0", initialBalanceDate = "2026-03-01" }, "color" },
        { "color", new { name = "Azul", type = "checking", color = "#12345", initialBalance = "0", initialBalanceDate = "2026-03-01" }, "color" },
        { "balance", new { name = "Azul", type = "checking", color = "#1E88E5", initialBalance = "1/0", initialBalanceDate = "2026-03-01" }, "initialBalance" },
        { "balance", new { name = "Azul", type = "checking", color = "#1E88E5", initialBalance = "abc", initialBalanceDate = "2026-03-01" }, "initialBalance" },
        { "balance", new { name = "Azul", type = "checking", color = "#1E88E5", initialBalance = "10000000", initialBalanceDate = "2026-03-01" }, "initialBalance" },
        { "balance", new { name = "Azul", type = "checking", color = "#1E88E5", initialBalance = "-10000000", initialBalanceDate = "2026-03-01" }, "initialBalance" },
        { "balance", new { name = "Azul", type = "checking", color = "#1E88E5", initialBalanceDate = "2026-03-01" }, "initialBalance" },
        { "date", new { name = "Azul", type = "checking", color = "#1E88E5", initialBalance = "0" }, "initialBalanceDate" },
        { "date", new { name = "Azul", type = "checking", color = "#1E88E5", initialBalance = "0", initialBalanceDate = "1999-12-31" }, "initialBalanceDate" },
        { "date", new { name = "Azul", type = "checking", color = "#1E88E5", initialBalance = "0", initialBalanceDate = "2101-01-01" }, "initialBalanceDate" },
    };

    [Theory]
    [MemberData(nameof(Invalidos))]
    public async Task Dados_invalidos_na_criacao_sao_422_com_o_campo(string caso, object body, string field)
    {
        _ = caso;
        var (lia, _) = await NewSpaceAsync();

        var response = await lia.PostAsJsonAsync(Base, body);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await ProblemAsync(response);
        problem.Code.ShouldBe("validation_failed");
        problem.Errors!.ShouldContainKey(field);
        (await ListAsync(lia, "?archived=all")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Erro_de_valor_usa_as_mensagens_do_campo_de_valor()
    {
        var (lia, _) = await NewSpaceAsync();

        var response = await lia.PostAsJsonAsync(Base, Body("Azul", "1/0"));

        (await response.Content.ReadAsStringAsync()).ShouldContain("Divisão por zero");
    }

    [Fact]
    public async Task Dados_invalidos_na_edicao_sao_422()
    {
        var (lia, _) = await NewSpaceAsync();
        var azul = await CreateAsync(lia, "Azul");

        var response = await PutAsync(lia, azul.Id, new { name = "", type = "checking", color = "#1E88E5", initialBalance = "0", initialBalanceDate = "2026-03-01", version = azul.Version });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await lia.GetFromJsonAsync<WalletDto>($"{Base}/{azul.Id}", JsonDefaults.Options))!.Name.ShouldBe("Azul");
    }

    // --- 404, auth e isolamento ---

    [Fact]
    public async Task Carteira_inexistente_ou_de_outro_Espaco_e_404_not_found_em_todas_as_operacoes()
    {
        var (lia, _) = await NewSpaceAsync("Lia");
        var (leo, _) = await NewSpaceAsync("Léo");
        var deLia = await CreateAsync(lia, "Azul");
        var corpoPut = new { name = "X", type = "checking", color = "#1E88E5", initialBalance = "0", initialBalanceDate = "2026-03-01", version = deLia.Version };

        foreach (var id in new[] { Guid.NewGuid(), deLia.Id })
        {
            var respostas = new[]
            {
                await leo.GetAsync($"{Base}/{id}"),
                await PutAsync(leo, id, corpoPut),
                await leo.DeleteAsync($"{Base}/{id}"),
                await PostActionAsync(leo, id, "make-primary"),
                await PostActionAsync(leo, id, "archive"),
                await PostActionAsync(leo, id, "unarchive"),
            };

            foreach (var response in respostas)
            {
                response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
                (await ProblemAsync(response)).Code.ShouldBe("not_found");
            }
        }

        (await ListAsync(leo, "?archived=all")).ShouldBeEmpty();
        (await ReadAsync(await lia.GetAsync($"{Base}/{deLia.Id}"))).Name.ShouldBe("Azul");
    }

    [Fact]
    public async Task Nova_principal_de_outro_Espaco_e_404()
    {
        var (lia, _) = await NewSpaceAsync("Lia");
        var (leo, _) = await NewSpaceAsync("Léo");
        var azul = await CreateAsync(lia, "Azul");
        await CreateAsync(lia, "Verde");
        var deLeo = await CreateAsync(leo, "Azul");

        (await PostActionAsync(lia, azul.Id, "archive", new { newPrimaryWalletId = deLeo.Id })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Sem_token_todas_as_rotas_respondem_401()
    {
        using var anonimo = Factory.CreateClient();
        var id = Guid.NewGuid();

        (await anonimo.GetAsync(Base)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonimo.PostAsJsonAsync(Base, Body("Azul"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonimo.GetAsync($"{Base}/{id}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonimo.DeleteAsync($"{Base}/{id}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonimo.PostAsync($"{Base}/{id}/make-primary", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // --- idempotência ---

    [Fact]
    public async Task Post_com_a_mesma_Idempotency_Key_nao_duplica_a_carteira()
    {
        var (lia, _) = await NewSpaceAsync();
        var key = Guid.NewGuid().ToString();

        HttpRequestMessage Request() => new(HttpMethod.Post, Base)
        {
            Content = new StringContent(JsonSerializer.Serialize(Body("Azul", "500,00")), Encoding.UTF8, "application/json"),
            Headers = { { IdempotencyFilter.HeaderName, key } },
        };

        var primeira = await lia.SendAsync(Request());
        var segunda = await lia.SendAsync(Request());

        primeira.StatusCode.ShouldBe(HttpStatusCode.Created);
        segunda.StatusCode.ShouldBe(HttpStatusCode.Created);
        segunda.Headers.GetValues(IdempotencyFilter.ReplayedHeaderName).ShouldBe(["true"]);
        (await ReadAsync(segunda)).Id.ShouldBe((await ReadAsync(primeira)).Id);
        (await ListAsync(lia)).Count.ShouldBe(1);
    }
}
