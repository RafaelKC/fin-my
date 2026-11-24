using FinMy.Application.Abstractions;
using FinMy.Application.Planning;
using FinMy.Domain.Cards;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;
using FinMy.Domain.Months;
using FinMy.Domain.Wallets;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Infrastructure.Planning;

/// <summary>Consultas da grade do ano (007 research R2): lançamentos do ano, cartões e faturas com vencimento no ano.</summary>
public sealed class PlanningReader(FinMyDbContext db) : IPlanningReader
{
    public async Task<PlanningData> ReadAsync(int year, DateOnly today, CancellationToken cancellationToken)
    {
        var first = new DateOnly(year, 1, 1);
        var last = new DateOnly(year, 12, 1);

        var wallets = await db.Set<Wallet>().AsNoTracking()
            .Select(w => new { w.Id, w.ArchivedAt })
            .ToListAsync(cancellationToken);
        var allWallets = wallets.Select(w => w.Id).ToHashSet();
        var active = wallets.Where(w => w.ArchivedAt is null).Select(w => w.Id).ToHashSet();

        var rows = await db.Set<Entry>().AsNoTracking()
            .Where(e => e.ReferenceMonth >= first && e.ReferenceMonth <= last)
            .ToListAsync(cancellationToken);
        var links = await TagLinks.ForEntriesAsync(db, rows.Select(e => e.Id).ToList(), cancellationToken);
        var entries = rows
            .Select(e => new PlanningEntry(
                e.Id, e.Kind, e.Description, e.DescriptionNormalized, e.AmountCents, e.EntryDate, e.ReferenceYearMonth, e.Status,
                e.WalletId, e.CreatedAt, TagLinks.All(e.SettlementTagId, links, e.Id)))
            .ToList();

        var cards = await db.Set<CreditCard>().AsNoTracking()
            .Select(c => new { c.Id, c.Name, c.PaymentWalletId, c.ArchivedAt })
            .ToListAsync(cancellationToken);

        return new PlanningData(
            entries,
            active,
            cards.Select(c => new PlanningCard(c.Id, c.Name, c.ArchivedAt is not null)).ToList(),
            await ReadInvoicesAsync(first, last, today, allWallets, cards.ToDictionary(c => c.Id, c => c.PaymentWalletId), cancellationToken));
    }

    /// <summary>Faturas do ano com total ≠ 0, pagamentos ou estimativa; o Previsto é o do painel (todas as Carteiras, com estimativa).</summary>
    private async Task<IReadOnlyList<PlanningInvoice>> ReadInvoicesAsync(
        DateOnly first, DateOnly last, DateOnly today, IReadOnlySet<Guid> allWallets, Dictionary<Guid, Guid> paymentWallets, CancellationToken cancellationToken)
    {
        var inYear = db.Set<Invoice>().AsNoTracking().Where(i => i.DueMonth >= first && i.DueMonth <= last);

        var totals = await db.Set<CardPurchase>().AsNoTracking()
            .Join(inYear, p => p.InvoiceId, i => i.Id, (p, i) => p)
            .GroupBy(p => p.InvoiceId)
            .Select(g => new { InvoiceId = g.Key, Cents = g.Sum(p => p.Nature == PurchaseNature.Credit ? -p.AmountCents : p.AmountCents) })
            .ToDictionaryAsync(r => r.InvoiceId, r => r.Cents, cancellationToken);

        var paid = (await db.Set<InvoicePayment>().AsNoTracking()
                .Join(inYear, p => p.InvoiceId, i => i.Id, (p, i) => p)
                .GroupBy(p => new { p.InvoiceId, p.WalletId })
                .Select(g => new { g.Key.InvoiceId, g.Key.WalletId, Cents = g.Sum(p => p.AmountCents) })
                .ToListAsync(cancellationToken))
            .GroupBy(r => r.InvoiceId)
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<Guid, long>)g.ToDictionary(r => r.WalletId, r => r.Cents));

        var estimated = await inYear.Where(i => i.EstimateCents != null).Select(i => i.Id).ToListAsync(cancellationToken);
        var ids = totals.Where(t => t.Value != 0).Select(t => t.Key).Concat(paid.Keys).Concat(estimated).Distinct().ToList();
        var invoices = await inYear.Where(i => ids.Contains(i.Id)).ToListAsync(cancellationToken);

        var none = new Dictionary<Guid, long>();
        return invoices
            .Select(i =>
            {
                var figure = new InvoiceFigure(
                    i.DueYearMonth,
                    totals.GetValueOrDefault(i.Id),
                    paymentWallets[i.CreditCardId],
                    paid.GetValueOrDefault(i.Id) ?? none,
                    InvoiceForecast.EstimateActive(i.EstimateCents, today, i.ClosingDate) ? i.EstimateCents : null);
                return new PlanningInvoice(i.CreditCardId, i.DueYearMonth, figure.Planned(allWallets), figure.TotalCents, i.EstimateCents);
            })
            .ToList();
    }
}
