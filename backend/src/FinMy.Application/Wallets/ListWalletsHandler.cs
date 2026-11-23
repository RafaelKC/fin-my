using FinMy.Application.Abstractions;
using FinMy.Domain.Common;

namespace FinMy.Application.Wallets;

/// <summary>Lista carteiras com saldo atual (FR-003) e o filtro <c>archived</c> (<c>false</c> | <c>true</c> | <c>all</c>).</summary>
public sealed class ListWalletsHandler(IWalletStore wallets)
{
    public Task<IReadOnlyList<WalletView>> HandleAsync(string? archived, CancellationToken cancellationToken)
    {
        var filter = archived switch
        {
            null or "" or "false" => WalletArchivedFilter.ActiveOnly,
            "true" => WalletArchivedFilter.ArchivedOnly,
            "all" => WalletArchivedFilter.All,
            _ => throw new DomainException(WalletErrors.InvalidArchivedFilter),
        };

        return wallets.ListAsync(filter, cancellationToken);
    }
}
