using FinMy.Domain.Common;
using FinMy.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Application.Tests.Infrastructure;

[Collection(PostgresTestGroup.Name)]
public class TenantInterceptorTests(PostgresFixture postgres)
{
    private async Task<ProbeParent> SeedAsync(Guid tenant, string name = "semente")
    {
        await using var db = postgres.CreateContext(tenant);
        var parent = new ProbeParent { Name = name };
        db.Add(parent);
        await db.SaveChangesAsync();
        return parent;
    }

    [Fact]
    public async Task Entidade_nova_recebe_o_Espaco_do_contexto_sozinha()
    {
        var tenant = Guid.CreateVersion7();
        await using var db = postgres.CreateContext(tenant);
        var parent = new ProbeParent { Name = "nova" };

        db.Add(parent);
        await db.SaveChangesAsync();

        parent.TenantId.ShouldBe(tenant);
        await using var leitura = postgres.CreateContext(tenant);
        (await leitura.Parents.SingleAsync(p => p.Id == parent.Id)).TenantId.ShouldBe(tenant);
    }

    [Fact]
    public async Task Entidade_nova_com_o_mesmo_Espaco_do_contexto_e_aceita()
    {
        var tenant = Guid.CreateVersion7();
        await using var db = postgres.CreateContext(tenant);
        var parent = new ProbeParent { Name = "ok" };
        db.Add(parent);
        db.Entry(parent).Property(nameof(ProbeParent.TenantId)).CurrentValue = tenant;

        await db.SaveChangesAsync();

        parent.TenantId.ShouldBe(tenant);
    }

    [Fact]
    public async Task Gravar_sem_Espaco_no_contexto_falha()
    {
        await using var db = postgres.CreateContext(new CurrentContext());
        db.Add(new ProbeParent { Name = "sem espaço" });

        await Should.ThrowAsync<MissingTenantContextException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Gravar_entidade_nova_de_outro_Espaco_falha()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        await using var db = postgres.CreateContext(tenantA);
        var parent = new ProbeParent { Name = "intruso" };
        db.Add(parent);
        db.Entry(parent).Property(nameof(ProbeParent.TenantId)).CurrentValue = tenantB;

        await Should.ThrowAsync<CrossTenantWriteException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Trocar_o_Espaco_de_uma_entidade_existente_falha()
    {
        var tenantA = Guid.CreateVersion7();
        var seeded = await SeedAsync(tenantA);

        await using var db = postgres.CreateContext(tenantA);
        var parent = await db.Parents.SingleAsync(p => p.Id == seeded.Id);

        // TenantId é chave alternativa: o próprio EF já recusa a troca (InvalidOperationException);
        // se isso mudar, o TenantInterceptor responde com CrossTenantWriteException (que também é um InvalidOperationException).
        typeof(TenantEntity).GetProperty(nameof(TenantEntity.TenantId))!.SetValue(parent, Guid.CreateVersion7());

        await Should.ThrowAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        await using var leitura = postgres.CreateContext(tenantA);
        (await leitura.Parents.SingleAsync(p => p.Id == seeded.Id)).TenantId.ShouldBe(tenantA);
    }

    [Fact]
    public async Task Alterar_entidade_de_outro_Espaco_falha()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        var deB = await SeedAsync(tenantB);

        await using var dbA = postgres.CreateContext(tenantA);
        dbA.Attach(deB);
        deB.Name = "alterado por A";

        await Should.ThrowAsync<CrossTenantWriteException>(() => dbA.SaveChangesAsync());

        await using var dbB = postgres.CreateContext(tenantB);
        (await dbB.Parents.SingleAsync(p => p.Id == deB.Id)).Name.ShouldBe("semente");
    }

    [Fact]
    public async Task Excluir_entidade_de_outro_Espaco_falha()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        var deB = await SeedAsync(tenantB);

        await using var dbA = postgres.CreateContext(tenantA);
        dbA.Attach(deB);
        dbA.Remove(deB);

        await Should.ThrowAsync<CrossTenantWriteException>(() => dbA.SaveChangesAsync());

        await using var dbB = postgres.CreateContext(tenantB);
        (await dbB.Parents.CountAsync(p => p.Id == deB.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task Alterar_ou_excluir_sem_Espaco_no_contexto_falha()
    {
        var tenant = Guid.CreateVersion7();
        var seeded = await SeedAsync(tenant);

        await using var db = postgres.CreateContext(new CurrentContext());
        db.Attach(seeded);
        seeded.Name = "sem espaço";

        await Should.ThrowAsync<MissingTenantContextException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Ler_sem_contexto_nao_retorna_nada()
    {
        var tenant = Guid.CreateVersion7();
        await SeedAsync(tenant);

        await using var db = postgres.CreateContext(new CurrentContext());

        (await db.Parents.ToListAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Escopo_de_sistema_grava_no_Espaco_informado()
    {
        var tenant = Guid.CreateVersion7();
        var context = new CurrentContext();
        await using var db = postgres.CreateContext(context);
        var parent = new ProbeParent { Name = "do job" };

        using (context.BeginSystemScope(tenant))
        {
            db.Add(parent);
            await db.SaveChangesAsync();
        }

        parent.TenantId.ShouldBe(tenant);
    }
}
