using FinMy.Application.Abstractions;
using FinMy.Domain.Common;
using FinMy.Domain.Wallets;

namespace FinMy.Application.Wallets;

/// <summary>Escolha da nova principal ao arquivar ou excluir a atual (research R6).</summary>
internal static class NewPrimaryRule
{
    /// <summary>
    /// A nova principal escolhida: inexistente no Espaço → 404; arquivada ou a própria → 422 <c>wallet.invalid_new_primary</c>.
    /// </summary>
    public static async Task<Wallet> ResolveAsync(IWalletStore wallets, Wallet leaving, Guid? newPrimaryWalletId, CancellationToken cancellationToken)
    {
        if (newPrimaryWalletId is null)
        {
            throw new DomainException(WalletErrors.NewPrimaryRequired);
        }

        var chosen = await wallets.FindAsync(newPrimaryWalletId.Value, cancellationToken) ?? throw new DomainException(WalletErrors.NotFound);
        if (chosen.Id == leaving.Id || chosen.IsArchived)
        {
            throw new DomainException(WalletErrors.InvalidNewPrimary);
        }

        return chosen;
    }
}
