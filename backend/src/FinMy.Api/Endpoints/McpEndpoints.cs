using FinMy.Api.Composition;
using FinMy.Api.Mcp;

namespace FinMy.Api.Endpoints;

/// <summary>
/// <c>/mcp</c> (008 research R1, R2): só aceita o token do conector (o esquema de autenticação é escolhido pelo caminho);
/// sem token ou com token inválido responde 401 com <c>WWW-Authenticate: Bearer resource_metadata="…"</c> (RFC 9728).
/// </summary>
public sealed class McpEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapMcp(McpSetup.Path).RequireAuthorization(AuthenticationSetup.McpResourcePolicy).ExcludeFromDescription();
    }
}
