using FinMy.Infrastructure.Auth;

namespace FinMy.Api.Composition;

/// <summary>Ponte entre os endpoints e o cookie de refresh da Infrastructure (os endpoints não tocam a Infrastructure).</summary>
public static class RefreshCookieResponse
{
    public static void SetRefreshCookie(this HttpResponse response, string token) => RefreshCookie.Append(response, token);
}
