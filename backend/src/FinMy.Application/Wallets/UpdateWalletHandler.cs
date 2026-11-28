using FinMy.Application.Abstractions;
using FinMy.Domain.Common;
using FluentValidation;

namespace FinMy.Application.Wallets;

/// <summary>Edita nome, tipo, cor, saldo inicial e data; não muda principal nem arquivamento. Exige <c>version</c> (research R11).</summary>
public sealed class UpdateWalletHandler(IWalletStore wallets)
{
    public async Task<WalletView> HandleAsync(Guid id, WalletInput input, long? version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var wallet = await wallets.FindAsync(id, cancellationToken) ?? throw new DomainException(WalletErrors.NotFound);

        var parsed = WalletInputValidator.ValidateAndParse(input);
        if (version is null)
        {
            throw new ValidationException([new FluentValidation.Results.ValidationFailure("version", "Informe a versão da carteira.")]);
        }

        if (wallets.VersionOf(wallet) != version)
        {
            throw new DomainException(WalletErrors.ConcurrencyConflict);
        }

        wallets.ExpectVersion(wallet, version.Value);
        wallet.Rename(parsed.Name);
        wallet.ChangeType(parsed.Type);
        wallet.ChangeColor(parsed.Color);
        wallet.ChangeBank(input.Bank);
        wallet.ChangeInitialBalance(parsed.InitialBalance, parsed.InitialBalanceDate);
        await wallets.SaveAsync(cancellationToken);

        return await wallets.GetAsync(id, cancellationToken) ?? throw new DomainException(WalletErrors.NotFound);
    }
}
