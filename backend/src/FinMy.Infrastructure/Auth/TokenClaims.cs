namespace FinMy.Infrastructure.Auth;

/// <summary>Nomes das claims do access token (docs/arquitetura.md §7).</summary>
public static class TokenClaims
{
    public const string Subject = "sub";
    public const string Tenant = "tid";
    public const string Session = "sid";
    public const string Name = "name";
    public const string Channel = "channel";
}
