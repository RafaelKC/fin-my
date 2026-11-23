using FinMy.Application.Abstractions;
using FinMy.Domain.Cards;
using FinMy.Domain.Common;

namespace FinMy.Application.Cards;

/// <summary>
/// Exclui (lógico) a compra, ou as parcelas do escopo (<c>this</c>, <c>this_and_following</c>, <c>all</c>, FR-016), num
/// único lote que a pessoa pode desfazer (RT-09). O par de "saldo levado" vai junto (FR-020); o plano sem parcelas também.
/// </summary>
public sealed class DeleteCardPurchaseHandler(ICardPurchaseStore purchases, ICurrentContext context)
{
    public async Task<Guid> HandleAsync(Guid id, string? scope, CancellationToken cancellationToken)
    {
        var purchase = await purchases.FindAsync(id, cancellationToken) ?? throw new DomainException(CardPurchaseErrors.NotFound);
        var parsed = CardPurchaseInput.ParseScope(scope, allowAll: true);

        var batchId = context.BeginDeletionBatch();

        if (purchase.CarryOverPairId is { } pairId)
        {
            foreach (var one in await purchases.ListCarryOverPairAsync(pairId, cancellationToken))
            {
                purchases.Remove(one);
            }
        }
        else if (purchase.InstallmentPlanId is { } planId)
        {
            var parcels = await purchases.ListPlanAsync(planId, cancellationToken);
            var doomed = parsed switch
            {
                PurchaseScope.All => parcels.ToList(),
                PurchaseScope.ThisAndFollowing => parcels.Where(p => p.InstallmentNumber >= purchase.InstallmentNumber).ToList(),
                _ => [purchase],
            };

            foreach (var parcel in doomed)
            {
                purchases.Remove(parcel);
            }

            if (doomed.Count == parcels.Count)
            {
                purchases.Remove(await purchases.FindPlanAsync(planId, cancellationToken) ?? throw new InvalidOperationException("Plano da parcela não encontrado."));
            }
        }
        else
        {
            purchases.Remove(purchase);
        }

        await purchases.SaveAsync(cancellationToken);
        return batchId;
    }
}
