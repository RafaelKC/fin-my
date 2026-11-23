using System.Diagnostics;
using FinMy.Api.Tests.Support;
using FinMy.Application.Tests.Infrastructure;
using Npgsql;

namespace FinMy.Api.Tests.Composition;

/// <summary>Executa <c>dotnet FinMy.Api.dll migrate</c> como processo, como o compose faz.</summary>
[Collection(ApiTestGroup.Name)]
public class MigrateCommandTests(PostgresFixture postgres)
{
    private static async Task<(int ExitCode, string Output)> RunMigrateAsync(string connection)
    {
        var dll = typeof(Program).Assembly.Location;
        var info = new ProcessStartInfo("dotnet", [dll, "migrate"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(dll)!,
        };
        info.Environment["ConnectionStrings__Default"] = connection;
        info.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";

        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        await process.WaitForExitAsync(timeout.Token);

        return (process.ExitCode, await stdout + await stderr);
    }

    private static async Task<bool> TableExistsAsync(string connection, string table)
    {
        await using var conn = new NpgsqlConnection(connection);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT to_regclass(@t) IS NOT NULL", conn);
        cmd.Parameters.AddWithValue("t", table);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task Migrate_aplica_as_migracoes_e_encerra_com_codigo_0()
    {
        var connection = await postgres.CreateEmptyDatabaseAsync();

        var (exitCode, output) = await RunMigrateAsync(connection);

        exitCode.ShouldBe(0, output);
        (await TableExistsAsync(connection, "users")).ShouldBeTrue();
        (await TableExistsAsync(connection, "tenants")).ShouldBeTrue();
    }

    [Fact]
    public async Task Migrate_e_idempotente()
    {
        var connection = await postgres.CreateEmptyDatabaseAsync();

        (await RunMigrateAsync(connection)).ExitCode.ShouldBe(0);
        var (exitCode, output) = await RunMigrateAsync(connection);

        exitCode.ShouldBe(0, output);
    }

    [Fact]
    public async Task Migrate_falha_com_codigo_1_quando_o_banco_nao_responde()
    {
        var (exitCode, _) = await RunMigrateAsync("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2");

        exitCode.ShouldBe(1);
    }
}
