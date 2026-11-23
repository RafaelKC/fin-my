using System.Security.Claims;
using FinMy.Api.Composition;
using FinMy.Api.Endpoints.Auth;
using FinMy.Application.Auth;
using FinMy.Application.Profile;
using FinMy.Infrastructure.Auth;

namespace FinMy.Api.Endpoints.Profile;

/// <summary>Contexto e perfil (US2/US5): <c>GET /me</c>, <c>PATCH /me</c>, <c>POST /me/password</c> e <c>PATCH /tenant</c> (contracts/openapi.yaml).</summary>
public sealed class MeEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/v1").WithTags("me").RequireAuthorization();

        group.MapGet("/me", async (GetMeHandler handler, HttpContext http, CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(cancellationToken);
            http.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new MeResponse(ToDto(result.User), ToDto(result.Tenant)));
        });

        group.MapPatch("/me", async (RenameRequest body, UpdateMyNameHandler handler, CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new UpdateMyNameCommand(body.Name), cancellationToken);
            return Results.Ok(new MeResponse(ToDto(result.User), ToDto(result.Tenant)));
        });

        // A senha atual é conferida a cada pedido: o mesmo limite por IP do /auth contém tentativas com token roubado.
        group.MapPost("/me/password", async (ChangePasswordRequest body, ClaimsPrincipal user, ChangePasswordHandler handler, CancellationToken cancellationToken) =>
        {
            var sessionId = Guid.TryParse(user.FindFirst(TokenClaims.Session)?.Value, out var sid) ? sid : Guid.Empty;
            await handler.HandleAsync(new ChangePasswordCommand(body.CurrentPassword, body.NewPassword, sessionId), cancellationToken);
            return Results.NoContent();
        }).RequireRateLimiting(RateLimitPolicies.Auth);

        group.MapPatch("/tenant", async (RenameRequest body, RenameTenantHandler handler, CancellationToken cancellationToken) =>
        {
            var tenant = await handler.HandleAsync(new RenameTenantCommand(body.Name), cancellationToken);
            return Results.Ok(ToDto(tenant));
        });
    }

    private static UserDto ToDto(UserSummary user) => new(user.Id, user.Name, user.Email);

    private static TenantDto ToDto(TenantSummary tenant) => new(tenant.Id, tenant.Name, tenant.Role.ToString().ToLowerInvariant());
}
