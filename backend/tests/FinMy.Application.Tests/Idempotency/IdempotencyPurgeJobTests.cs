using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Idempotency;
using FinMy.Domain.Identity;
using FinMy.Infrastructure.Context;
using FinMy.Infrastructure.Jobs;
using FinMy.Infrastructure.Persistence;
using FinMy.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinMy.Application.Tests.Idempotency;

[Collection(PostgresTestGroup.Name)]
public class IdempotencyPurgeJobTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddFinMyTenantJobRunner();
        services.AddFinMyIdempotencyPurge();
        services.AddScoped<ITenantIdSource, EfTenantIdSource>();
        services.AddScoped<CurrentContext>();
        services.AddScoped<ICurrentContext>(sp => sp.GetRequiredService<CurrentContext>());
        services.AddSingleton<IClock>(new FakeClock(Now));
        services.AddDbContext<TestFinMyDbContext>((sp, options) =>
            options.UseFinMyNpgsql(postgres.ConnectionString).AddFinMyInterceptors(sp.GetRequiredService<IClock>()));
        services.AddScoped<FinMyDbContext>(sp => sp.GetRequiredService<TestFinMyDbContext>());

        return services.BuildServiceProvider();
    }

    private async Task<Guid> SeedSpaceAsync(string tag)
    {
        await using var provider = BuildServices();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestFinMyDbContext>();

        using var system = scope.ServiceProvider.GetRequiredService<ICurrentContext>().BeginSystemScope(Guid.Empty);
        var user = new User(tag, $"{tag}-{Guid.NewGuid():N}@example.com", "hash", Now);
        var tenant = new Tenant($"Espaço {tag}");
        db.AddRange(user, tenant);
        await db.SaveChangesAsync();
        db.Add(new TenantMember(tenant.Id, user.Id, Now));
        await db.SaveChangesAsync();

        return tenant.Id;
    }

    private static IdempotencyKey KeyOf(Guid tenantId, DateTimeOffset createdAt)
        => new(tenantId, Guid.NewGuid(), null, [1, 2, 3], createdAt);

    [Fact]
    public async Task Remove_chaves_com_mais_de_24h_de_todos_os_Espacos_e_preserva_as_recentes()
    {
        var tenantA = await SeedSpaceAsync("a");
        var tenantB = await SeedSpaceAsync("b");
        var old = new[] { KeyOf(tenantA, Now.AddHours(-25)), KeyOf(tenantB, Now.AddDays(-3)) };
        var recent = new[] { KeyOf(tenantA, Now.AddHours(-23)), KeyOf(tenantB, Now.AddMinutes(-1)) };

        await using var provider = BuildServices();
        await using (var scope = provider.CreateAsyncScope())
        {
            var seed = scope.ServiceProvider.GetRequiredService<TestFinMyDbContext>();
            seed.AddRange(old);
            seed.AddRange(recent);
            await seed.SaveChangesAsync();
        }

        var job = provider.GetRequiredService<IdempotencyPurgeJob>();
        await job.RunAsync();
        await job.RunAsync();

        await using var read = provider.CreateAsyncScope();
        var db = read.ServiceProvider.GetRequiredService<TestFinMyDbContext>();
        using var system = read.ServiceProvider.GetRequiredService<ICurrentContext>().BeginSystemScope(tenantA);
        var keptA = await db.Set<IdempotencyKey>().Select(k => k.Key).ToListAsync();
        keptA.ShouldContain(recent[0].Key);
        keptA.ShouldNotContain(old[0].Key);

        using var systemB = read.ServiceProvider.GetRequiredService<ICurrentContext>().BeginSystemScope(tenantB);
        var keptB = await db.Set<IdempotencyKey>().Select(k => k.Key).ToListAsync();
        keptB.ShouldContain(recent[1].Key);
        keptB.ShouldNotContain(old[1].Key);
    }

    [Fact]
    public void Job_diario_e_registrado_as_03h15_no_horario_de_Brasilia()
    {
        var recorded = new List<(string Id, string Cron, TimeZoneInfo? Zone)>();

        IdempotencyPurgeJob.Describe((id, cron, zone) => recorded.Add((id, cron, zone)));

        var job = recorded.ShouldHaveSingleItem();
        job.Id.ShouldBe("idempotency-purge");
        job.Cron.ShouldBe("15 3 * * *");
        job.Zone!.Id.ShouldBe("America/Sao_Paulo");
    }
}
