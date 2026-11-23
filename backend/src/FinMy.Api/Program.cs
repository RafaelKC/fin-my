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

// Hangfire (jobs em segundo plano) só sobe quando há conexão com o PostgreSQL configurada.
var connectionString = builder.Configuration.GetConnectionString("Default");
if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddFinMyHangfire(connectionString);
    builder.Services.AddHangfireServer();
}

var app = builder.Build();

if (!string.IsNullOrWhiteSpace(connectionString))
{
    app.Services.GetRequiredService<IRecurringJobManager>().RegisterFinMyRecurringJobs();
}

app.UseFinMyPipeline();
await app.RunAsync();
return 0;

/// <summary>Marcador para <c>WebApplicationFactory&lt;Program&gt;</c> nos testes.</summary>
public partial class Program;
