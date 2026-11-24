namespace FinMy.Domain.Recurrences;

/// <summary>Como a recorrência termina: nunca, em um mês ou depois de N ocorrências (005 FR-002).</summary>
public enum RecurrenceEndType
{
    None,
    UntilMonth,
    AfterCount,
}
