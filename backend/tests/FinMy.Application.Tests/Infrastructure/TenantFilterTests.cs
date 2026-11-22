using FinMy.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Application.Tests.Infrastructure;

[Collection(PostgresTestGroup.Name)]
public class TenantFilterTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Leitura_sem_contexto_nao_retorna_nenhuma_linha()
    {
        var tenant = Guid.CreateVersion7();
        await using (var seed = postgres.CreateContext(tenant))
        {
            seed.Add(seed.InTenant(new ProbeParent { Name = "existe" }, tenant));
            await seed.SaveChangesAsync();
        }

        await using var semContexto = postgres.CreateContext(new CurrentContext());

        (await semContexto.Parents.ToListAsync()).ShouldBeEmpty();
        (await semContexto.Parents.IgnoreQueryFilters([FinMy.Infrastructure.Persistence.ModelBuilderExtensions.SoftDeleteFilter]).ToListAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Cada_Espaco_ve_apenas_as_proprias_linhas_mesmo_com_modelo_em_cache()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        await using (var seed = postgres.CreateContext(tenantA))
        {
            seed.Add(seed.InTenant(new ProbeParent { Name = "de A" }, tenantA));
            await seed.SaveChangesAsync();
        }

        await using (var seed = postgres.CreateContext(tenantB))
        {
            seed.Add(seed.InTenant(new ProbeParent { Name = "de B" }, tenantB));
            await seed.SaveChangesAsync();
        }

        await using var dbA = postgres.CreateContext(tenantA);
        await using var dbB = postgres.CreateContext(tenantB);

        (await dbA.Parents.Select(p => p.Name).ToListAsync()).ShouldBe(["de A"]);
        (await dbB.Parents.Select(p => p.Name).ToListAsync()).ShouldBe(["de B"]);
    }

    [Fact]
    public async Task Filtro_de_exclusao_logica_esconde_linhas_excluidas()
    {
        var tenant = Guid.CreateVersion7();
        await using var db = postgres.CreateContext(tenant);
        var viva = db.InTenant(new ProbeParent { Name = "viva" }, tenant);
        var morta = db.InTenant(new ProbeParent { Name = "morta" }, tenant);
        db.AddRange(viva, morta);
        await db.SaveChangesAsync();
        await db.Parents.Where(p => p.Id == morta.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.DeletedAt, DateTimeOffset.UtcNow));

        (await db.Parents.Select(p => p.Name).ToListAsync()).ShouldBe(["viva"]);
    }

    [Fact]
    public async Task IgnoreQueryFilters_SoftDelete_mantem_o_filtro_de_Espaco()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        foreach (var tenant in new[] { tenantA, tenantB })
        {
            await using var seed = postgres.CreateContext(tenant);
            var morta = seed.InTenant(new ProbeParent { Name = $"morta {tenant}" }, tenant);
            seed.Add(morta);
            await seed.SaveChangesAsync();
            await seed.Parents.Where(p => p.Id == morta.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.DeletedAt, DateTimeOffset.UtcNow));
        }

        await using var dbA = postgres.CreateContext(tenantA);

        var visiveis = await dbA.Parents.IgnoreQueryFilters([FinMy.Infrastructure.Persistence.ModelBuilderExtensions.SoftDeleteFilter]).Select(p => p.Name).ToListAsync();

        visiveis.ShouldBe([$"morta {tenantA}"]);
    }

    [Fact]
    public async Task Token_de_concorrencia_xmin_detecta_conflito()
    {
        var tenant = Guid.CreateVersion7();
        await using var seed = postgres.CreateContext(tenant);
        var parent = seed.InTenant(new ProbeParent { Name = "v1" }, tenant);
        seed.Add(parent);
        await seed.SaveChangesAsync();

        await using var db1 = postgres.CreateContext(tenant);
        await using var db2 = postgres.CreateContext(tenant);
        var p1 = await db1.Parents.SingleAsync(p => p.Id == parent.Id);
        var p2 = await db2.Parents.SingleAsync(p => p.Id == parent.Id);
        p1.Name = "v2";
        await db1.SaveChangesAsync();
        p2.Name = "v3";

        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => db2.SaveChangesAsync());
    }

    [Fact]
    public async Task Colunas_em_snake_case_e_canal_em_texto()
    {
        var tenant = Guid.CreateVersion7();
        await using var db = postgres.CreateContext(tenant);
        db.Add(db.InTenant(new ProbeParent { Name = "x" }, tenant));
        await db.SaveChangesAsync();

        var via = await db.Database.SqlQueryRaw<string>("SELECT created_via AS \"Value\" FROM probe_parents WHERE tenant_id = {0}", tenant).SingleAsync();

        via.ShouldBe("app");
    }
}
