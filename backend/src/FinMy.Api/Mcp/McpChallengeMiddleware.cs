using FinMy.Api.Composition;
using Microsoft.Net.Http.Headers;

namespace FinMy.Api.Mcp;

/// <summary>
/// 401 do <c>/mcp</c> com <c>WWW-Authenticate: Bearer resource_metadata="&lt;APP_URL&gt;/.well-known/oauth-protected-resource"</c> (RFC 9728,
/// 008 research R2): é por ele que o Claude descobre onde autenticar. Mantém o <c>error</c> do OpenIddict quando há token inválido.
/// </summary>
public sealed class McpChallengeMiddleware(RequestDelegate next, IConfiguration configuration)
{
    public const string MetadataPath = "/.well-known/oauth-protected-resource";

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Request.Path.StartsWithSegments(McpSetup.Path, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.OnStarting(() =>
            {
                if (context.Response.StatusCode == StatusCodes.Status401Unauthorized)
                {
                    var existing = context.Response.Headers.WWWAuthenticate.ToString();
                    var rest = existing.StartsWith("Bearer", StringComparison.OrdinalIgnoreCase) ? existing["Bearer".Length..].Trim() : string.Empty;
                    var metadata = $"resource_metadata=\"{OAuthSetup.ResolveAppUrl(configuration)}{MetadataPath}\"";
                    context.Response.Headers[HeaderNames.WWWAuthenticate] = rest.Length == 0 ? $"Bearer {metadata}" : $"Bearer {metadata}, {rest}";
                }

                return Task.CompletedTask;
            });
        }

        return next(context);
    }
}
