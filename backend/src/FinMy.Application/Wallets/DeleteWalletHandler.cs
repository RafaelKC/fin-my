using FinMy.Application.Abstractions;
using FinMy.Domain.Common;

namespace FinMy.Application.Wallets;

/// <summary>
/// Exclui a carteira (research R6), só se nenhuma linha de lançamento ou transferência a referencia
/// (409 <c>wallet.has_entries_archive_instead</c>). Excluir a principal com outra ativa exige <c>newPrimaryWalletId</c>.
/// </summary>
public sealed class DeleteWalletHandler(IWalletStore wallets)
{
    public async Task HandleAsync(Guid id, Guid? newPrimaryWalletId, CancellationToken cancellationToken)
    {
        var wallet = await wallets.FindAsync(id, cancellationToken) ?? throw new DomainException(WalletErrors.NotFound);

        if (await wallets.HasEntriesAsync(id, cancellationToken))
        {
            throw new DomainException(WalletErrors.HasEntriesArchiveInstead);
        }

        Domain.Wallets.Wallet? chosen = null;
        if (wallet.IsPrimary && await wallets.AnyActiveAsync(wallet.Id, cancellationToken))
        {
            chosen = await NewPrimaryRule.ResolveAsync(wallets, wallet, newPrimaryWalletId, cancellationToken);
        }

        await using var transaction = await wallets.BeginTransactionAsync(cancellationToken);
        wallet.UnmarkPrimary();
        wallets.Remove(wallet);
        await wallets.SaveAsync(cancellationToken);

        if (chosen is not null)
        {
            chosen.MarkPrimary();
            await wallets.SaveAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
