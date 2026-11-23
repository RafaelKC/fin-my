using FinMy.Application.Abstractions;
using FinMy.Application.Cards;
using FinMy.Domain.Cards;
using FinMy.Domain.Common;
using FinMy.Domain.Monetary;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Infrastructure.Persistence;

/// <summary>
/// Pagamentos de fatura e leitura da fatura completa. Total, pago, conferido e situação são calculados a cada leitura
/// (Princípio V); <c>version</c> do pagamento é o <c>xmin</c> da linha (research R11).
/// </summary>
public sealed class InvoiceStore(FinMyDbContext db) : IInvoiceStore
{
    private const string VersionColumn = "xmin";

    public async Task<InvoiceDetailView> GetDetailAsync(CreditCard card, YearMonth dueMonth, DateOnly today, CancellationToken cancellationToken)
    {
        var month = dueMonth.FirstDay;
        var row = await db.Set<Invoice>().AsNoTracking()
            .SingleOrDefaultAsync(i => i.CreditCardId == card.Id && i.DueMonth == month, cancellationToken);

        if (row is null)
        {
            // Fatura sem linha: total 0, datas calculadas, id nulo (research R2).
            var summary = new InvoiceSummaryView(
                card.Id, dueMonth, InvoiceCycle.ClosingDate(dueMonth, card.ClosingDay, card.DueDay), InvoiceCycle.DueDate(dueMonth, card.DueDay),
                null, InvoiceStatus.NoAmountDue, 0, 0, 0);
            return new InvoiceDetailView(summary, 0, false, [], [], dueMonth.Previous(), dueMonth.Next());
        }

        var purchases = await db.Set<CardPurchase>().AsNoTracking()
            .Where(p => p.InvoiceId == row.Id)
            .OrderBy(p => p.PurchaseDate).ThenBy(p => p.CreatedAt).ThenBy(p => p.Id) // RT-12
            .Select(p => new { Purchase = p, Version = EF.Property<uint>(p, VersionColumn) })
            .ToListAsync(cancellationToken);
        var payments = await db.Set<InvoicePayment>().AsNoTracking()
            .Where(p => p.InvoiceId == row.Id)
            .OrderBy(p => p.PaymentDate).ThenBy(p => p.CreatedAt).ThenBy(p => p.Id)
            .Select(p => new { Payment = p, Version = EF.Property<uint>(p, VersionColumn) })
            .ToListAsync(cancellationToken);

        static long Signed(CardPurchase p) => p.Nature == PurchaseNature.Credit ? -p.AmountCents : p.AmountCents;

        var total = purchases.Sum(p => Signed(p.Purchase));
        var paid = payments.Sum(p => p.Payment.AmountCents);
        var reconciled = purchases.Where(p => p.Purchase.Reconciled).Sum(p => Signed(p.Purchase));
        var status = InvoiceStatusRule.Evaluate(total, paid, today, row.ClosingDate, row.DueDate);
        var remaining = total - paid;

        return new InvoiceDetailView(
            new InvoiceSummaryView(card.Id, dueMonth, row.ClosingDate, row.DueDate, row.Id, status, total, paid, remaining),
            reconciled,
            today >= row.ClosingDate && remaining != 0, // FR-020
            purchases.ConvertAll(p => CardPurchaseStore.ToView(p.Purchase, dueMonth, p.Version)),
            payments.ConvertAll(p => ToView(p.Payment, dueMonth, card.Id, p.Version)),
            dueMonth.Previous(),
            dueMonth.Next());
    }

    public async Task LockAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        var tenantId = db.CurrentContext.TenantIdOrEmpty;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT id FROM invoices WHERE id = {invoiceId} AND tenant_id = {tenantId} FOR UPDATE",
            cancellationToken);
    }

    public Task<InvoicePayment?> FindPaymentAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<InvoicePayment>().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<InvoicePayment>> ListPaymentsAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        await db.Set<InvoicePayment>().Where(p => p.InvoiceId == invoiceId).OrderBy(p => p.PaymentDate).ThenBy(p => p.Id).ToListAsync(cancellationToken);

    public async Task<InvoicePaymentView?> GetPaymentViewAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await db.Set<InvoicePayment>().AsNoTracking()
            .Where(p => p.Id == id)
            .Join(
                db.Set<Invoice>().AsNoTracking(),
                p => p.InvoiceId,
                i => i.Id,
                (p, i) => new { Payment = p, i.CreditCardId, i.DueMonth, Version = EF.Property<uint>(p, VersionColumn) })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null ? null : ToView(row.Payment, YearMonth.From(row.DueMonth), row.CreditCardId, row.Version);
    }

    public long VersionOf(InvoicePayment payment) => db.Entry(payment).Property<uint>(VersionColumn).CurrentValue;

    public void ExpectVersion(InvoicePayment payment, long version) =>
        db.Entry(payment).Property<uint>(VersionColumn).OriginalValue = (uint)version;

    public void Add(InvoicePayment payment) => db.Set<InvoicePayment>().Add(payment);

    public void Remove(InvoicePayment payment) => db.Set<InvoicePayment>().Remove(payment);

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException(InvoiceErrors.ConcurrencyConflict);
        }
    }

    private static InvoicePaymentView ToView(InvoicePayment p, YearMonth dueMonth, Guid creditCardId, uint version) =>
        new(p.Id, creditCardId, dueMonth, p.WalletId, p.AmountCents, p.PaymentDate, version);
}
