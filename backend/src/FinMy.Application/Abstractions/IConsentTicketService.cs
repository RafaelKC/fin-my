using FinMy.Application.OAuth;

namespace FinMy.Application.Abstractions;

/// <summary>
/// Tickets de consentimento do OAuth (008 research R2): opacos, de uso único, 2 minutos. Só valem para o mesmo
/// <c>client_id</c>, <c>redirect_uri</c> e <c>code_challenge</c> para os quais foram emitidos.
/// </summary>
public interface IConsentTicketService
{
    Task<(IssuedConsentTicket? Issued, ConsentTicketRejection Rejection)> IssueAsync(
        Guid userId, Guid tenantId, string? clientId, string? redirectUri, string? codeChallenge, CancellationToken cancellationToken);

    /// <summary>Consome o ticket (uma única vez) se ele vale para exatamente este pedido; senão devolve <c>null</c>.</summary>
    Task<RedeemedConsent?> RedeemAsync(string ticket, string? clientId, string? redirectUri, string? codeChallenge, CancellationToken cancellationToken);
}
