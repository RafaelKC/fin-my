using FinMy.Application.Abstractions;
using FinMy.Domain.Cards;
using FinMy.Domain.Entries;
using FinMy.Domain.Transfers;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Infrastructure.Persistence;

/// <summary>Localiza e exclui (lógico) o que o Claude criou, junto com o grupo de divisão/parcelas (008 research R4).</summary>
public sealed class UndoStore(FinMyDbContext db) : IUndoStore
{
    public async Task<CreatedItem?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var entry = await db.Set<Entry>().AsNoTracking().Where(e => e.Id == id)
            .Select(e => new CreatedItem(CreatedItemType.Entry, e.Id, e.CreatedBy, e.CreatedVia, e.CreatedAt))
            .SingleOrDefaultAsync(cancellationToken);
        if (entry is not null)
        {
            return entry;
        }

        var purchase = await db.Set<CardPurchase>().AsNoTracking().Where(p => p.Id == id)
            .Select(p => new CreatedItem(CreatedItemType.CardPurchase, p.Id, p.CreatedBy, p.CreatedVia, p.CreatedAt))
            .SingleOrDefaultAsync(cancellationToken);
        if (purchase is not null)
        {
            return purchase;
        }

        return await db.Set<Transfer>().AsNoTracking().Where(t => t.Id == id)
            .Select(t => new CreatedItem(CreatedItemType.Transfer, t.Id, t.CreatedBy, t.CreatedVia, t.CreatedAt))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<int> DeleteWithGroupAsync(CreatedItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        var count = item.Type switch
        {
            CreatedItemType.Entry => await RemoveEntriesAsync(item.Id, cancellationToken),
            CreatedItemType.CardPurchase => await RemovePurchasesAsync(item.Id, cancellationToken),
            _ => await RemoveTransferAsync(item.Id, cancellationToken),
        };

        await db.SaveChangesAsync(cancellationToken);
        return count;
    }

    private async Task<int> RemoveEntriesAsync(Guid id, CancellationToken cancellationToken)
    {
        var entry = await db.Set<Entry>().SingleAsync(e => e.Id == id, cancellationToken);
        var group = entry.SplitGroupId is { } groupId
            ? await db.Set<Entry>().Where(e => e.SplitGroupId == groupId).ToListAsync(cancellationToken)
            : [entry];
        db.RemoveRange(group);
        return group.Count;
    }

    private async Task<int> RemovePurchasesAsync(Guid id, CancellationToken cancellationToken)
    {
        var purchase = await db.Set<CardPurchase>().SingleAsync(p => p.Id == id, cancellationToken);
        var group = purchase.SplitGroupId is { } groupId
            ? await db.Set<CardPurchase>().Where(p => p.SplitGroupId == groupId).ToListAsync(cancellationToken)
            : [purchase];

        // Cada parte de um parcelamento leva todas as parcelas e o próprio plano.
        var planIds = group.Where(p => p.InstallmentPlanId is not null).Select(p => p.InstallmentPlanId!.Value).Distinct().ToList();
        var all = planIds.Count == 0
            ? group
            : group.Concat(await db.Set<CardPurchase>().Where(p => p.InstallmentPlanId != null && planIds.Contains(p.InstallmentPlanId.Value) && !group.Contains(p))
                .ToListAsync(cancellationToken)).ToList();

        db.RemoveRange(all);
        db.RemoveRange(await db.Set<InstallmentPlan>().Where(p => planIds.Contains(p.Id)).ToListAsync(cancellationToken));
        return all.Count;
    }

    private async Task<int> RemoveTransferAsync(Guid id, CancellationToken cancellationToken)
    {
        db.Remove(await db.Set<Transfer>().SingleAsync(t => t.Id == id, cancellationToken));
        return 1;
    }
}
