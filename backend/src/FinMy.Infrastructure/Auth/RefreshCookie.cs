using Microsoft.AspNetCore.Http;

namespace FinMy.Infrastructure.Auth;

/// <summary>
/// Cookie do refresh token, conforme contracts/openapi.yaml <c>RefreshCookie</c>:
/// <c>finmy_rt=&lt;token&gt;; HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth; Max-Age=7776000</c>.
/// </summary>
public static class RefreshCookie
{
    public const string Name = "finmy_rt";

    public const string Path = "/api/v1/auth";

    private static CookieOptions Options(TimeSpan? maxAge) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = Path,
        MaxAge = maxAge,
    };

    public static void Append(HttpResponse response, string token)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Cookies.Append(Name, token, Options(RefreshTokenStore.Lifetime));
    }

    /// <summary>Apaga o cookie (logout), com os mesmos atributos para o navegador reconhecê-lo.</summary>
    public static void Delete(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Cookies.Delete(Name, Options(null));
    }

    public static string? Read(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Cookies.TryGetValue(Name, out var token) && !string.IsNullOrWhiteSpace(token) ? token : null;
    }
}
