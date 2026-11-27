using FinMy.Application.Abstractions;
using FinMy.Application.Entries;
using FinMy.Application.Months;
using FinMy.Application.Panel;
using FinMy.Application.Settlements;
using FinMy.Domain.Cards;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Widget;

/// <summary>Próximo item a pagar/receber: lançamento previsto (<c>income</c>/<c>expense</c>) ou fatura (<c>invoice</c>).</summary>
/// <remarks><c>EntryId</c> só para lançamentos (o relógio marca como pago/recebido); faturas não têm.</remarks>
public sealed record WidgetUpcomingItem(string Kind, string Description, DateOnly Date, long AmountCents, bool Overdue, Guid? EntryId = null);

/// <summary>Saldo acumulado de uma pessoa (Tag de acerto) até o mês de hoje; negativo é crédito dela.</summary>
public sealed record WidgetSettlementItem(Guid TagId, string Name, string Color, long AmountCents);

/// <summary>Números do mês de hoje na leitura Prevista e na Realizada (os mesmos de <c>GET /months/{m}</c>).</summary>
public sealed record WidgetMonthFigures(
    long PlannedIncomeCents,
    long PlannedExpenseCents,
    long PlannedFinalCents,
    long ActualIncomeCents,
    long ActualExpenseCents,
    long ActualFinalCents);

/// <summary>Dados dos widgets "Mês atual", "Próximos vencimentos" e "Acertos" (010).</summary>
public sealed record WidgetOverviewView(
    DateOnly Today,
    string Month,
    WidgetMonthFigures Figures,
    long TotalBalanceCents,
    PendingSummary Pending,
    IReadOnlyList<WidgetUpcomingItem> Upcoming,
    IReadOnlyList<WidgetSettlementItem> Settlements);

/// <summary>
/// Resumo para os widgets, montado com os handlers das telas de origem (mês, agora, pendências e acertos), então os números
/// batem com o app. Próximos vencimentos = previstas atrasadas (meses anteriores), previstas do mês de hoje a partir de hoje e
/// faturas com restante, atrasadas primeiro e depois por data.
/// </summary>
public sealed class GetWidgetOverviewHandler(
    GetMonthHandler month,
    GetNowHandler now,
    ListPendingEntriesHandler pending,
    GetSettlementsHandler settlements,
    IClock clock)
{
    public const int MaxUpcoming = 8;

    public async Task<WidgetOverviewView> HandleAsync(CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var current = YearMonth.From(today).ToString();
        var monthView = await month.HandleAsync(current, null, null, cancellationToken);
        var nowView = await now.HandleAsync(cancellationToken);
        var overdueEntries = await pending.HandleAsync(cancellationToken);
        var people = await settlements.HandleAsync(current, cancellationToken);

        var upcoming = overdueEntries
            .Select(e => new WidgetUpcomingItem(KindCode(e.Kind), e.Description, e.Date, e.AmountCents, Overdue: true, e.Id))
            .Concat(monthView.Entries
                .Where(e => e.Status == EntryStatus.Planned)
                .Select(e => new WidgetUpcomingItem(KindCode(e.Kind), e.Description, e.Date, e.AmountCents, Overdue: e.Date < today, e.Id)))
            .Concat(nowView.Invoices
                .Where(i => i.RemainingCents > 0 && i.Status != InvoiceStatus.Paid)
                .Select(i => new WidgetUpcomingItem(
                    "invoice", $"Fatura {i.CreditCardName}", i.DueDate, i.RemainingCents, Overdue: i.Status == InvoiceStatus.Overdue)))
            .OrderByDescending(u => u.Overdue)
            .ThenBy(u => u.Date)
            .ThenBy(u => u.Description, StringComparer.CurrentCultureIgnoreCase)
            .Take(MaxUpcoming)
            .ToList();

        var summary = monthView.Summary;
        return new WidgetOverviewView(
            today,
            current,
            new WidgetMonthFigures(
                summary.Planned.IncomeCents,
                summary.Planned.ExpenseCents,
                summary.Planned.FinalBalanceCents,
                summary.Actual.IncomeCents,
                summary.Actual.ExpenseCents,
                summary.Actual.FinalBalanceCents),
            nowView.TotalBalanceCents,
            monthView.Pending,
            upcoming,
            people
                .Where(p => !p.Archived && p.Figures.AccumulatedToReceiveCents != 0)
                .OrderByDescending(p => p.Figures.AccumulatedToReceiveCents)
                .Select(p => new WidgetSettlementItem(p.TagId, p.TagName, p.TagColor, p.Figures.AccumulatedToReceiveCents))
                .ToList());
    }

    private static string KindCode(EntryKind kind) => kind == EntryKind.Income ? "income" : "expense";
}
