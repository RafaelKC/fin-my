using FinMy.Application.Abstractions;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinMy.Infrastructure.Jobs;

public static class JobsServiceCollectionExtensions
{
    /// <summary>Registra o <see cref="ITenantJobRunner"/> (sem Hangfire; usável em testes).</summary>
    public static IServiceCollection AddFinMyTenantJobRunner(this IServiceCollection services)
        => services.AddSingleton<ITenantJobRunner, TenantJobRunner>();

    /// <summary>Registra o job de limpeza de tokens.</summary>
    public static IServiceCollection AddFinMyTokenCleanup(this IServiceCollection services)
        => services.AddTransient<TokenCleanupJob>();

    /// <summary>Registra o job de expurgo de exclusões lógicas.</summary>
    public static IServiceCollection AddFinMyDeletionPurge(this IServiceCollection services)
        => services.AddTransient<DeletionPurgeJob>();

    /// <summary>Registra o job diário que estende o horizonte das recorrências.</summary>
    public static IServiceCollection AddFinMyRecurrenceHorizon(this IServiceCollection services)
        => services.AddTransient<RecurrenceHorizonJob>();

    /// <summary>Registra o job de expurgo das chaves de idempotência.</summary>
    public static IServiceCollection AddFinMyIdempotencyPurge(this IServiceCollection services)
        => services.AddTransient<IdempotencyPurgeJob>();

    /// <summary>Agenda os jobs recorrentes no Hangfire (idempotente: atualiza se já existir).</summary>
    public static void RegisterFinMyRecurringJobs(this IRecurringJobManager manager)
    {
        TokenCleanupJob.Describe((id, cron, zone) =>
            manager.AddOrUpdate<TokenCleanupJob>(id, job => job.RunAsync(CancellationToken.None), cron, new RecurringJobOptions { TimeZone = zone ?? TimeZoneInfo.Utc }));
        DeletionPurgeJob.Describe((id, cron, zone) =>
            manager.AddOrUpdate<DeletionPurgeJob>(id, job => job.RunAsync(CancellationToken.None), cron, new RecurringJobOptions { TimeZone = zone ?? TimeZoneInfo.Utc }));
        RecurrenceHorizonJob.Describe((id, cron, zone) =>
            manager.AddOrUpdate<RecurrenceHorizonJob>(id, job => job.RunAsync(CancellationToken.None), cron, new RecurringJobOptions { TimeZone = zone ?? TimeZoneInfo.Utc }));
        IdempotencyPurgeJob.Describe((id, cron, zone) =>
            manager.AddOrUpdate<IdempotencyPurgeJob>(id, job => job.RunAsync(CancellationToken.None), cron, new RecurringJobOptions { TimeZone = zone ?? TimeZoneInfo.Utc }));
    }

    /// <summary>Hangfire com armazenamento no PostgreSQL (docs/arquitetura.md §2) e o <see cref="ITenantJobRunner"/>.</summary>
    public static IServiceCollection AddFinMyHangfire(this IServiceCollection services, string connectionString)
    {
        services.AddFinMyTenantJobRunner();
        services.AddFinMyTokenCleanup();
        services.AddFinMyDeletionPurge();
        services.AddFinMyIdempotencyPurge();
        services.AddFinMyRecurrenceHorizon();
        services.AddScoped<ITenantIdSource, EfTenantIdSource>();
        services.AddTransient<PasswordResetEmailJob>();
        services.Replace(ServiceDescriptor.Scoped<IPasswordResetDispatcher, HangfirePasswordResetDispatcher>());
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString)));

        return services;
    }
}
