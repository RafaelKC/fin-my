using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Api.Composition;

/// <summary><c>dotnet FinMy.Api.dll migrate</c>: aplica as migrações pendentes e encerra (research R8).</summary>
public static partial class MigrateCommand
{
    public const string Name = "migrate";

    public static bool IsRequested(string[] args) => args.Length > 0 && string.Equals(args[0], Name, StringComparison.OrdinalIgnoreCase);

    /// <returns>0 em sucesso; 1 em falha.</returns>
    public static async Task<int> RunAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args.Skip(1).ToArray());
        builder.Host.UseFinMySerilog();
        builder.Services.AddFinMyInfrastructure(builder.Configuration);

        await using var app = builder.Build();
        var logger = app.Services.GetRequiredService<ILogger<WebApplication>>();

        try
        {
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<FinMyDbContext>();

            var pending = (await db.Database.GetPendingMigrationsAsync()).Count();
            await db.Database.MigrateAsync();
            LogApplied(logger, pending);
            return 0;
        }
        catch (Exception exception)
        {
            LogFailed(logger, exception);
            return 1;
        }
        finally
        {
            await Serilog.Log.CloseAndFlushAsync();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Migrações aplicadas: {Count}")]
    private static partial void LogApplied(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Falha ao aplicar as migrações")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
