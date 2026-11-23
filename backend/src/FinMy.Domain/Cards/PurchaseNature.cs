namespace FinMy.Domain.Cards;

/// <summary>Natureza da compra no cartão: débito soma na fatura, crédito (estorno) abate dela.</summary>
public enum PurchaseNature
{
    Debit,
    Credit,
}
