using FinMy.Api.Endpoints;
using FinMy.Application.Tests.Infrastructure;
using Npgsql;

namespace FinMy.Api.Tests.Support;

/// <summary>Coleção que compartilha um único PostgreSQL entre as classes de teste da Api.</summary>
[CollectionDefinition(Name)]
public sealed class ApiTestGroup : ICollectionFixture<PostgresFixture>
{
    public const string Name = "api";
}

/// <summary>
/// Base dos testes HTTP: cada classe ganha um banco novo e já migrado e uma API própria.
/// Módulos extras de endpoints (<see cref="ExtraEndpoints"/>) e configurações (<see cref="Settings"/>)
/// podem ser sobrescritos por quem herda.
/// </summary>
[Collection(ApiTestGroup.Name)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "O descarte acontece em IAsyncLifetime.DisposeAsync (xUnit).")]
public abstract class ApiTestBase(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory? _factory;

    protected virtual IReadOnlyDictionary<string, string?>? Settings => null;

    protected virtual IEndpointModule[] ExtraEndpoints => [];

    /// <summary>Relógio controlável no lugar do relógio real; <c>null</c> mantém o do sistema.</summary>
    protected virtual FinMy.Application.Abstractions.IClock? Clock => null;

    protected ApiFactory Factory => _factory ?? throw new InvalidOperationException("Factory ainda não inicializada.");

    protected HttpClient Client { get; private set; } = null!;

    /// <summary>String de conexão do banco próprio deste teste.</summary>
    protected string DatabaseConnection { get; private set; } = null!;

    protected PostgresFixture Postgres => postgres;

    public async Task InitializeAsync()
    {
        DatabaseConnection = await postgres.CreateEmptyDatabaseAsync();
        _factory = new ApiFactory(DatabaseConnection, Settings, Clock, ExtraEndpoints);
        await _factory.MigrateAsync();
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
