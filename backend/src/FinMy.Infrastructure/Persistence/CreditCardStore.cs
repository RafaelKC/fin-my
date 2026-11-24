using FinMy.Application.Abstractions;
using FinMy.Application.Cards;
using FinMy.Domain.Cards;
using FinMy.Domain.Common;
using FinMy.Domain.Monetary;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace FinMy.Infrastructure.Persistence;

/// <summary>
/// Cartões do Espaço do contexto. Totais, pago e situação das faturas vêm de consultas agregadas (nunca gravados,
/// Princípio V); o limite disponível segue FR-026 (research R10).
/// </summary>
public sealed class CreditCardStore(FinMyDbContext db) : ICreditCardStore
{
    private const string VersionColumn = "xmin";

    public Task<CreditCard?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<CreditCard>().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<CreditCardView?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        (await ViewsAsync(db.Set<CreditCard>().Where(c => c.Id == id), cancellationToken)).SingleOrDefault();

    public async Task<CreditCardOverviewView?> GetOverviewAsync(Guid id, DateOnly today, CancellationToken cancellationToken) =>
        (await OverviewsAsync(db.Set<CreditCard>().Where(c => c.Id == id), today, cancellationToken)).SingleOrDefault();

    public Task<IReadOnlyList<CreditCardOverviewView>> ListOverviewsAsync(CreditCardArchivedFilter filter, DateOnly today, CancellationToken cancellationToken)
    {
        var query = db.Set<CreditCard>().AsQueryable();
        query = filter switch
        {
            CreditCardArchivedFilter.ActiveOnly => query.Where(c => c.ArchivedAt == null),
            CreditCardArchivedFilter.ArchivedOnly => query.Where(c => c.ArchivedAt != null),
            _ => query,
        };

        return OverviewsAsync(query, today, cancellationToken);
    }

    public async Task<bool> HasPurchasesOrPaymentsAsync(Guid id, CancellationToken cancellationToken) =>
        (await WithPurchasesOrPaymentsAsync([id], cancellationToken)).Contains(id);

    public Task<Invoice?> FindInvoiceAsync(Guid creditCardId, YearMonth dueMonth, CancellationToken cancellationToken)
    {
        var month = dueMonth.FirstDay;
        return db.Set<Invoice>().AsNoTracking()
            .SingleOrDefaultAsync(i => i.CreditCardId == creditCardId && i.DueMonth == month, cancellationToken);
    }

