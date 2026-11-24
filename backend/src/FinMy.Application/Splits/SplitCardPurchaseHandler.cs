using FinMy.Application.Abstractions;
using FinMy.Application.Cards;
using FinMy.Application.Tags;
using FinMy.Domain.Cards;
using FinMy.Domain.Common;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Splits;

/// <summary>
/// Divide uma compra no cartão entre pessoas (004 US4, FR-012/FR-013, research R5): cada parte vira uma compra à vista ou
/// um parcelamento próprio, com o valor da parte como total (003 FR-013) e o mesmo <c>split_group_id</c>, numa só transação.
/// Crédito e parcelamento em andamento não se dividem.
/// </summary>
public sealed class SplitCardPurchaseHandler(CreateCardPurchaseHandler create, ICardPurchaseStore purchases, ITagStore tags)
{
    public async Task<CardPurchaseCreateResult> HandleAsync(CreateCardPurchaseCommand command, SplitCommand? split, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var (amount, negative) = CardPurchaseInput.ParseSignedAmount(command.Amount);
        if (negative || CardPurchaseInput.ParseNature(command.Nature) == PurchaseNature.Credit)
        {
            throw new DomainException(SplitErrors.CreditNotAllowed);
        }

        // "Valor da parcela" vira total (valor × N) antes de dividir; cada parte é então um parcelamento por valor total.
        var total = amount.Cents;
        var partInstallments = command.Installments;
        if (command.Installments is { } installments)
        {
            var mode = CardPurchaseInput.ParseMode(installments.Mode);
            if (mode == InstallmentMode.InProgress)
            {
                throw new DomainException(SplitErrors.InProgressNotAllowed);
            }

            if (installments.Total is > 1 and var count)
            {
                if (mode == InstallmentMode.InstallmentAmount)
                {
                    total = checked(total * count);
                    if (total > Money.MaxCents)
                    {
                        throw new DomainException(ValueExpressionErrors.ToDomainError(ValueExpressionErrors.TooLarge, "amount"));
                    }
                }

                partInstallments = new InstallmentsCommand(count, "total_amount", null, null);
            }
        }

        var parts = await SplitResolver.PlanAsync(tags, total, split, cancellationToken);
        var selection = new TagSelection(null, await SplitResolver.CommonTagsAsync(tags, command.TagIds, cancellationToken));

        var prepared = new List<(CreateCardPurchaseHandler.Prepared Value, Guid? SettlementTagId)>(parts.Count);
        foreach (var part in parts)
        {
            var partCommand = command with { Installments = partInstallments };
            prepared.Add((await create.PrepareAsync(partCommand, (Money.FromCents(part.AmountCents), false), cancellationToken, selection), part.SettlementTagId));
        }

        var groupId = Guid.CreateVersion7();
        var created = new List<CardPurchase>();
        await using var transaction = await purchases.BeginTransactionAsync(cancellationToken);
        foreach (var (value, settlementTagId) in prepared)
        {
            await create.StageAsync(value, (settlementTagId, groupId), created, cancellationToken);
        }

        await purchases.SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var views = (await purchases.GetViewsAsync(created.Select(p => p.Id).ToList(), cancellationToken)).ToDictionary(v => v.Id);
        return new CardPurchaseCreateResult(created.Select(p => views[p.Id]).ToList(), []);
    }
}
