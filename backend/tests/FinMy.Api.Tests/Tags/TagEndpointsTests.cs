using System.Net;
using System.Net.Http.Json;
using FinMy.Api.Tests.Entries;
using FinMy.Api.Tests.Support;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Common;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;
using FinMy.Domain.Tags;
using FinMy.Infrastructure.Context;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Dinheiro = FinMy.Domain.Monetary.Money;

namespace FinMy.Api.Tests.Tags;

/// <summary>Tags pela API (004 US1.2, US1.3, US5.1–US5.4; research R2 e R7).</summary>
public class TagEndpointsTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private const string Base = "/api/v1/tags";

    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?> { ["RateLimiting:Auth:PermitLimit"] = "100000" };

    private sealed record TagDto(Guid Id, string Name, string Color, bool IsSettlement, bool Archived, int Uses90d, bool InUse, long Version);

    private async Task<(HttpClient Http, TestAccount Account, Guid WalletId)> NewSpaceAsync(string name = "Lia")
    {
        using var anonymous = Factory.CreateClient();
        var account = await anonymous.SignUpAsync(name: name);
        var walletId = await Factory.SeedWalletAsync(account, "Azul");
        return (Factory.CreateClient().WithBearer(account.AccessToken), account, walletId);
    }

    private static async Task<TagDto> CreateAsync(HttpClient http, string name, bool isSettlement = false, string? color = null)
    {
        var response = await http.PostAsJsonAsync(Base, new { name, color, isSettlement });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<TagDto>(JsonDefaults.Options))!;
    }

    private static async Task<List<TagDto>> ListAsync(HttpClient http, string query = "") =>
        (await http.GetFromJsonAsync<List<TagDto>>($"{Base}{query}", JsonDefaults.Options))!;

    private static Task<HttpResponseMessage> PutAsync(HttpClient http, TagDto tag, bool? isSettlement = null, string? name = null) =>
        http.PutAsJsonAsync($"{Base}/{tag.Id}", new { name = name ?? tag.Name, color = tag.Color, isSettlement = isSettlement ?? tag.IsSettlement, version = tag.Version });

    /// <summary>Cria um lançamento direto no banco (a API de lançamentos só ganha <c>tagIds</c> na T003).</summary>
    private async Task<Guid> SeedEntryAsync(TestAccount account, Guid walletId, string description, DateOnly date, Guid? settlementTagId = null, params Guid[] tagIds)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentContext>().Populate(account.Session.User.Id, account.Session.Tenant.Id, Channel.App);
        var db = scope.ServiceProvider.GetRequiredService<FinMyDbContext>();

        var entry = Entry.Create(EntryKind.Expense, description, Dinheiro.FromCents(4000), date, walletId, null, null, null, date);
        entry.ChangeSettlementTag(settlementTagId);
        db.Add(entry);
        db.AddRange(tagIds.Select(t => new EntryTag(entry.Id, t)));
        await db.SaveChangesAsync();
        return entry.Id;
    }

    private async Task<string> StateAsync(TestAccount account, Guid entryId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentContext>().Populate(account.Session.User.Id, account.Session.Tenant.Id, Channel.App);
        var db = scope.ServiceProvider.GetRequiredService<FinMyDbContext>();
        var entry = await db.Set<Entry>().SingleAsync(e => e.Id == entryId);
        var links = await db.Set<EntryTag>().Where(l => l.EntryId == entryId).Select(l => l.TagId).ToListAsync();
        return Describe(entry.SettlementTagId, links);
    }

    private static string Describe(Guid? settlement, IEnumerable<Guid> links) => $"{settlement}|{string.Join(",", links.Order())}";

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task Cria_com_cor_padrao_da_paleta_e_lista_ordenada_por_nome()
    {
        var (http, _, _) = await NewSpaceAsync();

        var viagem = await CreateAsync(http, "Viagem");
        var bruna = await CreateAsync(http, "Bruna", isSettlement: true, color: "#00ACC1");

        viagem.Color.ShouldBe("#E53935");
        viagem.IsSettlement.ShouldBeFalse();
        viagem.Uses90d.ShouldBe(0);
        viagem.InUse.ShouldBeFalse();
        bruna.Color.ShouldBe("#00ACC1");
        bruna.IsSettlement.ShouldBeTrue();
        (await ListAsync(http)).Select(t => t.Name).ShouldBe(["Bruna", "Viagem"]);
    }

    [Fact]
    public async Task Nome_repetido_sem_diferenciar_caixa_e_acento_devolve_409_com_existingTagId()
    {
        var (http, _, _) = await NewSpaceAsync();
        var uber = await CreateAsync(http, "Café");

        var response = await http.PostAsJsonAsync(Base, new { name = " CAFE " });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var body = await response.JsonAsync();
        body.GetProperty("code").GetString().ShouldBe("tag.name_taken");
        body.GetProperty("existingTagId").GetGuid().ShouldBe(uber.Id);
    }

    [Fact]
    public async Task Lista_traz_uses90d_e_inUse_e_filtra_arquivadas()
    {
        var (http, account, walletId) = await NewSpaceAsync();
        var mercado = await CreateAsync(http, "Mercado");
        var carla = await CreateAsync(http, "Carla", isSettlement: true);
        var velha = await CreateAsync(http, "Velha");
        await SeedEntryAsync(account, walletId, "Compras", Today.AddDays(-10), null, mercado.Id);
        await SeedEntryAsync(account, walletId, "Compras antigas", Today.AddDays(-200), null, mercado.Id);
        await SeedEntryAsync(account, walletId, "Almoço Carla", Today.AddDays(-1), carla.Id);

        (await http.PostAsync($"{Base}/{velha.Id}/archive", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var active = (await ListAsync(http)).ToDictionary(t => t.Name);
        active.Keys.ShouldBe(["Carla", "Mercado"], ignoreOrder: true);
        active["Mercado"].Uses90d.ShouldBe(1);
        active["Mercado"].InUse.ShouldBeTrue();
        active["Carla"].Uses90d.ShouldBe(1);
        (await ListAsync(http, "?archived=true")).Select(t => t.Name).ShouldBe(["Velha"]);
        (await ListAsync(http, "?archived=all")).Count.ShouldBe(3);
        (await http.GetAsync($"{Base}?archived=talvez")).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Edita_nome_e_cor_com_version_e_recusa_nome_de_outra_tag()
    {
        var (http, _, _) = await NewSpaceAsync();
        var mercado = await CreateAsync(http, "Mercado");
        await CreateAsync(http, "Feira");

        var renamed = await PutAsync(http, mercado, name: "Supermercado");
        renamed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = (await renamed.Content.ReadFromJsonAsync<TagDto>(JsonDefaults.Options))!;
        updated.Name.ShouldBe("Supermercado");
        updated.Version.ShouldNotBe(mercado.Version);

        (await PutAsync(http, updated, name: "feira")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var stale = await PutAsync(http, mercado, name: "Outro");
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await stale.CodeAsync()).ShouldBe("concurrency_conflict");
    }

    [Fact]
    public async Task Marcar_como_acerto_move_ligacoes_para_a_coluna_e_desmarcar_volta()
    {
        var (http, account, walletId) = await NewSpaceAsync();
        var carla = await CreateAsync(http, "Carla");
        var entryId = await SeedEntryAsync(account, walletId, "Cinema", Today, null, carla.Id);

        var marked = await PutAsync(http, carla, isSettlement: true);
        marked.StatusCode.ShouldBe(HttpStatusCode.OK);
        var markedTag = (await marked.Content.ReadFromJsonAsync<TagDto>(JsonDefaults.Options))!;
        markedTag.IsSettlement.ShouldBeTrue();
        (await StateAsync(account, entryId)).ShouldBe(Describe(carla.Id, []));

        (await PutAsync(http, markedTag, isSettlement: false)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StateAsync(account, entryId)).ShouldBe(Describe(null, [carla.Id]));
    }

    [Fact]
    public async Task Marcar_como_acerto_com_outra_tag_de_acerto_no_mesmo_lancamento_devolve_409_e_lista_os_lancamentos()
    {
        var (http, account, walletId) = await NewSpaceAsync();
        var bruna = await CreateAsync(http, "Bruna", isSettlement: true);
        var carla = await CreateAsync(http, "Carla");
        var entryId = await SeedEntryAsync(account, walletId, "Jantar", new DateOnly(2026, 3, 5), bruna.Id, carla.Id);

        var response = await PutAsync(http, carla, isSettlement: true);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var body = await response.JsonAsync();
        body.GetProperty("code").GetString().ShouldBe("tag.settlement_conflict");
        var conflict = body.GetProperty("conflicts").EnumerateArray().Single();
        conflict.GetProperty("type").GetString().ShouldBe("entry");
        conflict.GetProperty("id").GetGuid().ShouldBe(entryId);
        conflict.GetProperty("description").GetString().ShouldBe("Jantar");
        conflict.GetProperty("date").GetString().ShouldBe("2026-03-05");
        (await ListAsync(http)).Single(t => t.Name == "Carla").IsSettlement.ShouldBeFalse();
        (await StateAsync(account, entryId)).ShouldBe(Describe(bruna.Id, [carla.Id]));
    }

    [Fact]
    public async Task Exclui_so_sem_uso_e_com_uso_manda_arquivar_e_desarquivar_volta()
    {
        var (http, account, walletId) = await NewSpaceAsync();
        var usada = await CreateAsync(http, "Usada");
        var livre = await CreateAsync(http, "Livre");
        await SeedEntryAsync(account, walletId, "Algo", Today, null, usada.Id);

        var blocked = await http.DeleteAsync($"{Base}/{usada.Id}");
        blocked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await blocked.CodeAsync()).ShouldBe("tag.in_use_archive_instead");

        (await http.DeleteAsync($"{Base}/{livre.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ListAsync(http, "?archived=all")).Select(t => t.Name).ShouldBe(["Usada"]);

        var archived = await http.PostAsync($"{Base}/{usada.Id}/archive", null);
        (await archived.Content.ReadFromJsonAsync<TagDto>(JsonDefaults.Options))!.Archived.ShouldBeTrue();
        var back = await http.PostAsync($"{Base}/{usada.Id}/unarchive", null);
        (await back.Content.ReadFromJsonAsync<TagDto>(JsonDefaults.Options))!.Archived.ShouldBeFalse();
        (await http.DeleteAsync($"{Base}/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Junta_aplica_o_destino_sem_duplicar_e_exclui_a_origem()
    {
        var (http, account, walletId) = await NewSpaceAsync();
        var mercado = await CreateAsync(http, "Mercado");
        var super = await CreateAsync(http, "Supermercado");
        var soSuper = await SeedEntryAsync(account, walletId, "Feira", Today, null, super.Id);
        var ambas = await SeedEntryAsync(account, walletId, "Compras", Today, null, super.Id, mercado.Id);

        var response = await http.PostAsJsonAsync($"{Base}/{super.Id}/merge", new { targetTagId = mercado.Id });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var merged = (await response.Content.ReadFromJsonAsync<TagDto>(JsonDefaults.Options))!;
        merged.Id.ShouldBe(mercado.Id);
        merged.Uses90d.ShouldBe(2);
        (await StateAsync(account, soSuper)).ShouldBe(Describe(null, [mercado.Id]));
        (await StateAsync(account, ambas)).ShouldBe(Describe(null, [mercado.Id]));
        (await ListAsync(http, "?archived=all")).Select(t => t.Name).ShouldBe(["Mercado"]);
    }

    [Fact]
    public async Task Junta_recusa_quando_algum_lancamento_ficaria_com_duas_tags_de_acerto()
    {
        var (http, account, walletId) = await NewSpaceAsync();
        var bruna = await CreateAsync(http, "Bruna", isSettlement: true);
        var duda = await CreateAsync(http, "Duda", isSettlement: true);
        var comum = await CreateAsync(http, "Passeio");
        var entryId = await SeedEntryAsync(account, walletId, "Ingresso", Today, bruna.Id, comum.Id);

        // "Passeio" (comum) em "Duda" (de acerto): o lançamento já tem a Bruna.
        var response = await http.PostAsJsonAsync($"{Base}/{comum.Id}/merge", new { targetTagId = duda.Id });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var body = await response.JsonAsync();
        body.GetProperty("code").GetString().ShouldBe("tag.settlement_conflict");
        body.GetProperty("conflicts").EnumerateArray().Single().GetProperty("id").GetGuid().ShouldBe(entryId);
        (await ListAsync(http)).Count.ShouldBe(3);
        (await StateAsync(account, entryId)).ShouldBe(Describe(bruna.Id, [comum.Id]));
    }

    [Fact]
    public async Task Junta_na_mesma_tag_ou_em_tag_inexistente_e_recusada()
    {
        var (http, _, _) = await NewSpaceAsync();
        var mercado = await CreateAsync(http, "Mercado");

        var self = await http.PostAsJsonAsync($"{Base}/{mercado.Id}/merge", new { targetTagId = mercado.Id });
        self.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await self.CodeAsync()).ShouldBe("tag.merge_into_self");
        (await http.PostAsJsonAsync($"{Base}/{mercado.Id}/merge", new { targetTagId = Guid.NewGuid() })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Tag_de_outro_Espaco_e_invisivel()
    {
        var (lia, _, _) = await NewSpaceAsync("Lia");
        var (leo, _, _) = await NewSpaceAsync("Léo");
        var tag = await CreateAsync(lia, "Mercado");

        (await ListAsync(leo)).ShouldBeEmpty();
        (await PutAsync(leo, tag)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await leo.PostAsync($"{Base}/{tag.Id}/archive", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CreateAsync(leo, "Mercado")).Id.ShouldNotBe(tag.Id);
    }
}
