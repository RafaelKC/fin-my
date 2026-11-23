using FinMy.Application.Abstractions;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.DependencyInjection;

namespace FinMy.Infrastructure.Jobs;

public static class JobsServiceCollectionExtensions
{
    /// <summary>Registra o <see cref="ITenantJobRunner"/> (sem Hangfire; usável em testes).</summary>
    public static IServiceCollection AddFinMyTenantJobRunner(this IServiceCollection services)
        => services.AddSingleton<ITenantJobRunner, TenantJobRunner>();

    /// <summary>Hangfire com armazenamento no PostgreSQL (docs/arquitetura.md §2) e o <see cref="ITenantJobRunner"/>.</summary>
    public static IServiceCollection AddFinMyHangfire(this IServiceCollection services, string connectionString)
    {
        services.AddFinMyTenantJobRunner();
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString)));

        return services;
    }
}
