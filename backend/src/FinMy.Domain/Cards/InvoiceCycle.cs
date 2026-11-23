using FinMy.Domain.Monetary;

namespace FinMy.Domain.Cards;

/// <summary>Ciclo da fatura de cartão (research R1): datas de fechamento/vencimento e fatura de uma compra.</summary>
public static class InvoiceCycle
{
    /// <summary>
    /// Data de fechamento da fatura que vence em <paramref name="dueMonth"/>. Se <paramref name="closingDay"/> é menor
    /// que <paramref name="dueDay"/>, fecha no próprio mês; senão (inclusive igual) fecha no mês anterior. Dia que não
    /// existe no mês vira o último dia do mês (RT-03).
    /// </summary>
    public static DateOnly ClosingDate(YearMonth dueMonth, int closingDay, int dueDay)
    {
        ValidateDay(closingDay, nameof(closingDay));
        ValidateDay(dueDay, nameof(dueDay));
        var month = closingDay < dueDay ? dueMonth : dueMonth.Previous();
        return Clamp(month, closingDay);
    }

    /// <summary>Data de vencimento da fatura de <paramref name="dueMonth"/> (dia inexistente vira o último do mês).</summary>
    public static DateOnly DueDate(YearMonth dueMonth, int dueDay)
    {
        ValidateDay(dueDay, nameof(dueDay));
        return Clamp(dueMonth, dueDay);
    }

    /// <summary>
    /// Fatura (mês de vencimento) que recebe uma compra de <paramref name="date"/>: o menor mês cuja data de
    /// fechamento é posterior à compra. Compra no dia do fechamento vai para a fatura seguinte.
    /// </summary>
    public static YearMonth InvoiceFor(DateOnly date, int closingDay, int dueDay)
    {
        var candidate = YearMonth.From(date);
        for (var i = 0; i < 3; i++, candidate = candidate.Next())
        {
            if (ClosingDate(candidate, closingDay, dueDay) > date)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Nenhuma fatura encontrada em três meses; dias do cartão inválidos.");
    }

    /// <summary>Fatura atual (FR-007): a que recebe as compras de hoje.</summary>
    public static YearMonth CurrentInvoice(DateOnly today, int closingDay, int dueDay) =>
        InvoiceFor(today, closingDay, dueDay);

    private static DateOnly Clamp(YearMonth month, int day) =>
        new(month.Year, month.Month, Math.Min(day, month.LastDay.Day));

    private static void ValidateDay(int day, string name)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(day, 1, name);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(day, 31, name);
    }
}