    public async Task<IReadOnlyList<Invoice>> ListInvoicesAsync(Guid creditCardId, CancellationToken cancellationToken) =>
        await db.Set<Invoice>().Where(i => i.CreditCardId == creditCardId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CardPurchase>> ListPurchasesAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken cancellationToken) =>
        await db.Set<CardPurchase>().Where(p => invoiceIds.Contains(p.InvoiceId)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, InstallmentPlan>> GetPlansAsync(IReadOnlyCollection<Guid> planIds, CancellationToken cancellationToken) =>
        await db.Set<InstallmentPlan>().Where(p => planIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);

    public long VersionOf(CreditCard card) => db.Entry(card).Property<uint>(VersionColumn).CurrentValue;

    public void ExpectVersion(CreditCard card, long version) =>
        db.Entry(card).Property<uint>(VersionColumn).OriginalValue = (uint)version;

    public void Add(CreditCard card) => db.Set<CreditCard>().Add(card);

    public void Remove(CreditCard card) => db.Set<CreditCard>().Remove(card);

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new DomainException(CreditCardErrors.NameTaken);
        }
    }

    public async Task<ICardTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        new CardTransaction(await db.Database.BeginTransactionAsync(cancellationToken));

    private async Task<HashSet<Guid>> WithPurchasesOrPaymentsAsync(IReadOnlyCollection<Guid> cardIds, CancellationToken cancellationToken)
    {
        // Linhas excluídas e ainda não expurgadas contam: a exclusão do cartão só vale se ele nunca teve nada (RT-11).
        var withPurchases = await db.Set<CardPurchase>().AsNoTracking().IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])
            .Where(p => cardIds.Contains(p.CreditCardId)).Select(p => p.CreditCardId).Distinct().ToListAsync(cancellationToken);
        var result = withPurchases.ToHashSet();

        var withPayments = await db.Set<InvoicePayment>().AsNoTracking().IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])
            .Join(
                db.Set<Invoice>().AsNoTracking().IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter]),
                p => p.InvoiceId,
                i => i.Id,
                (_, i) => i.CreditCardId)
            .Where(cardId => cardIds.Contains(cardId)).Distinct().ToListAsync(cancellationToken);
        result.UnionWith(withPayments);
        return result;
    }

    private async Task<IReadOnlyList<CreditCardView>> ViewsAsync(IQueryable<CreditCard> cards, CancellationToken cancellationToken)
    {
        var rows = await RowsAsync(cards, cancellationToken);
        return await ToViewsAsync(rows, cancellationToken);
    }

    private static async Task<List<(CreditCard Card, uint Version)>> RowsAsync(IQueryable<CreditCard> cards, CancellationToken cancellationToken)
    {
        var rows = await cards.AsNoTracking()
            .OrderBy(c => c.NameNormalized)
            .Select(c => new { Card = c, Version = EF.Property<uint>(c, VersionColumn) })
            .ToListAsync(cancellationToken);
        return rows.Select(r => (r.Card, r.Version)).ToList();
    }

    private async Task<IReadOnlyList<CreditCardView>> ToViewsAsync(List<(CreditCard Card, uint Version)> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var used = await WithPurchasesOrPaymentsAsync(rows.Select(r => r.Card.Id).ToList(), cancellationToken);
        return rows.Select(r => ToView(r.Card, r.Version, used.Contains(r.Card.Id))).ToList();
    }

    private static CreditCardView ToView(CreditCard c, uint version, bool hasPurchases) =>
        new(c.Id, c.Name, c.ClosingDay, c.DueDay, c.PaymentWalletId, c.Color, c.LimitCents, c.IsArchived, hasPurchases, version);

    private async Task<IReadOnlyList<CreditCardOverviewView>> OverviewsAsync(IQueryable<CreditCard> cards, DateOnly today, CancellationToken cancellationToken)
    {
        var rows = await RowsAsync(cards, cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        var views = (await ToViewsAsync(rows, cancellationToken)).ToDictionary(v => v.Id);
        var cardIds = rows.Select(r => r.Card.Id).ToList();

        var invoices = await db.Set<Invoice>().AsNoTracking()
            .Where(i => cardIds.Contains(i.CreditCardId)).ToListAsync(cancellationToken);
        var invoiceIds = invoices.Select(i => i.Id).ToList();

        // Total = débitos − créditos (FR-005); Pago = soma dos pagamentos. Exclusões lógicas saem pelo filtro padrão.
        var totals = (await db.Set<CardPurchase>().AsNoTracking()
            .Where(p => cardIds.Contains(p.CreditCardId))
            .GroupBy(p => p.InvoiceId)
            .Select(g => new { InvoiceId = g.Key, Total = g.Sum(p => p.Nature == PurchaseNature.Credit ? -p.AmountCents : p.AmountCents) })
            .ToListAsync(cancellationToken)).ToDictionary(t => t.InvoiceId, t => t.Total);
        var paid = (await db.Set<InvoicePayment>().AsNoTracking()
            .Where(p => invoiceIds.Contains(p.InvoiceId))
            .GroupBy(p => p.InvoiceId)
            .Select(g => new { InvoiceId = g.Key, Total = g.Sum(p => p.AmountCents) })
            .ToListAsync(cancellationToken)).ToDictionary(t => t.InvoiceId, t => t.Total);

        return rows.Select(r =>
        {
            var card = r.Card;
            var own = invoices.Where(i => i.CreditCardId == card.Id).ToList();

            InvoiceSummaryView Summary(Invoice i) => Summarize(i, totals, paid, today);

            InvoiceSummaryView ForMonth(YearMonth month)
            {
                var row = own.SingleOrDefault(i => i.DueMonth == month.FirstDay);
                return row is not null
                    ? Summary(row)
                    : new InvoiceSummaryView(
                        card.Id, month, InvoiceCycle.ClosingDate(month, card.ClosingDay, card.DueDay),
                        InvoiceCycle.DueDate(month, card.DueDay), null, InvoiceStatus.NoAmountDue, 0, 0, 0);
            }

            var current = InvoiceCycle.CurrentInvoice(today, card.ClosingDay, card.DueDay);
            var summaries = own.Select(Summary).ToList();

            // FR-026: limite − soma de max(Restante, 0) de todas as faturas não pagas, inclusive as futuras.
            long? available = card.LimitCents is { } limit
                ? limit - summaries.Where(s => s.Status != InvoiceStatus.Paid).Sum(s => Math.Max(s.RemainingCents, 0))
                : null;

            var openPast = summaries
                .Where(s => s.ClosingDate <= today && s.Status is InvoiceStatus.Closed or InvoiceStatus.Overdue)
                .OrderBy(s => s.DueMonth)
                .ToList();

            return new CreditCardOverviewView(views[card.Id], ForMonth(current), ForMonth(current.Next()), openPast, available);
        }).ToList();
    }

    private static InvoiceSummaryView Summarize(Invoice invoice, Dictionary<Guid, long> totals, Dictionary<Guid, long> paid, DateOnly today)
    {
        var total = totals.GetValueOrDefault(invoice.Id);
        var paidCents = paid.GetValueOrDefault(invoice.Id);
        var status = InvoiceStatusRule.Evaluate(total, paidCents, today, invoice.ClosingDate, invoice.DueDate);
        return new InvoiceSummaryView(
            invoice.CreditCardId, invoice.DueYearMonth, invoice.ClosingDate, invoice.DueDate, invoice.Id, status, total, paidCents, total - paidCents,
            invoice.EstimateCents, InvoiceForecast.EstimateActive(invoice.EstimateCents, today, invoice.ClosingDate));
    }

    private sealed class CardTransaction(IDbContextTransaction transaction) : ICardTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
