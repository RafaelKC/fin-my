using Serilog;

namespace FinMy.Api.Composition;

public static class LoggingSetup
{
    /// <summary>
    /// Serilog em JSON no console (docs/arquitetura.md §2). Níveis vêm da seção <c>Serilog</c>; sinks
    /// registrados no DI também recebem os eventos (usado pelos testes). Sem dados financeiros nem e-mail.
    /// </summary>
    public static IHostBuilder UseFinMySerilog(this IHostBuilder host) =>
        host.UseSerilog((context, services, logger) => logger
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .WriteTo.Console(new RedactingJsonFormatter()));
}
