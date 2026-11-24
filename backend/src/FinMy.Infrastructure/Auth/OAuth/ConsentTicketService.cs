using System.Security.Cryptography;
using System.Text;
using FinMy.Application.Abstractions;
using FinMy.Application.OAuth;
using FinMy.Domain.Identity;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace FinMy.Infrastructure.Auth.OAuth;

/// <summary>
/// Emite e consome os tickets de consentimento (008 research R2): opacos, de uso único, 2 minutos, só o hash em
/// <c>oauth_consent_tickets</c>. O ticket só vale para o mesmo <c>client_id</c>, <c>redirect_uri</c> e <c>code_challenge</c>.
/// </summary>
public sealed class ConsentTicketService(FinMyDbContext db, IOpenIddictApplicationManager applications, IClock clock) : IConsentTicketService
{
    private const int ChallengeMinLength = 43;
    private const int ChallengeMaxLength = 128;

    public async Task<(IssuedConsentTicket? Issued, ConsentTicketRejection Rejection)> IssueAsync(
        Guid userId, Guid tenantId, string? clientId, string? redirectUri, string? codeChallenge, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientId) || await applications.FindByClientIdAsync(clientId, cancellationToken) is not { } application)
        {
            return (null, ConsentTicketRejection.UnknownClient);
        }

        var registered = await applications.GetRedirectUrisAsync(application, cancellationToken);
        if (string.IsNullOrEmpty(redirectUri) || !registered.Contains(redirectUri, StringComparer.Ordinal))
        {
            return (null, ConsentTicketRejection.RedirectNotRegistered);
        }

        if (codeChallenge is null || codeChallenge.Length is < ChallengeMinLength or > ChallengeMaxLength)
        {
            return (null, ConsentTicketRejection.InvalidChallenge);
        }

        var ticket = Base64Url(RandomNumberGenerator.GetBytes(32));
        var entity = new OAuthConsentTicket(Hash(ticket), userId, tenantId, clientId, redirectUri, codeChallenge, clock.UtcNow);
        db.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return (new IssuedConsentTicket(ticket, entity.ExpiresAt), ConsentTicketRejection.None);
    }

    /// <summary>Consome o ticket (uma única vez) se ele vale para exatamente este pedido; senão devolve <c>null</c>.</summary>
    public async Task<RedeemedConsent?> RedeemAsync(string ticket, string? clientId, string? redirectUri, string? codeChallenge, CancellationToken cancellationToken)
    {
        var hash = Hash(ticket);
        var now = clock.UtcNow;

        var found = await db.Set<OAuthConsentTicket>().AsNoTracking().SingleOrDefaultAsync(t => t.TicketHash == hash, cancellationToken);
        if (found is null
            || found.UsedAt is not null
            || found.ExpiresAt <= now
            || !string.Equals(found.ClientId, clientId, StringComparison.Ordinal)
            || !string.Equals(found.RedirectUri, redirectUri, StringComparison.Ordinal)
            || !string.Equals(found.CodeChallenge, codeChallenge, StringComparison.Ordinal))
        {
            return null;
        }

        // Uso único mesmo com duas requisições simultâneas: só uma consegue marcar o ticket.
        var claimed = await db.Set<OAuthConsentTicket>()
            .Where(t => t.Id == found.Id && t.UsedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.UsedAt, now), cancellationToken);
        if (claimed == 0)
        {
            return null;
        }

        var name = await db.Set<User>().AsNoTracking().Where(u => u.Id == found.UserId).Select(u => u.Name).SingleOrDefaultAsync(cancellationToken);
        return name is null ? null : new RedeemedConsent(found.UserId, found.TenantId, name);
    }

    private static byte[] Hash(string ticket) => SHA256.HashData(Encoding.UTF8.GetBytes(ticket));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
