namespace FinMy.Domain.Recurrences;

/// <summary>Mês de referência da ocorrência: o próprio mês da data ou o seguinte (só entrada/saída).</summary>
public enum ReferenceMonthRule
{
    SameMonth,
    NextMonth,
}
