using FinMy.Application.Abstractions;
using FinMy.Domain.Recurrences;

namespace FinMy.Application.Recurrences;

/// <summary>
/// Diz se a recorrência precisa de revisão: carteira, cartão ou tag arquivados (005 Edge Case, research R2). Nesse caso o
/// gerador não cria nada e a API devolve <c>needsReview</c> com o motivo.
/// </summary>
public sealed class RecurrenceReviewer(IEntryStore wallets, ICreditCardStore cards, ITagStore tags)
{
    public const string WalletArchived = "wallet_archived";
    public const string CardArchived = "card_archived";
    public const string TagArchived = "tag_archived";

    /// <summary>O primeiro motivo (carteira, cartão, tag), ou <c>null</c> se está tudo ativo.</summary>
    public async Task<string?> ReasonAsync(Recurrence recurrence, IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recurrence);
        ArgumentNullException.ThrowIfNull(tagIds);

        if (recurrence.WalletId is { } walletId
            && await wallets.GetWalletAsync(walletId, cancellationToken) is { IsArchived: true })
        {
            return WalletArchived;
        }

        if (recurrence.CreditCardId is { } cardId
            && await cards.FindAsync(cardId, cancellationToken) is { IsArchived: true })
        {
            return CardArchived;
        }

        if (tagIds.Count > 0 && (await tags.FindManyAsync(tagIds, cancellationToken)).Any(t => t.IsArchived))
        {
            return TagArchived;
        }

        return null;
    }
}
