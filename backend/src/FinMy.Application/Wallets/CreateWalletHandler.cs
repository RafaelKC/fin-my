using FinMy.Application.Abstractions;
using FinMy.Domain.Wallets;

namespace FinMy.Application.Wallets;

/// <summary>Cria carteira (US1.1). A primeira ativa do Espaço vira a principal (FR-002).</summary>
public sealed class CreateWalletHandler(IWalletStore wallets)
{
    public async Task<WalletView> HandleAsync(WalletInput input, CancellationToken cancellationToken)
    {
        var parsed = WalletInputValidator.ValidateAndParse(input);

        var isPrimary = !await wallets.AnyActiveAsync(null, cancellationToken);
        var wallet = Wallet.Create(parsed.Name, parsed.Type, parsed.Color, parsed.InitialBalance, parsed.InitialBalanceDate, isPrimary);
        wallets.Add(wallet);
        await wallets.SaveAsync(cancellationToken);

        return await wallets.GetAsync(wallet.Id, cancellationToken) ?? throw new InvalidOperationException("Carteira criada não encontrada.");
    }
}
