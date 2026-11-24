using FinMy.Api.Mcp.Tools;

namespace FinMy.Api.Mcp;

/// <summary>Servidor MCP do conector do Claude (008 research R1): SDK oficial, Streamable HTTP, sem estado de sessão, em <c>/mcp</c>.</summary>
public static class McpSetup
{
    public const string Path = "/mcp";

    public const string Instructions =
        "Fin-My: finanças pessoais do Espaço da pessoa conectada. Valores em reais, datas AAAA-MM-DD, meses AAAA-MM. "
        + "Use list_catalog para conhecer carteiras, cartões e tags; os nomes são comparados de forma exata. "
        + "Toda consulta devolve `today` para interpretar \"hoje\", \"ontem\" e \"esse mês\".";

    public static IServiceCollection AddFinMyMcp(this IServiceCollection services)
    {
        services.AddMcpServer(options =>
            {
                options.ServerInfo = new ModelContextProtocol.Protocol.Implementation { Name = "fin-my", Version = "1.0.0" };
                options.ServerInstructions = Instructions;
            })
            .WithHttpTransport(options => options.Stateless = true)
            .WithTools<CatalogTools>()
            .WithTools<MonthTools>()
            .WithTools<InvoiceTools>()
            .WithTools<CreationTools>()
            .WithTools<ConfirmationTools>();

        services.AddScoped<McpIdempotency>();

        return services;
    }
}
