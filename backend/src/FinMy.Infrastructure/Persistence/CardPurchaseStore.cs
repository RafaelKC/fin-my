using FinMy.Application.Abstractions;
using FinMy.Application.Cards;
using FinMy.Domain.Cards;
using FinMy.Domain.Common;
using FinMy.Domain.Monetary;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FinMy.Infrastructure.Persistence;

/// <summary>Compras e parcelamentos do Espaço do contexto; <c>version</c> é o <c>xmin</c> da linha (research R11).</summary>
public sealed class CardPurchaseStore(FinMyDbContext db) : ICardPurchaseStore
{
    private const string VersionColumn = "xmin";

    public Task<CardPurchase?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<CardPurchase>().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<InstallmentPlan?> FindPlanAsync(Guid planId, CancellationToken cancellationToken) =>
        db.Set<InstallmentPlan>().SingleOrDefaultAsync(p => p.Id == planId, cancellationToken);

    public async Task<IReadOnlyList<CardPurchase>> ListPlanAsync(Guid planId, CancellationToken cancellationToken) =>
        await db.Set<CardPurchase>().Where(p => p.InstallmentPlanId == planId).OrderBy(p => p.InstallmentNumber).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CardPurchase>> ListCarryOverPairAsync(Guid pairId, CancellationToken cancellationToken) =>
        await db.Set<CardPurchase>().Where(p => p.CarryOverPairId == pairId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, Invoice>> GetInvoicesAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken cancellationToken) =>
        await db.Set<Invoice>().AsNoTracking().Where(i => invoiceIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, InvoiceTotals>> GetInvoiceTotalsAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken cancellationToken)
    {
        // Total = débitos − créditos (FR-005); Pago = soma dos pagamentos. Exclusões lógicas saem pelo filtro padrão.
        var totals = await db.Set<CardPurchase>().AsNoTracking()
            .Where(p => invoiceIds.Contains(p.InvoiceId))
            .GroupBy(p => p.InvoiceId)
            .Select(g => new { InvoiceId = g.Key, Total = g.Sum(p => p.Nature == PurchaseNature.Credit ? -p.AmountCents : p.AmountCents) })
            .ToDictionaryAsync(t => t.InvoiceId, t => t.Total, cancellationToken);
        var paid = await db.Set<InvoicePayment>().AsNoTracking()
            .Where(p => invoiceIds.Contains(p.InvoiceId))
            .GroupBy(p => p.InvoiceId)
            .Select(g => new { InvoiceId = g.Key, Total = g.Sum(p => p.AmountCents) })
            .ToDictionaryAsync(t => t.InvoiceId, t => t.Total, cancellationToken);

        return invoiceIds.ToDictionary(id => id, id => new InvoiceTotals(totals.GetValueOrDefault(id), paid.GetValueOrDefault(id)));
    }

    public async Task<IReadOnlyList<CardPurchaseView>> GetViewsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var rows = await db.Set<CardPurchase>().AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Join(
                db.Set<Invoice>().AsNoTracking(),
                p => p.InvoiceId,
                i => i.Id,
                (p, i) => new { Purchase = p, i.DueMonth, Version = EF.Property<uint>(p, VersionColumn) })
            .OrderBy(r => r.Purchase.InstallmentNumber).ThenBy(r => r.Purchase.CreatedAt).ThenBy(r => r.Purchase.Id)
            .ToListAsync(cancellationToken);

        return rows.ConvertAll(r => ToView(r.Purchase, YearMonth.From(r.DueMonth), r.Version));
    }

    public long VersionOf(CardPurchase purchase) => db.Entry(purchase).Property<uint>(VersionColumn).CurrentValue;

    public void ExpectVersion(CardPurchase purchase, long version) =>
        db.Entry(purchase).Property<uint>(VersionColumn).OriginalValue = (uint)version;

    public void Add(CardPurchase purchase) => db.Set<CardPurchase>().Add(purchase);

    public void Add(InstallmentPlan plan) => db.Set<InstallmentPlan>().Add(plan);

    public void Remove(CardPurchase purchase) => db.Set<CardPurchase>().Remove(purchase);

    public void Remove(InstallmentPlan plan) => db.Set<InstallmentPlan>().Remove(plan);

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException(CardPurchaseErrors.ConcurrencyConflict);
        }
    }

    public async Task<ICardTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        new CardTransaction(await db.Database.BeginTransactionAsync(cancellationToken));

    private static CardPurchaseView ToView(CardPurchase p, YearMonth dueMonth, uint version) => new(
        p.Id,
        p.CreditCardId,
        dueMonth,
        p.Description,
        p.DisplayDescription,
        p.AmountCents,
        p.Nature,
        p.PurchaseDate,
        p.Origin,
        p.Reconciled,
        p.ManuallyMoved,
        p.Notes,
        p.InstallmentPlanId,
        p.InstallmentNumber,
        p.InstallmentTotal,
        p.CreatedAt,
        version);

    private sealed class CardTransaction(IDbContextTransaction transaction) : ICardTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
