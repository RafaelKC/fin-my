namespace FinMy.Domain.Cards;

/// <summary>Como a compra nasceu nesta feature: lançada à mão, parcela de um plano ou par de "saldo levado" (FR-020).</summary>
public enum PurchaseOrigin
{
    Manual,
    Installment,
    CarryOver,
}
