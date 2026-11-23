using FinMy.Application.Abstractions;
using FinMy.Domain.Common;

namespace FinMy.Application.Wallets;

/// <summary>
/// Arquiva (RT-11, US1.4). Arquivar a principal exige <c>newPrimaryWalletId</c>; a única ativa não pode ser arquivada.
/// </summary>
public sealed class ArchiveWalletHandler(IWalletStore wallets, IClock clock)
{
    public async Task<WalletView> HandleAsync(Guid id, Guid? newPrimaryWalletId, CancellationToken cancellationToken)
    {
        var wallet = await wallets.FindAsync(id, cancellationToken) ?? throw new DomainException(WalletErrors.NotFound);

        if (!wallet.IsArchived)
        {
            if (wallet.IsPrimary)
            {
                await ArchivePrimaryAsync(wallet, newPrimaryWalletId, cancellationToken);
            }
            else
            {
                wallet.Archive(clock.UtcNow);
                await wallets.SaveAsync(cancellationToken);
            }
        }

        return await wallets.GetAsync(id, cancellationToken) ?? throw new DomainException(WalletErrors.NotFound);
    }

    private async Task ArchivePrimaryAsync(Domain.Wallets.Wallet wallet, Guid? newPrimaryWalletId, CancellationToken cancellationToken)
    {
        if (!await wallets.AnyActiveAsync(wallet.Id, cancellationToken))
        {
            throw new DomainException(WalletErrors.LastActiveCannotBeArchived);
        }

        var chosen = await NewPrimaryRule.ResolveAsync(wallets, wallet, newPrimaryWalletId, cancellationToken);

        await using var transaction = await wallets.BeginTransactionAsync(cancellationToken);
        wallet.UnmarkPrimary();
        wallet.Archive(clock.UtcNow);
        await wallets.SaveAsync(cancellationToken);

        chosen.MarkPrimary();
        await wallets.SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
