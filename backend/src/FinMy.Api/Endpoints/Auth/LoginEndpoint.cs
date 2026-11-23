using FinMy.Api.Composition;
using FinMy.Application.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FinMy.Api.Endpoints.Auth;

/// <summary><c>POST /api/v1/auth/login</c>, com o rate limiter por IP <c>auth</c> (research R5).</summary>
public sealed class LoginEndpoint : IEndpointModule
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(AuthEndpoints.Prefix).WithTags("auth").RequireRateLimiting(RateLimitPolicies.Auth);

        group.MapPost("/login", LoginAsync);
    }

    private static async Task<JsonHttpResult<SessionResponse>> LoginAsync(
        LoginRequest request,
        LoginHandler handler,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var userAgent = http.Request.Headers.UserAgent.ToString();
        var result = await handler.HandleAsync(new LoginCommand(request.Email, request.Password, userAgent), cancellationToken);

        http.Response.SetRefreshCookie(result.RefreshToken);
        http.Response.Headers.CacheControl = "no-store";

        var body = new SessionResponse(
            result.AccessToken,
            result.AccessTokenExpiresAt,
            new UserDto(result.User.Id, result.User.Name, result.User.Email),
            new TenantDto(result.Tenant.Id, result.Tenant.Name, result.Tenant.Role.ToString().ToLowerInvariant()));

        return TypedResults.Json(body, statusCode: StatusCodes.Status200OK);
    }
}
