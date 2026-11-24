using FinMy.Domain.Cards;
using FinMy.Domain.Monetary;

namespace FinMy.Domain.Recurrences;

/// <summary>Uma ocorrência a gerar: mês, data do lançamento e, conforme o tipo, Mês de referência ou fatura.</summary>
/// <param name="Month">Mês da ocorrência (<c>recurrence_month</c>).</param>
/// <param name="Date">Data do lançamento (dia da regra, limitado ao último dia do mês — RT-03).</param>
/// <param name="ReferenceMonth">Mês de referência (entrada/saída); nulo para compra no cartão.</param>
/// <param name="InvoiceDueMonth">Mês de vencimento da fatura (compra no cartão); nulo para entrada/saída.</param>
public readonly record struct RecurrenceOccurrence(YearMonth Month, DateOnly Date, YearMonth? ReferenceMonth, YearMonth? InvoiceDueMonth);

/// <summary>Regra pura da recorrência (005, research R1): quais ocorrências faltam gerar.</summary>
public static class RecurrenceScheduler
{
    /// <summary>
    /// Ocorrências de <paramref name="rule"/> de <c>max(início, fromMonthExclusive + 1)</c> até
    /// <c>min(dezembro do ano seguinte ao de hoje, mês final)</c>. <paramref name="card"/> (fechamento e vencimento)
    /// é obrigatório para compra no cartão e define a fatura de cada ocorrência.
    /// </summary>
    public static IReadOnlyList<RecurrenceOccurrence> Occurrences(
        Recurrence rule,
        YearMonth? fromMonthExclusive,
        DateOnly today,
        (int ClosingDay, int DueDay)? card = null)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var first = rule.StartYearMonth;
        if (fromMonthExclusive is { } done && done.Next() > first)
        {
            first = done.Next();
        }

        var last = Horizon(today);
        if (rule.EndYearMonth is { } end && end < last)
        {
            last = end;
        }

        var isCard = rule.Kind == RecurrenceKind.CardPurchase;
        if (isCard && card is null)
        {
            throw new ArgumentException("Compra no cartão exige os dias de fechamento e vencimento.", nameof(card));
        }

        var result = new List<RecurrenceOccurrence>();
        for (var month = first; month <= last; month = month.Next())
        {
            var date = new DateOnly(month.Year, month.Month, Math.Min(rule.DayOfMonth, month.LastDay.Day));
            if (isCard)
            {
                var (closing, due) = card!.Value;
                result.Add(new(month, date, null, InvoiceCycle.InvoiceFor(date, closing, due)));
            }
            else
            {
                var reference = rule.ReferenceMonthRule == ReferenceMonthRule.NextMonth ? month.Next() : month;
                result.Add(new(month, date, reference, null));
            }
        }

        return result;
    }

    /// <summary>Último mês gerado: dezembro do ano seguinte ao de hoje (limitado a dezembro de 2100, RT-03).</summary>
    public static YearMonth Horizon(DateOnly today) => YearMonth.Of(Math.Min(today.Year + 1, 2100), 12);
}
