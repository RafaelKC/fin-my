using FinMy.Application.Abstractions;
using FinMy.Application.Months;
using FinMy.Domain.Cards;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;
using FinMy.Domain.Months;
using FinMy.Domain.Transfers;
using FinMy.Domain.Wallets;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Infrastructure.Months;

/// <summary>
/// Consultas da visão do mês (research R4): uma agregada por tipo de dado (lançamentos por mês/tipo/situação,
/// transferências por mês e carteiras) mais as listas do mês. Nada fica desnormalizado.
/// </summary>
public sealed class MonthReader(FinMyDbContext db) : IMonthReader
{
    public async Task<MonthData> ReadAsync(
        YearMonth month, IReadOnlyCollection<Guid>? walletIds, DateOnly today, CancellationToken cancellationToken)
    {
        var wallets = await db.Set<Wallet>().AsNoTracking()
            .Where(w => walletIds == null || walletIds.Contains(w.Id))
            .Select(w => new WalletOpening(w.Id, w.InitialBalanceCents, w.InitialBalanceDate))
            .ToListAsync(cancellationToken);
        var selected = wallets.Select(w => w.WalletId).ToList();

        var first = month.FirstDay;
        var last = month.LastDay;
        var currentMonth = YearMonth.From(today).FirstDay;

        var entries = db.Set<Entry>().AsNoTracking().Where(e => selected.Contains(e.WalletId));
        var transfers = db.Set<Transfer>().AsNoTracking()
            .Where(t => selected.Contains(t.FromWalletId) || selected.Contains(t.ToWalletId));

        var entryTotals = (await entries
                .Where(e => e.ReferenceMonth <= first)
                .GroupBy(e => new { e.ReferenceMonth, e.Kind, e.Status })
                .Select(g => new { g.Key.ReferenceMonth, g.Key.Kind, g.Key.Status, Cents = g.Sum(e => e.AmountCents) })
                .ToListAsync(cancellationToken))
            .Select(r => new EntryTotal(YearMonth.From(r.ReferenceMonth), r.Kind, r.Status, r.Cents))
            .ToList();

        var transferTotals = (await transfers
                .Where(t => t.TransferDate <= last)
                .GroupBy(t => new { t.TransferDate.Year, t.TransferDate.Month, t.FromWalletId, t.ToWalletId })
                .Select(g => new { g.Key.Year, g.Key.Month, g.Key.FromWalletId, g.Key.ToWalletId, Cents = g.Sum(t => t.AmountCents) })
                .ToListAsync(cancellationToken))
            .Select(r => new TransferTotal(YearMonth.Of(r.Year, r.Month), r.FromWalletId, r.ToWalletId, r.Cents))
            .ToList();

        // Pagamentos de fatura das carteiras selecionadas, por mês de vencimento da fatura (RT-04, RT-07).
        var invoicePayments = (await db.Set<InvoicePayment>().AsNoTracking()
                .Where(p => selected.Contains(p.WalletId))
                .Join(db.Set<Invoice>().AsNoTracking().Where(i => i.DueMonth <= first), p => p.InvoiceId, i => i.Id, (p, i) => new { i.DueMonth, p.AmountCents })
                .GroupBy(r => r.DueMonth)
                .Select(g => new { DueMonth = g.Key, Cents = g.Sum(r => r.AmountCents) })
                .ToListAsync(cancellationToken))
            .Select(r => new InvoiceAmount(YearMonth.From(r.DueMonth), r.Cents))
            .ToList();

        // RT-12: data crescente; empate pela ordem de criação (o id é UUIDv7, também cronológico).
        var entryItems = await entries
            .Where(e => e.ReferenceMonth == first)
            .OrderBy(e => e.EntryDate).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .Select(e => new MonthEntryItem(
                e.Id, e.Kind, e.Description, e.AmountCents, e.EntryDate, e.ReferenceMonth, e.Status, e.WalletId, e.Notes,
                e.CreatedAt, EF.Property<uint>(e, "xmin")))
            .ToListAsync(cancellationToken);

        var transferItems = await transfers
            .Where(t => t.TransferDate >= first && t.TransferDate <= last)
            .OrderBy(t => t.TransferDate).ThenBy(t => t.CreatedAt).ThenBy(t => t.Id)
            .Select(t => new MonthTransferItem(
                t.Id, t.FromWalletId, t.ToWalletId, t.AmountCents, t.TransferDate, t.Description, t.Notes,
                t.CreatedAt, EF.Property<uint>(t, "xmin")))
            .ToListAsync(cancellationToken);

        // Pendências (research R9) ignoram o filtro de carteiras: valem para o Espaço todo.
        var pending = await db.Set<Entry>().AsNoTracking()
            .Where(e => e.Status == EntryStatus.Planned && e.ReferenceMonth < currentMonth)
            .GroupBy(e => e.Kind)
            .Select(g => new { Kind = g.Key, Count = g.Count(), Cents = g.Sum(e => e.AmountCents) })
            .ToListAsync(cancellationToken);

        return new MonthData(
            wallets,
            entryTotals,
            transferTotals,
            invoicePayments,
            entryItems,
            transferItems,
            new PendingSummary(
                pending.Sum(p => p.Count),
                pending.Where(p => p.Kind == EntryKind.Income).Sum(p => p.Cents),
                pending.Where(p => p.Kind == EntryKind.Expense).Sum(p => p.Cents)));
    }
}
