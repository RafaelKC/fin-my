using FinMy.Infrastructure.Jobs;
using Hangfire;

var builder = WebApplication.CreateBuilder(args);

// Hangfire (jobs em segundo plano) só sobe quando há conexão com o PostgreSQL configurada.
var connectionString = builder.Configuration.GetConnectionString("Default");
if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddFinMyHangfire(connectionString);
    builder.Services.AddHangfireServer();
}

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.Run();
