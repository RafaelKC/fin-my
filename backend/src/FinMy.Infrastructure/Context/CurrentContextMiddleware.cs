using FinMy.Application.Abstractions;
using FinMy.Domain.Common;
using FinMy.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace FinMy.Infrastructure.Context;

/// <summary>
/// Preenche o <see cref="CurrentContext"/> a partir das claims <b>já validadas</b> pelo <c>JwtBearer</c>
/// (<c>sub</c>, <c>tid</c>, <c>channel</c>). Nunca lê o token por conta própria. Deve rodar depois de
/// <c>UseAuthentication</c>. Requisição anônima segue com o contexto vazio.
/// </summary>
public sealed class CurrentContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext httpContext, CurrentContext currentContext, IConnectedAppsStore connectedApps)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(currentContext);

        var user = httpContext.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            if (!TryReadClaims(user, out var userId, out var tenantId, out var channel))
            {
                // Token assinado por nós, mas sem as claims esperadas: trata como não autenticado.
                await httpContext.ChallengeAsync();
                return;
            }

            currentContext.Populate(userId, tenantId, channel);

            // Token do conector (008): anota o último uso da autorização para "Aplicativos conectados" (no máximo 1×/5 min).
            if (user.FindFirst(TokenClaims.OAuthAuthorization)?.Value is { Length: > 0 } authorizationId)
            {
                await connectedApps.RecordUseAsync(authorizationId, userId, httpContext.RequestAborted);
            }
        }

        await next(httpContext);
    }

    private static bool TryReadClaims(System.Security.Claims.ClaimsPrincipal user, out Guid userId, out Guid tenantId, out Channel channel)
    {
        channel = default;
        tenantId = default;

        return Guid.TryParse(user.FindFirst(TokenClaims.Subject)?.Value, out userId)
            && Guid.TryParse(user.FindFirst(TokenClaims.Tenant)?.Value, out tenantId)
            && userId != Guid.Empty
            && tenantId != Guid.Empty
            && TryParseChannel(user.FindFirst(TokenClaims.Channel)?.Value, out channel);
    }

    // O canal `system` nunca vem num token: só jobs entram em escopo de sistema.
    private static bool TryParseChannel(string? value, out Channel channel)
        => ChannelCodes.TryParse(value, out channel) && channel is Channel.App or Channel.Claude or Channel.AndroidWidget;
}

public static class CurrentContextMiddlewareExtensions
{
    public static IApplicationBuilder UseCurrentContext(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<CurrentContextMiddleware>();
    }
}
