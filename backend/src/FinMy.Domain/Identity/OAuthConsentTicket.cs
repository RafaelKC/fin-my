using FinMy.Domain.Common;

namespace FinMy.Domain.Identity;

/// <summary>
/// Ticket de consentimento do OAuth (008 research R2, <c>oauth_consent_tickets</c>): prova, por 2 minutos e uma única vez,
/// que a pessoa logada no app autorizou aquele pedido (<c>client_id</c>, <c>redirect_uri</c>, <c>code_challenge</c>).
/// Só o hash do ticket é guardado.
/// </summary>
public sealed class OAuthConsentTicket : IEntity
{
    /// <summary>Validade do ticket.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    // Construtor para o EF Core.
    private OAuthConsentTicket()
    {
    }

    public OAuthConsentTicket(byte[] ticketHash, Guid userId, Guid tenantId, string clientId, string redirectUri, string codeChallenge, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        TicketHash = ticketHash;
        UserId = userId;
        TenantId = tenantId;
        ClientId = clientId;
        RedirectUri = redirectUri;
        CodeChallenge = codeChallenge;
        CreatedAt = now;
        ExpiresAt = now + Lifetime;
    }

    public Guid Id { get; private set; }

    public byte[] TicketHash { get; private set; } = [];

    public Guid UserId { get; private set; }

    public Guid TenantId { get; private set; }

    public string ClientId { get; private set; } = string.Empty;

    public string RedirectUri { get; private set; } = string.Empty;

    public string CodeChallenge { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }
}
