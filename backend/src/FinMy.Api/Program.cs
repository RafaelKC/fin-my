using FinMy.Api.Composition;
using FinMy.Infrastructure.Jobs;
using Hangfire;

if (MigrateCommand.IsRequested(args))
{
    return await MigrateCommand.RunAsync(args);
}

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseFinMySerilog();
builder.Services.AddFinMyApi(builder.Configuration);

// Hangfire (jobs em segundo plano) só sobe quando há conexão com o PostgreSQL configurada
// e `Hangfire:Enabled` não está desligado (os testes HTTP o desligam para poupar conexões).
var connectionString = builder.Configuration.GetConnectionString("Default");
var hangfireEnabled = !string.IsNullOrWhiteSpace(connectionString) && builder.Configuration.GetValue("Hangfire:Enabled", true);
if (hangfireEnabled)
{
    builder.Services.AddFinMyHangfire(connectionString!);
    builder.Services.AddHangfireServer();
}

var app = builder.Build();

if (hangfireEnabled)
{
    app.Services.GetRequiredService<IRecurringJobManager>().RegisterFinMyRecurringJobs();
}

app.UseFinMyPipeline();
await app.RunAsync();
return 0;

/// <summary>Marcador para <c>WebApplicationFactory&lt;Program&gt;</c> nos testes.</summary>
public partial class Program;
