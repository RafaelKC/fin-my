using FinMy.Application.Abstractions;
using FinMy.Domain.Common;

namespace FinMy.Application.Wallets;

/// <summary>Desarquiva; só vira principal se o Espaço ficou sem nenhuma ativa (research R6).</summary>
public sealed class UnarchiveWalletHandler(IWalletStore wallets)
{
    public async Task<WalletView> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var wallet = await wallets.FindAsync(id, cancellationToken) ?? throw new DomainException(WalletErrors.NotFound);

        if (wallet.IsArchived)
        {
            var othersActive = await wallets.AnyActiveAsync(wallet.Id, cancellationToken);
            wallet.Unarchive();
            if (!othersActive)
            {
                wallet.MarkPrimary();
            }

            await wallets.SaveAsync(cancellationToken);
        }

        return await wallets.GetAsync(id, cancellationToken) ?? throw new DomainException(WalletErrors.NotFound);
    }
}
