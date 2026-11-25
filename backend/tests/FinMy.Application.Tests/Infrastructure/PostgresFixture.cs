using System.Collections.Concurrent;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FinMy.Application.Abstractions;
using FinMy.Infrastructure.Context;
using FinMy.Infrastructure.Persistence;
using FinMy.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FinMy.Application.Tests.Infrastructure;

/// <summary>
/// Acesso ao PostgreSQL real (Testcontainers). O container é um singleton por processo (criado sob demanda e de forma
/// thread-safe): todas as classes de teste — inclusive as que rodam em paralelo — compartilham o mesmo servidor e usam
/// bancos próprios. Use como <c>IClassFixture</c> (ou fixture de coleção); a instância em si não guarda estado.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string Password = "finmy-tests";

    // Várias APIs de teste (uma por teste, em paralelo) abrem pools próprios: o padrão (100) não basta.
    private const string MaxConnections = "max_connections=1000";

    private static readonly Lazy<Task<RunningServer>> Server = new(StartServerAsync, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly ConcurrentDictionary<string, Lazy<Task>> Templates = new();

    // CREATE DATABASE ... TEMPLATE exige que ninguém mais esteja criando a partir do mesmo modelo ao mesmo tempo.
    private static readonly SemaphoreSlim CreateGate = new(1, 1);

    private sealed record RunningServer(IContainer Container, string ConnectionString);

    /// <summary>Conexão com o banco padrão do servidor (usada também pelos testes da Api, que criam bancos próprios).</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Mantém o uso por instância (fixture) nos testes.")]
    public string ConnectionString =>
        Server.IsValueCreated && Server.Value.IsCompletedSuccessfully
            ? Server.Value.Result.ConnectionString
            : throw new InvalidOperationException("O PostgreSQL de testes ainda não foi iniciado.");

    public async Task InitializeAsync() => await Server.Value;

    // O container é do processo, não da classe de teste: ele é removido no fim da execução (e pelo Ryuk).
    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<RunningServer> StartServerAsync()
    {
        // Container genérico, sem a espera padrão de Testcontainers.PostgreSql: ela usa `exec` (pg_isready)
        // e a leitura de logs, que travam com o socket do podman rootless desta máquina (crun/oom_score_adj).
        // A prontidão é testada abrindo uma conexão (WaitUntilReadyAsync).
        var container = new ContainerBuilder("docker.io/library/postgres:17")
            .WithEnvironment("POSTGRES_PASSWORD", Password)
            .WithPortBinding(5432, true)
            // Banco descartável: durabilidade não importa, velocidade sim.
            .WithCommand("-c", MaxConnections, "-c", "fsync=off", "-c", "synchronous_commit=off", "-c", "full_page_writes=off")
            .Build();

        await container.StartAsync();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => container.DisposeAsync().AsTask().GetAwaiter().GetResult();

        var connectionString =
            $"Host={container.Hostname};Port={container.GetMappedPublicPort(5432)};Username=postgres;Password={Password};Database=postgres";
        await WaitUntilReadyAsync(connectionString);

        var server = new RunningServer(container, connectionString);
        await using var db = CreateContext(connectionString, new CurrentContext());
        await db.Database.EnsureCreatedAsync();

        return server;
    }

    private static async Task WaitUntilReadyAsync(string connectionString)
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (true)
        {
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();
                return;
            }
            catch (Exception) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(500);
            }
        }
    }

    /// <summary>Cria um banco vazio (sem tabelas) no mesmo servidor e devolve sua string de conexão.</summary>
    public async Task<string> CreateEmptyDatabaseAsync() => await CreateDatabaseAsync(template: null);

    /// <summary>
    /// Cria um banco novo copiando o banco modelo <paramref name="template"/> (<c>CREATE DATABASE … TEMPLATE</c>).
    /// O modelo é preparado uma única vez por processo, por <paramref name="prepare"/> (que recebe a conexão do modelo);
    /// chamadas concorrentes esperam a mesma preparação.
    /// </summary>
    public async Task<string> CreateDatabaseFromTemplateAsync(string template, Func<string, Task> prepare)
    {
        await Templates.GetOrAdd(template, name => new Lazy<Task>(() => PrepareTemplateAsync(name, prepare))).Value;

        return await CreateDatabaseAsync(template);
    }

    private async Task PrepareTemplateAsync(string template, Func<string, Task> prepare)
    {
        var connection = await CreateDatabaseAsync(template: null, name: template);
        await prepare(connection);

        // O modelo não pode ter conexões abertas, nem ociosas no pool, na hora de ser copiado.
        NpgsqlConnection.ClearPool(new NpgsqlConnection(connection));
    }

    private async Task<string> CreateDatabaseAsync(string? template, string? name = null)
    {
        name ??= $"finmy_{Guid.NewGuid():N}";
        var sql = template is null ? $"CREATE DATABASE {name}" : $"CREATE DATABASE {name} TEMPLATE {template}";

        await CreateGate.WaitAsync();
        try
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
        finally
        {
            CreateGate.Release();
        }

        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name }.ConnectionString;
    }

    /// <summary>Relógio fixo usado pelos interceptors quando o teste não informa outro.</summary>
    public static FakeClock DefaultClock { get; } = new(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));

    /// <summary>Contexto de testes com os interceptors reais e o <see cref="ICurrentContext"/> informado.</summary>
    public TestFinMyDbContext CreateContext(ICurrentContext currentContext, IClock? clock = null) =>
        CreateContext(ConnectionString, currentContext, clock);

    private static TestFinMyDbContext CreateContext(string connectionString, ICurrentContext currentContext, IClock? clock = null)
    {
        var builder = new DbContextOptionsBuilder<TestFinMyDbContext>();
        builder.UseFinMyNpgsql(connectionString).AddFinMyInterceptors(clock ?? DefaultClock);

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
