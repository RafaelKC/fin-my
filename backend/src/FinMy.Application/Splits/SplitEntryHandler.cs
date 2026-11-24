using FinMy.Application.Abstractions;
using FinMy.Application.Entries;
using FinMy.Domain.Common;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Splits;

/// <summary>
/// Divide uma Saída entre pessoas (004 US4, FR-012/FR-013): cria uma Saída independente por parte, com os demais campos
/// iguais e o mesmo <c>split_group_id</c>, numa única gravação. Só <c>expense</c>.
/// </summary>
public sealed class SplitEntryHandler(IEntryStore store, ITagStore tags, IClock clock)
{
    public async Task<IReadOnlyList<EntryView>> HandleAsync(CreateEntryCommand command, SplitCommand? split, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (EntryInput.ParseKind(command.Kind) != EntryKind.Expense)
        {
            throw new DomainException(SplitErrors.ExpenseOnly);
        }

        var total = EntryInput.ParseAmount(command.Amount);
        var date = command.Date ?? throw new DomainException(EntryErrors.Field("date.required", "date", "Informe a data"));
        var walletId = command.WalletId is { } id && id != Guid.Empty
            ? id
            : throw new DomainException(EntryErrors.Field("wallet.required", "walletId", "Escolha a carteira"));
        EntryStatus? status = command.Status is null ? null : EntryInput.ParseStatus(command.Status);
        var referenceMonth = string.IsNullOrEmpty(command.ReferenceMonth) ? (YearMonth?)null : EntryInput.ParseReferenceMonth(command.ReferenceMonth);

        var wallet = await store.GetWalletAsync(walletId, cancellationToken) ?? throw new DomainException(EntryErrors.WalletNotFound);
        if (wallet.IsArchived)
        {
            throw new DomainException(EntryErrors.WalletArchived);
        }

        var parts = await SplitResolver.PlanAsync(tags, total.Cents, split, cancellationToken);
        var commonTagIds = await SplitResolver.CommonTagsAsync(tags, command.TagIds, cancellationToken);

        var groupId = Guid.CreateVersion7();
        var items = new List<(Entry Entry, IReadOnlyCollection<Guid> CommonTagIds)>(parts.Count);
        foreach (var part in parts)
        {
            var entry = Entry.Create(
                EntryKind.Expense, command.Description!, Money.FromCents(part.AmountCents), date, walletId, status, referenceMonth, command.Notes, clock.Today);
            entry.ChangeSettlementTag(part.SettlementTagId);
            entry.ChangeSplitGroup(groupId);
            items.Add((entry, commonTagIds));
        }

        return await store.AddManyAsync(items, cancellationToken);
    }
}
