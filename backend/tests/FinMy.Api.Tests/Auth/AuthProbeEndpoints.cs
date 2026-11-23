using FinMy.Api.Endpoints;
using FinMy.Application.Abstractions;
using FinMy.Domain.Identity;
using FinMy.Infrastructure.Auth;
using FinMy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace FinMy.Api.Tests.Auth;

/// <summary>Sondas só de teste: expõem o <see cref="ICurrentContext"/> e o cookie de refresh.</summary>
public sealed class AuthProbeEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/test/whoami", (ICurrentContext context) => Describe(context)).RequireAuthorization();
        app.MapGet("/test/maybe-whoami", (ICurrentContext context) => Describe(context));
        app.MapPost("/test/audited", async (ICurrentContext context, FinMyDbContext db) =>
        {
            var tenant = new Tenant("Espaço da sonda");
            db.Add(tenant);
            await db.SaveChangesAsync();
            return Results.Ok(new { tenant.Id, tenant.CreatedBy, CreatedVia = tenant.CreatedVia.ToString() });
        }).RequireAuthorization();

        app.MapPost("/test/cookie/set", (HttpResponse response) => RefreshCookie.Append(response, "valor-do-token"));
        app.MapPost("/test/cookie/clear", (HttpResponse response) => RefreshCookie.Delete(response));
        app.MapGet("/test/cookie/read", (HttpRequest request) => Results.Ok(new { token = RefreshCookie.Read(request) }));
    }

    private static object Describe(ICurrentContext context) => new
    {
        context.IsAuthenticated,
        context.IsSystem,
        context.UserId,
        context.TenantId,
        Channel = context.Channel.ToString(),
    };
}

public sealed record WhoAmI(bool IsAuthenticated, bool IsSystem, Guid? UserId, Guid? TenantId, string Channel);
