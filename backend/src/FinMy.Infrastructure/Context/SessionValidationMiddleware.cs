using FinMy.Application.Abstractions;
using FinMy.Infrastructure.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FinMy.Infrastructure.Context;

/// <summary>
/// Confere, a cada requisição autenticada, se a sessão <c>sid</c> do token segue ativa (001 research R3), para que
/// logout e troca de senha valham na próxima ação. Sessão revogada responde 401 <c>session_revoked</c>.
/// Deve rodar depois de <c>UseAuthentication</c> e antes de <c>UseCurrentContext</c>.
/// </summary>
public sealed class SessionValidationMiddleware(RequestDelegate next)
{
    public const string SessionRevokedCode = "session_revoked";

    public async Task InvokeAsync(HttpContext httpContext, ISessionRegistry sessions)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(sessions);

        var user = httpContext.User;
        // Token do conector (OAuth, 008): a autorização é conferida a cada chamada pelo OpenIddict, não há sessão `sid`.
        if (user.Identity?.IsAuthenticated == true && !user.HasClaim(c => c.Type == TokenClaims.OAuthAuthorization))
        {
            // Sem `sid` válido o token não é um access token nosso: trata como sessão inválida.
            var active = Guid.TryParse(user.FindFirst(TokenClaims.Session)?.Value, out var sessionId)
                && sessionId != Guid.Empty
                && await sessions.IsActiveAsync(sessionId, httpContext.RequestAborted);

            if (!active)
            {
                await WriteRevokedAsync(httpContext);
                return;
            }
        }

        await next(httpContext);
    }

    private static async Task WriteRevokedAsync(HttpContext httpContext)
    {
        var problem = new ProblemDetails
        {
            Type = $"urn:finmy:problem:{SessionRevokedCode}",
            Title = "Sessão encerrada",
            Status = StatusCodes.Status401Unauthorized,
            Detail = "Sua sessão foi encerrada. Entre novamente para continuar.",
        };
        problem.Extensions["code"] = SessionRevokedCode;
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        var response = httpContext.Response;
        response.StatusCode = StatusCodes.Status401Unauthorized;
        response.Headers.WWWAuthenticate = "Bearer";
        await response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", httpContext.RequestAborted);
    }
}

public static class SessionValidationMiddlewareExtensions
{
    public static IApplicationBuilder UseSessionValidation(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<SessionValidationMiddleware>();
    }
}
