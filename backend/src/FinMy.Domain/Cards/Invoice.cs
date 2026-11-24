using FinMy.Domain.Common;
using FinMy.Domain.Monetary;

namespace FinMy.Domain.Cards;

/// <summary>
/// Fatura de um cartão (<c>invoices</c>, 003 FR-002..007): uma linha por cartão e mês de vencimento, criada sob
/// demanda (research R2). Não grava total, pago nem situação — são calculados (FR-005/FR-006).
/// As datas são gravadas na criação (FR-004) e só recalculadas pela mudança de dias do cartão (research R3).
/// </summary>
public sealed class Invoice : TenantEntity
{
    // Construtor para o EF Core.
    private Invoice()
    {
    }

    private Invoice(Guid creditCardId, YearMonth dueMonth, DateOnly closingDate, DateOnly dueDate)
    {
        CreditCardId = creditCardId;
        DueMonth = Rules.Date(dueMonth.FirstDay, "dueMonth");
        ChangeDates(closingDate, dueDate);
    }

    public Guid CreditCardId { get; private set; }

    /// <summary>Mês de vencimento: sempre o dia 1 (RT-04).</summary>
    public DateOnly DueMonth { get; private set; }

    public DateOnly ClosingDate { get; private set; }

    public DateOnly DueDate { get; private set; }

    public YearMonth DueYearMonth => YearMonth.From(DueMonth);

    /// <summary>
    /// Estimativa de gasto da fatura (007 FR-009, research R1): só entra no Previsto até o fechamento
    /// (<see cref="InvoiceForecast"/>); nunca em limite, acertos, Total/Restante nem saldos (FR-012).
    /// </summary>
    public long? EstimateCents { get; private set; }

    public static Invoice Create(Guid creditCardId, YearMonth dueMonth, DateOnly closingDate, DateOnly dueDate) =>
        new(creditCardId, dueMonth, closingDate, dueDate);

    public void ChangeDates(DateOnly closingDate, DateOnly dueDate)
    {
        ClosingDate = Rules.Date(closingDate, "closingDate");
        DueDate = Rules.Date(dueDate, "dueDate");
    }

    /// <summary>Define a estimativa (valor entre 1 e <see cref="Money.MaxCents"/>) ou a remove (<c>null</c>).</summary>
    public void SetEstimate(Money? estimate)
    {
        EstimateCents = estimate is { } value ? Rules.EntryAmount(value, "amount") : null;
    }
}
