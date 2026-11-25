using System.Diagnostics;
using FinMy.Api.Tests.Support;
using FinMy.Application.Tests.Infrastructure;
using Npgsql;

namespace FinMy.Api.Tests.Composition;

/// <summary>Executa <c>dotnet FinMy.Api.dll migrate</c> como processo, como o compose faz.</summary>
public class MigrateCommandTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static async Task<(int ExitCode, string Output)> RunMigrateAsync(string connection, string? androidRedirect = null)
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
        if (androidRedirect is not null)
        {
            info.Environment["Android__RedirectUri"] = androidRedirect;
        }

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
    public async Task Migrate_semeia_o_cliente_finmy_android_uma_so_vez_e_alinha_o_redirect_configurado()
    {
        var connection = await postgres.CreateEmptyDatabaseAsync();

        (await RunMigrateAsync(connection)).ExitCode.ShouldBe(0);
        (await RunMigrateAsync(connection)).ExitCode.ShouldBe(0);
        (await ReadAndroidClientAsync(connection)).ShouldBe([("finmy-android", "App Fin-My (Android)", "br.app.finmy:/oauth/callback")]);

        (await RunMigrateAsync(connection, androidRedirect: "br.app.finmy.teste:/cb")).ExitCode.ShouldBe(0);
        (await ReadAndroidClientAsync(connection)).ShouldBe([("finmy-android", "App Fin-My (Android)", "br.app.finmy.teste:/cb")]);
    }

    private static async Task<List<(string ClientId, string Name, string Redirects)>> ReadAndroidClientAsync(string connection)
    {
        await using var conn = new NpgsqlConnection(connection);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT client_id, display_name, redirect_uris FROM openiddict_applications", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<(string, string, string)>();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetString(1), System.Text.Json.JsonSerializer.Deserialize<string[]>(reader.GetString(2))!.Single()));
        }

        return rows;
    }

    [Fact]
    public async Task Migrate_falha_com_codigo_1_quando_o_banco_nao_responde()
    {
        var (exitCode, _) = await RunMigrateAsync("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2");

        exitCode.ShouldBe(1);
    }
}
