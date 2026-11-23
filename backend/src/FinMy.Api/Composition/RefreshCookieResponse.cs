using FinMy.Infrastructure.Auth;

namespace FinMy.Api.Composition;

/// <summary>Ponte entre os endpoints e o cookie de refresh da Infrastructure (os endpoints não tocam a Infrastructure).</summary>
public static class RefreshCookieResponse
{
    public static void SetRefreshCookie(this HttpResponse response, string token) => RefreshCookie.Append(response, token);

    public static void DeleteRefreshCookie(this HttpResponse response) => RefreshCookie.Delete(response);

    /// <summary>O refresh token enviado pelo navegador no cookie <c>finmy_rt</c>, ou <c>null</c>.</summary>
    public static string? ReadRefreshCookie(this HttpRequest request) => RefreshCookie.Read(request);
}
