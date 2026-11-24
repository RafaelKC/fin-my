namespace FinMy.Domain.Identity;

/// <summary>
/// Último uso de uma autorização OAuth do conector (008 research R3, <c>oauth_authorization_usage</c>): o app lista isso em
/// "Aplicativos conectados". Tabela global; a autorização em si é do OpenIddict.
/// </summary>
public sealed class OAuthAuthorizationUsage
{
    // Construtor para o EF Core.
    private OAuthAuthorizationUsage()
    {
    }

    public OAuthAuthorizationUsage(string authorizationId, Guid userId, DateTimeOffset lastUsedAt)
    {
        AuthorizationId = authorizationId;
        UserId = userId;
        LastUsedAt = lastUsedAt;
    }

    public string AuthorizationId { get; private set; } = string.Empty;

    public Guid UserId { get; private set; }

    public DateTimeOffset LastUsedAt { get; private set; }
}
