using FinMy.Domain.Common;
using FinMy.Infrastructure.Context;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Application.Tests.Infrastructure;

[Collection(PostgresTestGroup.Name)]
public class SoftDeleteTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset T0 = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Remove_vira_soft_delete_com_autor_horario_e_lote()
    {
        var tenant = Guid.CreateVersion7();
        var user = Guid.CreateVersion7();
        var contexto = new CurrentContext();
        contexto.Populate(user, tenant, Channel.App);
        var parent = new ProbeParent { Name = "a excluir" };
        await using (var seed = postgres.CreateContext(tenant))
        {
            seed.Add(parent);
            await seed.SaveChangesAsync();
        }

        await using (var db = postgres.CreateContext(contexto, new FakeClock(T0)))
        {
            db.Remove(await db.Parents.SingleAsync(p => p.Id == parent.Id));
            await db.SaveChangesAsync();
        }

        await using var leitura = postgres.CreateContext(tenant);
        var linha = await leitura.Parents.IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])
            .AsNoTracking().SingleAsync(p => p.Id == parent.Id);
        linha.DeletedAt.ShouldBe(T0);
        linha.DeletedBy.ShouldBe(user);
        linha.DeletionBatchId.ShouldBe(contexto.DeletionBatchId);
        linha.UpdatedAt.ShouldBe(T0);
        linha.UpdatedBy.ShouldBe(user);
    }

    [Fact]
    public async Task Exclusoes_da_mesma_requisicao_compartilham_o_lote()
    {
        var tenant = Guid.CreateVersion7();
        var contexto = new CurrentContext();
        contexto.Populate(Guid.CreateVersion7(), tenant, Channel.App);
        await using var db = postgres.CreateContext(contexto);
        var um = new ProbeParent { Name = "um" };
        var dois = new ProbeParent { Name = "dois" };
        db.AddRange(um, dois);
        await db.SaveChangesAsync();

        db.RemoveRange(um, dois);
        await db.SaveChangesAsync();

        var lotes = await db.Parents.IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])
            .Select(p => p.DeletionBatchId).ToListAsync();
        lotes.ShouldBe([contexto.DeletionBatchId, contexto.DeletionBatchId]);
    }

    [Fact]
    public async Task Filtro_esconde_a_linha_excluida_e_a_linha_continua_no_banco()
    {
        var tenant = Guid.CreateVersion7();
        await using var db = postgres.CreateContext(tenant);
        var viva = new ProbeParent { Name = "viva" };
        var morta = new ProbeParent { Name = "morta" };
        db.AddRange(viva, morta);
        await db.SaveChangesAsync();

        db.Remove(morta);
        await db.SaveChangesAsync();

        (await db.Parents.Select(p => p.Name).ToListAsync()).ShouldBe(["viva"]);
        (await db.Parents.IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter]).CountAsync()).ShouldBe(2);
        (await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM probe_parents WHERE tenant_id = {0}", tenant).SingleAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task IgnoreQueryFilters_SoftDelete_mantem_o_filtro_de_Espaco()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        foreach (var tenant in new[] { tenantA, tenantB })
        {
            await using var seed = postgres.CreateContext(tenant);
            var morta = new ProbeParent { Name = $"morta {tenant}" };
            seed.Add(morta);
            await seed.SaveChangesAsync();
            seed.Remove(morta);
            await seed.SaveChangesAsync();
        }

        await using var dbA = postgres.CreateContext(tenantA);

        var nomes = await dbA.Parents.IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter]).Select(p => p.Name).ToListAsync();

        nomes.ShouldBe([$"morta {tenantA}"]);
    }

    [Fact]
    public async Task Excluir_o_pai_nao_apaga_os_dependentes()
    {
        var tenant = Guid.CreateVersion7();
        var parent = new ProbeParent { Name = "pai" };
        var item = new ProbeItem { ParentId = parent.Id, Label = "filho" };
        await using (var seed = postgres.CreateContext(tenant))
        {
            seed.AddRange(parent, item);
            await seed.SaveChangesAsync();
        }

        await using (var db = postgres.CreateContext(tenant))
        {
            db.Remove(await db.Parents.SingleAsync(p => p.Id == parent.Id));
            await db.SaveChangesAsync();
        }

        await using var leitura = postgres.CreateContext(tenant);
        (await leitura.Items.SingleAsync(i => i.Id == item.Id)).DeletedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Excluir_em_escopo_de_sistema_grava_o_usuario_de_sistema()
    {
        var tenant = Guid.CreateVersion7();
        var parent = new ProbeParent { Name = "x" };
        await using (var seed = postgres.CreateContext(tenant))
        {
            seed.Add(parent);
            await seed.SaveChangesAsync();
        }

        var contexto = new CurrentContext();
        await using var db = postgres.CreateContext(contexto);
        using (contexto.BeginSystemScope(tenant))
        {
            db.Remove(await db.Parents.SingleAsync(p => p.Id == parent.Id));
            await db.SaveChangesAsync();
        }

        await using var leitura = postgres.CreateContext(tenant);
        (await leitura.Parents.IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter]).SingleAsync(p => p.Id == parent.Id))
            .DeletedBy.ShouldBe(SystemUser.Id);
    }
}
