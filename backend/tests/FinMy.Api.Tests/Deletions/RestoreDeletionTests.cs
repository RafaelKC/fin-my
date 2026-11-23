using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Api.Tests.Support;
using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Common;
using FinMy.Infrastructure.Context;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinMy.Api.Tests.Deletions;

/// <summary>
/// <c>POST /api/v1/deletions/{batchId}/restore</c> (RT-09, research R3) com as sondas <c>ProbeItem</c>:
/// restaurar, expirado, outro Espaço e duas vezes, mais o conflito de índice único (409).
/// </summary>
public class RestoreDeletionTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    // O relógio parte de "agora": o token é emitido e validado nele (a validação usa o horário real).
    private static readonly DateTimeOffset Start = DateTimeOffset.UtcNow;

    private readonly FakeClock _clock = new(Start);

    protected override bool UseProbeModel => true;

    protected override IClock? Clock => _clock;

    private async Task<(HttpClient Http, TestAccount Account)> NewUserAsync(string name)
    {
        using var anonymous = Factory.CreateClient();
        var account = await anonymous.SignUpAsync(name: name);
        return (Factory.CreateClient().WithBearer(account.AccessToken), account);
    }

    /// <summary>Cria um item e o exclui num lote próprio, como um handler faria (<c>BeginDeletionBatch</c>).</summary>
    private async Task<(Guid Batch, Guid ItemId, Guid ParentId)> DeleteItemAsync(TestAccount account)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CurrentContext>();
        context.Populate(account.Session.User.Id, account.Session.Tenant.Id, Channel.App);
        var db = scope.ServiceProvider.GetRequiredService<TestFinMyDbContext>();

        var parent = new ProbeParent { Name = "Carteira Azul" };
        var item = new ProbeItem { ParentId = parent.Id, Label = "Mercado da Lia" };
        db.AddRange(parent, item);
        await db.SaveChangesAsync();

        var batch = context.BeginDeletionBatch();
        db.Remove(item);
        await db.SaveChangesAsync();
        return (batch, item.Id, parent.Id);
    }

    private async Task<bool> IsDeletedAsync(Guid tenantId, Guid itemId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CurrentContext>();
        using var system = context.BeginSystemScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<TestFinMyDbContext>();
        return (await db.Items.IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter]).SingleAsync(i => i.Id == itemId)).DeletedAt is not null;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    [Fact]
    public async Task Restaura_o_lote_com_204_e_a_linha_volta_a_aparecer()
    {
        var (http, lia) = await NewUserAsync("Lia");
        var (batch, itemId, _) = await DeleteItemAsync(lia);
        _clock.UtcNow = Start.AddMinutes(1);

        var response = await http.PostAsync($"/api/v1/deletions/{batch}/restore", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await IsDeletedAsync(lia.Session.Tenant.Id, itemId)).ShouldBeFalse();
    }

    [Fact]
    public async Task Depois_de_10_minutos_responde_404_deletion_not_found_e_nada_muda()
    {
        var (http, lia) = await NewUserAsync("Lia");
        var (batch, itemId, _) = await DeleteItemAsync(lia);
        _clock.UtcNow = Start.AddMinutes(10).AddSeconds(1);

        var response = await http.PostAsync($"/api/v1/deletions/{batch}/restore", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(response)).ShouldBe("deletion.not_found");
        (await IsDeletedAsync(lia.Session.Tenant.Id, itemId)).ShouldBeTrue();
    }

    [Fact]
    public async Task Lote_de_outro_Espaco_responde_404_e_a_linha_continua_excluida()
    {
        var (_, lia) = await NewUserAsync("Lia");
        var (httpDeLeo, _) = await NewUserAsync("Léo");
        var (batch, itemId, _) = await DeleteItemAsync(lia);

        var response = await httpDeLeo.PostAsync($"/api/v1/deletions/{batch}/restore", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(response)).ShouldBe("deletion.not_found");
        (await IsDeletedAsync(lia.Session.Tenant.Id, itemId)).ShouldBeTrue();
    }

    [Fact]
    public async Task Restaurar_duas_vezes_responde_404_na_segunda()
    {
        var (http, lia) = await NewUserAsync("Lia");
        var (batch, _, _) = await DeleteItemAsync(lia);

        (await http.PostAsync($"/api/v1/deletions/{batch}/restore", content: null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var second = await http.PostAsync($"/api/v1/deletions/{batch}/restore", content: null);

        second.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeAsync(second)).ShouldBe("deletion.not_found");
    }

    [Fact]
    public async Task Lote_inexistente_responde_404_e_sem_token_401()
    {
        var (http, _) = await NewUserAsync("Lia");

        var response = await http.PostAsync($"/api/v1/deletions/{Guid.NewGuid()}/restore", content: null);
        var anonymous = await Client.PostAsync($"/api/v1/deletions/{Guid.NewGuid()}/restore", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Violacao_de_indice_unico_responde_409_deletion_restore_conflict()
    {
        var (http, lia) = await NewUserAsync("Lia");
        Guid batch;
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CurrentContext>();
            context.Populate(lia.Session.User.Id, lia.Session.Tenant.Id, Channel.App);
            var db = scope.ServiceProvider.GetRequiredService<TestFinMyDbContext>();
            var azul = new ProbeNamed { Name = "Azul" };
            db.Add(azul);
            await db.SaveChangesAsync();
            batch = context.BeginDeletionBatch();
            db.Remove(azul);
            await db.SaveChangesAsync();
            db.Add(new ProbeNamed { Name = "Azul" });
            await db.SaveChangesAsync();
        }

        var response = await http.PostAsync($"/api/v1/deletions/{batch}/restore", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeAsync(response)).ShouldBe("deletion.restore_conflict");
    }
}
