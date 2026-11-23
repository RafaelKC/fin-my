using System.Net;
using System.Text.Json;
using FinMy.Api.Tests.Support;
using FinMy.Application.Tests.Infrastructure;

namespace FinMy.Api.Tests.Health;

public class HealthTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    [Fact]
    public async Task Health_responde_200_com_servidor_e_banco_saudaveis()
    {
        var response = await Client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("status").GetString().ShouldBe("healthy");
        json.RootElement.GetProperty("server").GetString().ShouldBe("healthy");
        json.RootElement.GetProperty("database").GetString().ShouldBe("healthy");
    }
}

public class HealthWithoutDatabaseTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    [Fact]
    public async Task Health_responde_503_quando_o_banco_cai_e_nao_vaza_detalhes()
    {
        // A API sobe com o banco no ar; depois ele some (conexões encerradas à força).
        var name = new Npgsql.NpgsqlConnectionStringBuilder(DatabaseConnection).Database;
        await using (var admin = new Npgsql.NpgsqlConnection(Postgres.ConnectionString))
        {
            await admin.OpenAsync();
            await using var drop = new Npgsql.NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }

        var response = await Client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("server").GetString().ShouldBe("healthy");
        json.RootElement.GetProperty("database").GetString().ShouldBe("unhealthy");
        body.ShouldNotContain("Npgsql");
    }
}
