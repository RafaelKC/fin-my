using FinMy.Application.Abstractions;
using FinMy.Application.Deletions;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Common;
using FinMy.Infrastructure.Context;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Application.Tests.Deletions;

[Collection(PostgresTestGroup.Name)]
public class DeletionStoreTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset T0 = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Cria um pai com um item e os exclui num lote novo no instante <paramref name="at"/>; devolve o lote e os ids.</summary>
    private async Task<(Guid Batch, Guid ParentId, Guid ItemId, Guid UserId)> SeedDeletedAsync(Guid tenant, DateTimeOffset at)
    {
        var user = Guid.CreateVersion7();
        var parent = new ProbeParent { Name = "Carteira Azul" };
        var item = new ProbeItem { ParentId = parent.Id, Label = "Mercado da Lia" };
        await using (var seed = postgres.CreateContext(tenant))
        {
            seed.AddRange(parent, item);
            await seed.SaveChangesAsync();
        }

        var contexto = new CurrentContext();
        contexto.Populate(user, tenant, Channel.App);
        var batch = contexto.BeginDeletionBatch();
        await using (var db = postgres.CreateContext(contexto, new FakeClock(at)))
        {
            db.Remove(await db.Items.SingleAsync(i => i.Id == item.Id));
            db.Remove(await db.Parents.SingleAsync(p => p.Id == parent.Id));
            await db.SaveChangesAsync();
        }

        return (batch, parent.Id, item.Id, user);
    }

    private static DeletionStoreHarness Harness(PostgresFixture postgres, Guid tenant, Guid? user = null) => new(postgres, tenant, user);

    private sealed class DeletionStoreHarness(PostgresFixture postgres, Guid tenant, Guid? user)
    {
        public async Task<RestoreOutcome> RestoreAsync(Guid batch, DateTimeOffset now)
        {
            await using var db = postgres.CreateContext(tenant, user, new FakeClock(now));
            var handler = new RestoreDeletionHandler(new DeletionStore(db), new FakeClock(now));
            try
            {
                await handler.HandleAsync(batch, CancellationToken.None);
                return RestoreOutcome.Restored;
            }
            catch (DomainException ex) when (ex.Error == DeletionErrors.NotFound)
            {
                return RestoreOutcome.NotFound;
            }
            catch (DomainException ex) when (ex.Error == DeletionErrors.RestoreConflict)
            {
                return RestoreOutcome.Conflict;
            }
        }
    }

    [Fact]
    public async Task Restaura_todas_as_entidades_do_lote_e_limpa_a_exclusao_e_atualiza_a_auditoria()
    {
        var tenant = Guid.CreateVersion7();
        var (batch, parentId, itemId, _) = await SeedDeletedAsync(tenant, T0);
        var restorer = Guid.CreateVersion7();

        (await Harness(postgres, tenant, restorer).RestoreAsync(batch, T0.AddMinutes(9))).ShouldBe(RestoreOutcome.Restored);

        await using var leitura = postgres.CreateContext(tenant);
        var item = await leitura.Items.AsNoTracking().SingleAsync(i => i.Id == itemId);
        var parent = await leitura.Parents.AsNoTracking().SingleAsync(p => p.Id == parentId);
        foreach (var linha in new TenantEntity[] { item, parent })
        {
            linha.DeletedAt.ShouldBeNull();
            linha.DeletedBy.ShouldBeNull();
            linha.DeletionBatchId.ShouldBeNull();
            linha.UpdatedAt.ShouldBe(T0.AddMinutes(9));
            linha.UpdatedBy.ShouldBe(restorer);
        }
    }

    [Fact]
    public async Task So_restaura_o_lote_pedido()
    {
        var tenant = Guid.CreateVersion7();
        var (batchA, _, itemA, _) = await SeedDeletedAsync(tenant, T0);
        var (_, _, itemB, _) = await SeedDeletedAsync(tenant, T0);

        (await Harness(postgres, tenant).RestoreAsync(batchA, T0)).ShouldBe(RestoreOutcome.Restored);

        await using var leitura = postgres.CreateContext(tenant);
        (await leitura.Items.Select(i => i.Id).ToListAsync()).ShouldBe([itemA]);
        (await leitura.Items.IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter]).CountAsync(i => i.Id == itemB && i.DeletedAt != null)).ShouldBe(1);
    }

    [Fact]
    public async Task Expirado_depois_de_10_minutos_nao_e_encontrado_e_nada_muda()
    {
        var tenant = Guid.CreateVersion7();
        var (batch, _, itemId, _) = await SeedDeletedAsync(tenant, T0);

        (await Harness(postgres, tenant).RestoreAsync(batch, T0.AddMinutes(10).AddSeconds(1))).ShouldBe(RestoreOutcome.NotFound);

        await using var leitura = postgres.CreateContext(tenant);
        (await leitura.Items.AnyAsync(i => i.Id == itemId)).ShouldBeFalse();
    }

    [Fact]
    public async Task No_limite_de_10_minutos_ainda_restaura()
    {
        var tenant = Guid.CreateVersion7();
        var (batch, _, _, _) = await SeedDeletedAsync(tenant, T0);

        (await Harness(postgres, tenant).RestoreAsync(batch, T0.AddMinutes(10))).ShouldBe(RestoreOutcome.Restored);
    }

    [Fact]
    public async Task Lote_de_outro_Espaco_e_nao_encontrado_e_continua_excluido()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        var (batch, _, itemId, _) = await SeedDeletedAsync(tenantA, T0);

        (await Harness(postgres, tenantB).RestoreAsync(batch, T0)).ShouldBe(RestoreOutcome.NotFound);

        await using var leitura = postgres.CreateContext(tenantA);
        (await leitura.Items.IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter]).SingleAsync(i => i.Id == itemId)).DeletedAt.ShouldBe(T0);
    }

    [Fact]
    public async Task Restaurar_duas_vezes_da_nao_encontrado_na_segunda()
    {
        var tenant = Guid.CreateVersion7();
        var (batch, _, _, _) = await SeedDeletedAsync(tenant, T0);
        var harness = Harness(postgres, tenant);

        (await harness.RestoreAsync(batch, T0)).ShouldBe(RestoreOutcome.Restored);
        (await harness.RestoreAsync(batch, T0)).ShouldBe(RestoreOutcome.NotFound);
    }

    [Fact]
    public async Task Lote_inexistente_e_nao_encontrado()
    {
        (await Harness(postgres, Guid.CreateVersion7()).RestoreAsync(Guid.CreateVersion7(), T0)).ShouldBe(RestoreOutcome.NotFound);
    }

    [Fact]
    public async Task Violacao_de_indice_unico_ao_restaurar_da_conflito_e_nao_restaura_nada()
    {
        var tenant = Guid.CreateVersion7();
        var user = Guid.CreateVersion7();
        var original = new ProbeNamed { Name = "Azul" };
        var parent = new ProbeParent { Name = "Pai do lote" };
        await using (var seed = postgres.CreateContext(tenant))
        {
            seed.AddRange(original, parent);
            await seed.SaveChangesAsync();
        }

        var contexto = new CurrentContext();
        contexto.Populate(user, tenant, Channel.App);
        var batch = contexto.BeginDeletionBatch();
        await using (var db = postgres.CreateContext(contexto, new FakeClock(T0)))
        {
            db.Remove(await db.Named.SingleAsync(n => n.Id == original.Id));
            db.Remove(await db.Parents.SingleAsync(p => p.Id == parent.Id));
            await db.SaveChangesAsync();
        }

        await using (var recriada = postgres.CreateContext(tenant))
        {
            recriada.Add(new ProbeNamed { Name = "Azul" });
            await recriada.SaveChangesAsync();
        }

        (await Harness(postgres, tenant).RestoreAsync(batch, T0)).ShouldBe(RestoreOutcome.Conflict);

        await using var leitura = postgres.CreateContext(tenant);
        (await leitura.Parents.AnyAsync(p => p.Id == parent.Id)).ShouldBeFalse("tudo ou nada: o pai do lote também não volta.");
        (await leitura.Named.IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter]).SingleAsync(n => n.Id == original.Id)).DeletedAt.ShouldBe(T0);
    }
}
