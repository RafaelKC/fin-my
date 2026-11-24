using FinMy.Application.Abstractions;
using FinMy.Application.Months;
using FinMy.Domain.Cards;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Infrastructure.Months;

/// <summary>Consultas do Resumo de agora (006 research R2): faturas da janela e pendências, com as mesmas regras da visão do mês.</summary>
public sealed class NowReader(FinMyDbContext db) : INowReader
{
    public async Task<IReadOnlyList<MonthInvoiceItem>> ReadInvoicesAsync(
        DateOnly firstDue, DateOnly lastDue, DateOnly today, CancellationToken cancellationToken)
    {
        var window = db.Set<Invoice>().AsNoTracking().Where(i => i.DueDate >= firstDue && i.DueDate <= lastDue);

        var totals = await db.Set<CardPurchase>().AsNoTracking()
            .Join(window, p => p.InvoiceId, i => i.Id, (p, i) => p)
            .GroupBy(p => p.InvoiceId)
            .Select(g => new { InvoiceId = g.Key, Cents = g.Sum(p => p.Nature == PurchaseNature.Credit ? -p.AmountCents : p.AmountCents) })
            .ToDictionaryAsync(r => r.InvoiceId, r => r.Cents, cancellationToken);
        var paid = await db.Set<InvoicePayment>().AsNoTracking()
            .Join(window, p => p.InvoiceId, i => i.Id, (p, i) => p)
            .GroupBy(p => p.InvoiceId)
            .Select(g => new { InvoiceId = g.Key, Cents = g.Sum(p => p.AmountCents) })
            .ToDictionaryAsync(r => r.InvoiceId, r => r.Cents, cancellationToken);

        var ids = totals.Keys.Concat(paid.Keys).Distinct().ToList();
        var rows = await window
            .Where(i => ids.Contains(i.Id))
            .Join(db.Set<CreditCard>().AsNoTracking(), i => i.CreditCardId, c => c.Id, (i, c) => new { Invoice = i, c.Name })
            .ToListAsync(cancellationToken);

        var items = new List<MonthInvoiceItem>();
        foreach (var row in rows.OrderBy(r => r.Invoice.DueDate).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.Invoice.Id))
        {
            var invoice = row.Invoice;
            var total = totals.GetValueOrDefault(invoice.Id);
            var paidCents = paid.GetValueOrDefault(invoice.Id);
            var remaining = total - paidCents;
            if (remaining <= 0)
            {
                continue;
            }

            items.Add(new MonthInvoiceItem(
                invoice.CreditCardId,
                row.Name,
                invoice.DueYearMonth,
                invoice.ClosingDate,
                invoice.DueDate,
                invoice.Id,
                InvoiceStatusRule.Evaluate(total, paidCents, today, invoice.ClosingDate, invoice.DueDate),
                total,
                paidCents,
                remaining,
                PlannedCents: 0,
                ActualCents: 0));
        }

        return items;
    }

    public async Task<PendingSummary> ReadPendingAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var currentMonth = YearMonth.From(today).FirstDay;
        var pending = await db.Set<Entry>().AsNoTracking()
            .Where(e => e.Status == EntryStatus.Planned && e.ReferenceMonth < currentMonth)
            .GroupBy(e => e.Kind)
            .Select(g => new { Kind = g.Key, Count = g.Count(), Cents = g.Sum(e => e.AmountCents) })
            .ToListAsync(cancellationToken);
        return new PendingSummary(
            pending.Sum(p => p.Count),
            pending.Where(p => p.Kind == EntryKind.Income).Sum(p => p.Cents),
            pending.Where(p => p.Kind == EntryKind.Expense).Sum(p => p.Cents));
    }
}
