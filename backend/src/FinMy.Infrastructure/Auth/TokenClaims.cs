namespace FinMy.Infrastructure.Auth;

/// <summary>Nomes das claims do access token (docs/arquitetura.md §7).</summary>
public static class TokenClaims
{
    public const string Subject = "sub";
    public const string Tenant = "tid";
    public const string Session = "sid";
    public const string Name = "name";
    public const string Channel = "channel";

    /// <summary>Id da autorização OAuth (claim privada do OpenIddict); só tokens do conector a trazem (008).</summary>
    public const string OAuthAuthorization = "oi_au_id";
}
