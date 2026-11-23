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
public class TenantJobRunnerTests(PostgresFixture postgres)
{
    private sealed class FixedTenants(params Guid[] ids) : ITenantIdSource
    {
        public Task<IReadOnlyList<Guid>> ListTenantIdsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Guid>>(ids);
    }

    private ServiceProvider BuildServices(params Guid[] tenantIds)
    {
        var services = new ServiceCollection();
        services.AddFinMyTenantJobRunner();
        services.AddSingleton<ITenantIdSource>(new FixedTenants(tenantIds));
        services.AddScoped<CurrentContext>();
        services.AddScoped<ICurrentContext>(sp => sp.GetRequiredService<CurrentContext>());
        services.AddSingleton<IClock>(PostgresFixture.DefaultClock);
        services.AddDbContext<TestFinMyDbContext>((sp, options) =>
            options.UseFinMyNpgsql(postgres.ConnectionString).AddFinMyInterceptors(sp.GetRequiredService<IClock>()));

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Job_roda_uma_vez_por_Espaco_sob_o_filtro_e_grava_via_system()
    {
        var (a, b) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        await using var provider = BuildServices(a, b);
        var runner = provider.GetRequiredService<ITenantJobRunner>();
        var seen = new List<(Guid Tenant, int Visible)>();

        // Linha pré-existente do Espaço B, para provar que o escopo de A não a enxerga.
        await using (var seed = postgres.CreateContext(b))
        {
            seed.Add(new ProbeParent { Name = "do-b" });
            await seed.SaveChangesAsync();
        }

        await runner.ForEachTenantAsync(async (sp, ct) =>
        {
            var ctx = sp.GetRequiredService<ICurrentContext>();
            var db = sp.GetRequiredService<TestFinMyDbContext>();

            ctx.IsSystem.ShouldBeTrue();
            ctx.Channel.ShouldBe(Channel.System);
            ctx.UserId.ShouldBe(SystemUser.Id);

            db.Add(new ProbeParent { Name = "gerado-pelo-job" });
            await db.SaveChangesAsync(ct);

            seen.Add((ctx.TenantIdOrEmpty, await db.Parents.CountAsync(ct)));
        });

        seen.Select(s => s.Tenant).ShouldBe([a, b]);
        seen.Single(s => s.Tenant == a).Visible.ShouldBe(1);
        seen.Single(s => s.Tenant == b).Visible.ShouldBe(2);

        foreach (var tenant in new[] { a, b })
        {
            await using var read = postgres.CreateContext(tenant);
            var row = await read.Parents.SingleAsync(p => p.Name == "gerado-pelo-job");
            row.TenantId.ShouldBe(tenant);
            row.CreatedVia.ShouldBe(Channel.System);
            row.UpdatedVia.ShouldBe(Channel.System);
            row.CreatedBy.ShouldBe(SystemUser.Id);
            row.UpdatedBy.ShouldBe(SystemUser.Id);
        }
    }

    [Fact]
    public async Task Falha_em_um_Espaco_nao_impede_os_demais_e_e_reportada()
    {
        var (a, b) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        await using var provider = BuildServices(a, b);
        var runner = provider.GetRequiredService<ITenantJobRunner>();
        var done = new List<Guid>();

        var ex = await Should.ThrowAsync<AggregateException>(() => runner.ForEachTenantAsync((sp, _) =>
        {
            var tenant = sp.GetRequiredService<ICurrentContext>().TenantIdOrEmpty;
            if (tenant == a)
            {
                throw new InvalidOperationException("falha do Espaço A");
            }

            done.Add(tenant);
            return Task.CompletedTask;
        }));

        ex.InnerExceptions.Count.ShouldBe(1);
        ex.InnerExceptions[0].Message.ShouldBe("falha do Espaço A");
        done.ShouldBe([b]);
    }

    [Fact]
    public async Task Sem_Espacos_nao_executa_nada()
    {
        await using var provider = BuildServices();
        var runner = provider.GetRequiredService<ITenantJobRunner>();
        var calls = 0;

        await runner.ForEachTenantAsync((_, _) =>
        {
            calls++;
            return Task.CompletedTask;
        });

        calls.ShouldBe(0);
    }

    [Fact]
    public async Task Cancelamento_interrompe_a_iteracao()
    {
        await using var provider = BuildServices(Guid.CreateVersion7(), Guid.CreateVersion7());
        var runner = provider.GetRequiredService<ITenantJobRunner>();
        using var cts = new CancellationTokenSource();
        var calls = 0;

        await Should.ThrowAsync<OperationCanceledException>(() => runner.ForEachTenantAsync((_, _) =>
        {
            calls++;
            cts.Cancel();
            return Task.CompletedTask;
        }, cts.Token));

        calls.ShouldBe(1);
    }
}
