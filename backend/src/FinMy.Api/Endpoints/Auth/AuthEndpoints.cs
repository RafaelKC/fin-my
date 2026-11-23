using FinMy.Api.Composition;
using FinMy.Application.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FinMy.Api.Endpoints.Auth;

/// <summary>Endpoints de autenticação (<c>/api/v1/auth</c>), com limite de requisições por IP (research R5).</summary>
public sealed class AuthEndpoints : IEndpointModule
{
    public const string Prefix = "/api/v1/auth";

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Prefix).WithTags("auth").RequireRateLimiting(RateLimitPolicies.Auth);

        group.MapPost("/signup", SignUpAsync);
    }

    private static async Task<JsonHttpResult<SessionResponse>> SignUpAsync(
        SignUpRequest request,
        SignUpHandler handler,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var userAgent = http.Request.Headers.UserAgent.ToString();
        var result = await handler.HandleAsync(
            new SignUpCommand(request.Name, request.Email, request.Password, userAgent),
            cancellationToken);

        http.Response.SetRefreshCookie(result.RefreshToken);
        http.Response.Headers.CacheControl = "no-store";

        var body = new SessionResponse(
            result.AccessToken,
            result.AccessTokenExpiresAt,
            new UserDto(result.User.Id, result.User.Name, result.User.Email),
            new TenantDto(result.Tenant.Id, result.Tenant.Name, result.Tenant.Role.ToString().ToLowerInvariant()));

        return TypedResults.Json(body, statusCode: StatusCodes.Status201Created);
    }
}
