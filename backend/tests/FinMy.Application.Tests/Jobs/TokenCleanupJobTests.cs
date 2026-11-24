using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Identity;
using FinMy.Infrastructure.Context;
using FinMy.Infrastructure.Jobs;
using FinMy.Infrastructure.Persistence;
using FinMy.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinMy.Application.Tests.Jobs;

[Collection(PostgresTestGroup.Name)]
public class TokenCleanupJobTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddFinMyTenantJobRunner();
        services.AddFinMyTokenCleanup();
        services.AddScoped<ITenantIdSource, EfTenantIdSource>();
        services.AddScoped<CurrentContext>();
        services.AddScoped<ICurrentContext>(sp => sp.GetRequiredService<CurrentContext>());
        services.AddSingleton<IClock>(new FakeClock(Now));
        services.AddDbContext<TestFinMyDbContext>((sp, options) =>
            options.UseFinMyNpgsql(postgres.ConnectionString).AddFinMyInterceptors(sp.GetRequiredService<IClock>()));
        services.AddScoped<FinMyDbContext>(sp => sp.GetRequiredService<TestFinMyDbContext>());

        return services.BuildServiceProvider();
    }

    private async Task<(Guid TenantId, Guid UserId)> SeedSpaceAsync(string tag)
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

        return (tenant.Id, user.Id);
    }

    private static RefreshToken Refresh(Guid userId, DateTimeOffset expiresAt)
        => new(userId, Guid.NewGuid(), Guid.NewGuid().ToByteArray(), null, Now.AddDays(-100), expiresAt);

    private static PasswordResetToken Reset(Guid userId, DateTimeOffset expiresAt)
        => new(userId, Guid.NewGuid().ToByteArray(), Now.AddDays(-5), expiresAt);

    [Fact]
    public async Task Remove_tokens_vencidos_ha_mais_de_24h_e_preserva_os_demais()
    {
        var (_, userA) = await SeedSpaceAsync("a");
        var (_, userB) = await SeedSpaceAsync("b");
        var expiredRefresh = new[] { Refresh(userA, Now.AddDays(-2)), Refresh(userB, Now.AddDays(-30)) };
        var keptRefresh = new[] { Refresh(userA, Now.AddHours(-23)), Refresh(userB, Now.AddDays(10)) };
        var expiredReset = new[] { Reset(userA, Now.AddDays(-2)), Reset(userB, Now.AddDays(-3)) };
        var keptReset = new[] { Reset(userA, Now.AddMinutes(30)), Reset(userB, Now.AddHours(-1)) };

        await using var provider = BuildServices();
        await using (var scope = provider.CreateAsyncScope())
        {
            var seed = scope.ServiceProvider.GetRequiredService<TestFinMyDbContext>();
            seed.AddRange(expiredRefresh);
            seed.AddRange(keptRefresh);
            seed.AddRange(expiredReset);
            seed.AddRange(keptReset);
            await seed.SaveChangesAsync();
        }

        await provider.GetRequiredService<TokenCleanupJob>().RunAsync();

        await using var read = provider.CreateAsyncScope();
        var db = read.ServiceProvider.GetRequiredService<TestFinMyDbContext>();
        var refreshIds = await db.Set<RefreshToken>().Select(t => t.Id).ToListAsync();
        var resetIds = await db.Set<PasswordResetToken>().Select(t => t.Id).ToListAsync();

        refreshIds.ShouldNotContain(expiredRefresh[0].Id);
        refreshIds.ShouldNotContain(expiredRefresh[1].Id);
        refreshIds.ShouldContain(keptRefresh[0].Id);
        refreshIds.ShouldContain(keptRefresh[1].Id);
        resetIds.ShouldNotContain(expiredReset[0].Id);
        resetIds.ShouldNotContain(expiredReset[1].Id);
        resetIds.ShouldContain(keptReset[0].Id);
        resetIds.ShouldContain(keptReset[1].Id);
    }

    [Fact]
    public async Task Remove_tickets_de_consentimento_OAuth_vencidos_ha_mais_de_24h()
    {
        var (_, user) = await SeedSpaceAsync("a");
        var old = new OAuthConsentTicket(Guid.NewGuid().ToByteArray(), user, Guid.NewGuid(), "cliente", "https://claude.ai/api/mcp/auth_callback", "desafio", Now.AddDays(-3));
        var recent = new OAuthConsentTicket(Guid.NewGuid().ToByteArray(), user, Guid.NewGuid(), "cliente", "https://claude.ai/api/mcp/auth_callback", "desafio", Now.AddHours(-1));

        await using var provider = BuildServices();
        await using (var scope = provider.CreateAsyncScope())
        {
            var seed = scope.ServiceProvider.GetRequiredService<TestFinMyDbContext>();
            seed.AddRange(old, recent);
            await seed.SaveChangesAsync();
        }

        await provider.GetRequiredService<TokenCleanupJob>().RunAsync();

        await using var read = provider.CreateAsyncScope();
        var ids = await read.ServiceProvider.GetRequiredService<TestFinMyDbContext>().Set<OAuthConsentTicket>().Select(t => t.Id).ToListAsync();
        ids.ShouldNotContain(old.Id);
        ids.ShouldContain(recent.Id);
    }

    [Fact]
    public async Task Executa_sob_escopo_de_sistema_uma_vez_por_Espaco_e_e_idempotente()
    {
        var (tenantA, _) = await SeedSpaceAsync("a");
        var (tenantB, _) = await SeedSpaceAsync("b");
        await using var provider = BuildServices();
        var seen = new List<Guid>();

        await provider.GetRequiredService<ITenantJobRunner>().ForEachTenantAsync((sp, _) =>
        {
            var ctx = sp.GetRequiredService<ICurrentContext>();
            ctx.IsSystem.ShouldBeTrue();
            seen.Add(ctx.TenantIdOrEmpty);
            return Task.CompletedTask;
        });
        seen.ShouldContain(tenantA);
        seen.ShouldContain(tenantB);

        var job = provider.GetRequiredService<TokenCleanupJob>();
        await job.RunAsync();
        await job.RunAsync();
    }

    [Fact]
    public void Job_diario_e_registrado_as_03h_no_horario_de_Brasilia()
    {
        var recorded = new List<(string Id, string Cron, TimeZoneInfo? Zone)>();

        TokenCleanupJob.Describe((id, cron, zone) => recorded.Add((id, cron, zone)));

        var job = recorded.ShouldHaveSingleItem();
        job.Id.ShouldBe("token-cleanup");
        job.Cron.ShouldBe("0 3 * * *");
        job.Zone!.Id.ShouldBe("America/Sao_Paulo");
    }
}
