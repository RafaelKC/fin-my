namespace FinMy.Domain.Wallets;

/// <summary>Tipo da Carteira (apenas informativo, sem efeito nas regras — 002 FR-001).</summary>
public enum WalletType
{
    Checking,
    Digital,
    Savings,
    Cash,
    MealVoucher,
    Other,
}
