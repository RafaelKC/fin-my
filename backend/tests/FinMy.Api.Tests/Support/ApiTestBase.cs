using FinMy.Api.Endpoints;
using FinMy.Application.Tests.Infrastructure;
using Npgsql;

namespace FinMy.Api.Tests.Support;

/// <summary>Nome do banco modelo (migrado uma vez por processo) a partir do qual cada teste cria o seu.</summary>
public static class ApiTemplateDatabase
{
    public const string Name = "finmy_template";

    /// <summary>Cria um banco já migrado copiando o modelo (migrado na primeira chamada do processo).</summary>
    public static Task<string> CreateAsync(PostgresFixture postgres) =>
        postgres.CreateDatabaseFromTemplateAsync(Name, async connection =>
        {
            await using var factory = new ApiFactory(connection);
            await factory.ApplyMigrationsAsync();
        });
}

/// <summary>Coleção para classes que mexem em estado global do processo (ex.: <c>Console.Out</c>): roda sozinha, sem paralelismo.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GlobalStateGroup
{
    public const string Name = "estado-global";
}

/// <summary>
/// Base dos testes HTTP: cada teste ganha um banco novo e já migrado e uma API própria.
/// Módulos extras de endpoints (<see cref="ExtraEndpoints"/>) e configurações (<see cref="Settings"/>)
/// podem ser sobrescritos por quem herda.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "O descarte acontece em IAsyncLifetime.DisposeAsync (xUnit).")]
public abstract class ApiTestBase(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private ApiFactory? _factory;

    protected virtual IReadOnlyDictionary<string, string?>? Settings => null;

    protected virtual IEndpointModule[] ExtraEndpoints => [];

    /// <summary>Relógio controlável no lugar do relógio real; <c>null</c> mantém o do sistema.</summary>
    protected virtual FinMy.Application.Abstractions.IClock? Clock => null;

    /// <summary>Usa o contexto de testes com as sondas <c>ISoftDeletable</c> (veja <see cref="ApiFactory.UseProbeModel"/>).</summary>
    protected virtual bool UseProbeModel => false;

    protected ApiFactory Factory => _factory ?? throw new InvalidOperationException("Factory ainda não inicializada.");

    protected HttpClient Client { get; private set; } = null!;

    /// <summary>String de conexão do banco próprio deste teste.</summary>
    protected string DatabaseConnection { get; private set; } = null!;

    protected PostgresFixture Postgres => postgres;

    public async Task InitializeAsync()
    {
        // O contexto de sondas cria o esquema por EnsureCreated (sem migrações): parte de um banco vazio.
        DatabaseConnection = UseProbeModel
            ? await postgres.CreateEmptyDatabaseAsync()
            : await ApiTemplateDatabase.CreateAsync(postgres);
        _factory = new ApiFactory(DatabaseConnection, Settings, Clock, ExtraEndpoints) { UseProbeModel = UseProbeModel };
        await (UseProbeModel ? _factory.MigrateAsync() : _factory.SeedAsync());
        Client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        // Cada classe usa um banco próprio e, portanto, um pool próprio: sem limpar, as conexões ociosas
        // se acumulam entre as classes e estouram o max_connections do Postgres ("too many clients").
        if (DatabaseConnection is not null)
        {
            NpgsqlConnection.ClearPool(new NpgsqlConnection(DatabaseConnection));
        }
    }
}
