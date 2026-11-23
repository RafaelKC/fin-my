using FinMy.Api.Composition;
using FinMy.Application.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FinMy.Api.Endpoints.Auth;

/// <summary><c>POST /api/v1/auth/refresh</c> e <c>POST /api/v1/auth/logout</c>: o refresh token vai só no cookie.</summary>
public sealed class SessionEndpoints : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(AuthEndpoints.Prefix).WithTags("auth").RequireRateLimiting(RateLimitPolicies.Auth);

        group.MapPost("/refresh", RefreshAsync);
        group.MapPost("/logout", LogoutAsync);
    }

    private static async Task<JsonHttpResult<SessionResponse>> RefreshAsync(
        RefreshHandler handler,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";

        SignUpResult result;
        try
        {
            result = await handler.HandleAsync(http.Request.ReadRefreshCookie(), cancellationToken);
        }
        catch (FinMy.Domain.Common.DomainException)
        {
            // Sessão inválida: o cookie não serve mais, o navegador pode descartá-lo.
            http.Response.DeleteRefreshCookie();
            throw;
        }

        http.Response.SetRefreshCookie(result.RefreshToken);

        var body = new SessionResponse(
            result.AccessToken,
            result.AccessTokenExpiresAt,
            new UserDto(result.User.Id, result.User.Name, result.User.Email),
            new TenantDto(result.Tenant.Id, result.Tenant.Name, result.Tenant.Role.ToString().ToLowerInvariant()));

        return TypedResults.Json(body, statusCode: StatusCodes.Status200OK);
    }

    private static async Task<NoContent> LogoutAsync(
        LogoutHandler handler,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        await handler.HandleAsync(http.Request.ReadRefreshCookie(), cancellationToken);
        http.Response.DeleteRefreshCookie();
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.NoContent();
    }
}
