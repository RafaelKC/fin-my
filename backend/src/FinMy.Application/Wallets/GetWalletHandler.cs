using FinMy.Application.Abstractions;
using FinMy.Domain.Common;

namespace FinMy.Application.Wallets;

/// <summary>Obtém uma carteira (ativa ou arquivada) com saldo atual.</summary>
public sealed class GetWalletHandler(IWalletStore wallets)
{
    public async Task<WalletView> HandleAsync(Guid id, CancellationToken cancellationToken) =>
        await wallets.GetAsync(id, cancellationToken) ?? throw new DomainException(WalletErrors.NotFound);
}
