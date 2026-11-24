using FinMy.Api.Endpoints;
using FinMy.Application.Abstractions;
using FinMy.Domain.Identity;
using FinMy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FinMy.Api.Tests.OAuth;

/// <summary>Sonda de teste sob <c>/mcp</c> (o servidor MCP real é da T003): expõe o <see cref="ICurrentContext"/> visto por um token do conector.</summary>
public sealed class OAuthProbeEndpoints : IEndpointModule
{
    public const string WhoAmIPath = "/mcp/test/whoami";
    public const string AuditedPath = "/mcp/test/audited";

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet(WhoAmIPath, (ICurrentContext context) => new
        {
            context.IsAuthenticated,
            context.UserId,
            context.TenantId,
            Channel = context.Channel.ToString(),
        }).RequireAuthorization();

        // Grava algo auditado para conferir `created_by`/`created_via` preenchidos a partir do token do conector.
        app.MapPost(AuditedPath, async (FinMyDbContext db) =>
        {
            var tenant = new Tenant("Espaço da sonda");
            db.Add(tenant);
            await db.SaveChangesAsync();
            return Results.Ok(new { tenant.CreatedBy, CreatedVia = tenant.CreatedVia.ToString() });
        }).RequireAuthorization();
    }
}

public sealed record McpWhoAmI(bool IsAuthenticated, Guid? UserId, Guid? TenantId, string Channel);
