using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Common;
using FinMy.Infrastructure.Context;
using FinMy.Infrastructure.Jobs;
using FinMy.Infrastructure.Persistence;
using FinMy.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinMy.Application.Tests.Jobs;

[Collection(PostgresTestGroup.Name)]
public class DeletionPurgeJobTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 4, 20, 12, 0, 0, TimeSpan.Zero);

    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddFinMyTenantJobRunner();
        services.AddFinMyDeletionPurge();
        services.AddScoped<ITenantIdSource, FixedTenants>();
        services.AddScoped<CurrentContext>();
        services.AddScoped<ICurrentContext>(sp => sp.GetRequiredService<CurrentContext>());
        services.AddSingleton<IClock>(new FakeClock(Now));
        services.AddDbContext<TestFinMyDbContext>((sp, options) =>
            options.UseFinMyNpgsql(postgres.ConnectionString).AddFinMyInterceptors(sp.GetRequiredService<IClock>()));
        services.AddScoped<FinMyDbContext>(sp => sp.GetRequiredService<TestFinMyDbContext>());
        services.AddSingleton(new TenantList());

        return services.BuildServiceProvider();
    }

    private sealed class TenantList
    {
        public List<Guid> Ids { get; } = [];
    }

    private sealed class FixedTenants(TenantList list) : ITenantIdSource
    {
        public Task<IReadOnlyList<Guid>> ListTenantIdsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>(list.Ids);
    }

    private async Task<Guid> SeedParentAsync(Guid tenant, string name, DateTimeOffset? deletedAt)
    {
        var parent = new ProbeParent { Name = name };
        await using var seed = postgres.CreateContext(tenant);
        seed.Add(parent);
        await seed.SaveChangesAsync();

        if (deletedAt is { } at)
        {
            await using var del = postgres.CreateContext(tenant, clock: new FakeClock(at));
            del.Remove(await del.Parents.SingleAsync(p => p.Id == parent.Id));
            await del.SaveChangesAsync();
        }

        return parent.Id;
    }

    private async Task<bool> ExistsAsync(Guid tenant, Guid id)
    {
        await using var db = postgres.CreateContext(tenant);
        return await db.Parents.IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter]).AnyAsync(p => p.Id == id);
    }

    [Fact]
    public async Task Expurga_so_o_que_foi_excluido_ha_mais_de_30_dias_e_so_do_Espaco_do_escopo()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        var velha = await SeedParentAsync(tenantA, "velha", Now.AddDays(-31));
        var recente = await SeedParentAsync(tenantA, "recente", Now.AddDays(-29));
        var ativa = await SeedParentAsync(tenantA, "ativa", null);
        var velhaDeB = await SeedParentAsync(tenantB, "velha de B", Now.AddDays(-45));

        await using var provider = BuildServices();
        provider.GetRequiredService<TenantList>().Ids.Add(tenantA);

        await provider.GetRequiredService<DeletionPurgeJob>().RunAsync();

        (await ExistsAsync(tenantA, velha)).ShouldBeFalse();
        (await ExistsAsync(tenantA, recente)).ShouldBeTrue();
        (await ExistsAsync(tenantA, ativa)).ShouldBeTrue();
        (await ExistsAsync(tenantB, velhaDeB)).ShouldBeTrue("B não estava na lista de Espaços do job.");
    }

    [Fact]
    public async Task Expurga_todos_os_Espacos_listados_e_e_idempotente()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        var velhaA = await SeedParentAsync(tenantA, "velha A", Now.AddDays(-40));
        var velhaB = await SeedParentAsync(tenantB, "velha B", Now.AddDays(-40));

        await using var provider = BuildServices();
        provider.GetRequiredService<TenantList>().Ids.AddRange([tenantA, tenantB]);
        var job = provider.GetRequiredService<DeletionPurgeJob>();

        await job.RunAsync();
        await job.RunAsync();

        (await ExistsAsync(tenantA, velhaA)).ShouldBeFalse();
        (await ExistsAsync(tenantB, velhaB)).ShouldBeFalse();
    }

    [Fact]
    public async Task Pai_expirado_so_e_apagado_depois_do_dependente_que_o_referencia()
    {
        var tenant = Guid.CreateVersion7();
        var parent = new ProbeParent { Name = "pai" };
        var item = new ProbeItem { ParentId = parent.Id, Label = "filho" };
        await using (var seed = postgres.CreateContext(tenant))
        {
            seed.AddRange(parent, item);
            await seed.SaveChangesAsync();
        }

        await using (var del = postgres.CreateContext(tenant, clock: new FakeClock(Now.AddDays(-40))))
        {
            del.Remove(await del.Parents.SingleAsync(p => p.Id == parent.Id));
            await del.SaveChangesAsync();
        }

        await using var provider = BuildServices();
        provider.GetRequiredService<TenantList>().Ids.Add(tenant);
        var job = provider.GetRequiredService<DeletionPurgeJob>();

        await job.RunAsync();
        (await ExistsAsync(tenant, parent.Id)).ShouldBeTrue("o item ativo ainda referencia o pai.");

        await using (var del = postgres.CreateContext(tenant, clock: new FakeClock(Now.AddDays(-35))))
        {
            del.Remove(await del.Items.SingleAsync(i => i.Id == item.Id));
            await del.SaveChangesAsync();
        }

        await job.RunAsync();
        (await ExistsAsync(tenant, parent.Id)).ShouldBeFalse();
    }

    [Fact]
    public async Task Varre_todas_as_tabelas_ISoftDeletable_do_modelo()
    {
        var tenant = Guid.CreateVersion7();
        var named = new ProbeNamed { Name = "velha" };
        await using (var seed = postgres.CreateContext(tenant))
        {
            seed.Add(named);
            await seed.SaveChangesAsync();
            seed.Remove(named);
            await seed.SaveChangesAsync();
        }

        await using (var db = postgres.CreateContext(tenant))
        {
            await db.Database.ExecuteSqlRawAsync("UPDATE probe_named SET deleted_at = {0} WHERE id = {1}", Now.AddDays(-31), named.Id);
        }

        await using var provider = BuildServices();
        provider.GetRequiredService<TenantList>().Ids.Add(tenant);
        await provider.GetRequiredService<DeletionPurgeJob>().RunAsync();

        await using var leitura = postgres.CreateContext(tenant);
        (await leitura.Named.IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter]).AnyAsync(n => n.Id == named.Id)).ShouldBeFalse();
    }

    [Fact]
    public void Job_diario_e_registrado_as_03h30_no_horario_de_Brasilia()
    {
        var recorded = new List<(string Id, string Cron, TimeZoneInfo? Zone)>();

        DeletionPurgeJob.Describe((id, cron, zone) => recorded.Add((id, cron, zone)));

        var job = recorded.ShouldHaveSingleItem();
        job.Id.ShouldBe("deletion-purge");
        job.Cron.ShouldBe("30 3 * * *");
        job.Zone!.Id.ShouldBe("America/Sao_Paulo");
        DeletionPurgeJob.Retention.ShouldBe(TimeSpan.FromDays(30));
    }
}
