using FinMy.Application.Abstractions;
using FinMy.Domain.Common;

namespace FinMy.Application.Wallets;

/// <summary>Torna a carteira a principal (FR-002): desmarca a antiga e marca a nova na mesma transação (research R6).</summary>
public sealed class MakePrimaryWalletHandler(IWalletStore wallets)
{
    public async Task<WalletView> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var wallet = await wallets.FindAsync(id, cancellationToken) ?? throw new DomainException(WalletErrors.NotFound);

        if (!wallet.IsPrimary)
        {
            if (wallet.IsArchived)
            {
                throw new DomainException(WalletErrors.Archived);
            }

            await using var transaction = await wallets.BeginTransactionAsync(cancellationToken);
            var current = await wallets.FindPrimaryAsync(cancellationToken);
            current?.UnmarkPrimary();
            await wallets.SaveAsync(cancellationToken);

            wallet.MarkPrimary();
            await wallets.SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await wallets.GetAsync(id, cancellationToken) ?? throw new DomainException(WalletErrors.NotFound);
    }
}
