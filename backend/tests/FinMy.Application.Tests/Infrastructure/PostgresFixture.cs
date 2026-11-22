using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FinMy.Application.Abstractions;
using FinMy.Infrastructure.Context;
using FinMy.Infrastructure.Persistence;
using FinMy.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FinMy.Application.Tests.Infrastructure;

/// <summary>PostgreSQL real (Testcontainers) compartilhado pela coleção; cada teste usa seus próprios Espaços.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string Password = "finmy-tests";

    // Container genérico, sem a espera padrão de Testcontainers.PostgreSql: ela usa `exec` (pg_isready)
    // e a leitura de logs, que travam com o socket do podman rootless desta máquina (crun/oom_score_adj).
    // A prontidão é testada abrindo uma conexão (WaitUntilReadyAsync).
    private readonly IContainer _container = new ContainerBuilder("docker.io/library/postgres:17")
        .WithEnvironment("POSTGRES_PASSWORD", Password)
        .WithPortBinding(5432, true)
        .Build();

    private string ConnectionString =>
        $"Host={_container.Hostname};Port={_container.GetMappedPublicPort(5432)};Username=postgres;Password={Password};Database=postgres";

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await WaitUntilReadyAsync();

        await using var db = CreateContext(new CurrentContext());
        await db.Database.EnsureCreatedAsync();
    }

    private async Task WaitUntilReadyAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (true)
        {
            try
            {
                await using var connection = new NpgsqlConnection(ConnectionString);
                await connection.OpenAsync();
                return;
            }
            catch (Exception) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(500);
            }
        }
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>Relógio fixo usado pelos interceptors quando o teste não informa outro.</summary>
    public static FakeClock DefaultClock { get; } = new(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));

    /// <summary>Contexto de testes com os interceptors reais e o <see cref="ICurrentContext"/> informado.</summary>
    public TestFinMyDbContext CreateContext(ICurrentContext currentContext, IClock? clock = null)
    {
        var builder = new DbContextOptionsBuilder<TestFinMyDbContext>();
        builder.UseFinMyNpgsql(ConnectionString).AddFinMyInterceptors(clock ?? DefaultClock);

        return new TestFinMyDbContext(builder.Options, currentContext);
    }

    /// <summary>Contexto atuando como usuário do Espaço informado.</summary>
    public TestFinMyDbContext CreateContext(Guid tenantId, Guid? userId = null, IClock? clock = null)
    {
        var current = new CurrentContext();
        current.Populate(userId ?? Guid.CreateVersion7(), tenantId, Domain.Common.Channel.App);
        return CreateContext(current, clock);
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresTestGroup : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
