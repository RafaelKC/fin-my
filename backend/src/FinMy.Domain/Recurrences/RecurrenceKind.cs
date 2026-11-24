namespace FinMy.Domain.Recurrences;

/// <summary>O que a recorrência gera todo mês: entrada, saída ou compra no cartão (005 FR-001).</summary>
public enum RecurrenceKind
{
    Income,
    Expense,
    CardPurchase,
}
