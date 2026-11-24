using FinMy.Application.Abstractions;
using FinMy.Application.Cards;
using FinMy.Application.Entries;
using FinMy.Application.Tags;
using FinMy.Domain.Cards;
using FinMy.Domain.Common;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;
using FinMy.Domain.Recurrences;

namespace FinMy.Application.Recurrences;

/// <param name="FromMonth"><c>AAAA-MM</c>; ausente = mês de hoje.</param>
/// <param name="Version">Obrigatória; divergente da atual → conflito de concorrência.</param>
public sealed record UpdateRecurrenceCommand(
    Guid Id,
    string? Kind,
    string? Description,
    string? Amount,
    int? DayOfMonth,
    Guid? WalletId,
    Guid? CreditCardId,
    IReadOnlyList<Guid>? TagIds,
    string? EndType,
    string? EndMonth,
    int? OccurrenceLimit,
    string? ReferenceMonthRule,
    string? FromMonth,
    long? Version);

/// <summary>A recorrência e quantas ocorrências a mudança alcançou (alteradas ou excluídas).</summary>
public sealed record RecurrenceChangeResult(RecurrenceView Recurrence, int AffectedCount);

/// <summary>
/// Altera o modelo da recorrência "a partir de um mês" (005 US3, FR-006, research R4): as ocorrências elegíveis daquele mês em
/// diante recebem os valores novos (ou são excluídas se passaram do novo término); as Pagas e as alteradas não mudam.
/// </summary>
public sealed class UpdateRecurrenceHandler(
    IRecurrenceStore store,
    IEntryStore wallets,
    ICreditCardStore cards,
    IInvoiceRepository invoices,
    ICardPurchaseStore purchases,
    ITagStore tags,
    ICurrentContext context,
    RecurrenceOccurrenceLoader loader,
    RecurrenceGenerator generator,
    RecurrenceViewBuilder views,
    IClock clock)
{
    public async Task<RecurrenceChangeResult> HandleAsync(UpdateRecurrenceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var recurrence = await store.FindAsync(command.Id, cancellationToken) ?? throw new DomainException(RecurrenceErrors.NotFound);
        if (RecurrenceInput.ParseKind(command.Kind) != recurrence.Kind)
        {
            throw new DomainException(RecurrenceErrors.KindImmutable);
        }

        var version = command.Version ?? throw new DomainException(RecurrenceErrors.Field("version.required", "version", "Informe a versão da recorrência"));
        if (store.VersionOf(recurrence) != version)
        {
            throw new DomainException(RecurrenceErrors.ConcurrencyConflict);
        }

        store.ExpectVersion(recurrence, version);

        var amount = EntryInput.ParseAmount(command.Amount);
        var endType = RecurrenceInput.ParseEndType(command.EndType);
        var endMonth = RecurrenceInput.ParseOptionalMonth(command.EndMonth, "endMonth");
        var rule = RecurrenceInput.ParseReferenceMonthRule(command.ReferenceMonthRule);
        if (recurrence.Kind != RecurrenceKind.CardPurchase)
        {
            rule ??= ReferenceMonthRule.SameMonth;
        }

        var fromMonth = RecurrenceInput.ParseOptionalMonth(command.FromMonth, "fromMonth") ?? YearMonth.From(clock.Today);
        if (fromMonth < recurrence.StartYearMonth)
        {
            throw new DomainException(RecurrenceErrors.FromMonthBeforeStart);
        }

        var walletId = command.WalletId is { } w && w != Guid.Empty ? w : (Guid?)null;
        var cardId = command.CreditCardId is { } c && c != Guid.Empty ? c : (Guid?)null;
        var previousWalletId = recurrence.WalletId;
        var previousCardId = recurrence.CreditCardId;

        var currentTagIds = (await store.GetTagIdsAsync([recurrence], cancellationToken))[recurrence.Id];
        var selection = await TagSelector.ResolveAsync(tags, command.TagIds, currentTagIds, cancellationToken);

        // Valida as regras da recorrência (tipo × carteira/cartão, dia, término) antes de consultar o banco.
        recurrence.ChangeFrom(
            command.Description!, amount, command.DayOfMonth ?? 0, walletId, cardId, selection.SettlementTagId,
            endType, endMonth, command.OccurrenceLimit, rule);

        // Como nas ocorrências comuns, carteira e cartão só precisam estar ativos quando são trocados.
        if (walletId is { } id && id != previousWalletId)
        {
            var wallet = await wallets.GetWalletAsync(id, cancellationToken) ?? throw new DomainException(EntryErrors.WalletNotFound);
            if (wallet.IsArchived)
            {
                throw new DomainException(EntryErrors.WalletArchived);
            }
        }

        CreditCard? card = null;
        if (cardId is { } cardIdValue)
        {
            card = await CardPurchaseInput.RequireCardAsync(cards, cardIdValue, mustBeActive: cardIdValue != previousCardId, cancellationToken);
        }

        var plan = RecurrenceScheduler.Occurrences(
            recurrence, fromMonth.Previous(), clock.Today, card is null ? null : (card.ClosingDay, card.DueDay))
            .ToDictionary(o => o.Month);

        context.BeginDeletionBatch();
        var affected = 0;
        await using (var transaction = await store.BeginTransactionAsync(cancellationToken))
        {
            await store.ReplaceTagsAsync(recurrence, selection.CommonTagIds, cancellationToken);
            var set = await loader.LoadAsync(recurrence.Id, fromMonth, cancellationToken);

            foreach (var entry in set.EligibleEntries)
            {
                affected++;
                if (plan.TryGetValue(YearMonth.From(entry.RecurrenceMonth!.Value), out var occurrence))
                {
                    await ApplyAsync(entry, recurrence, occurrence, selection, cancellationToken);
                }
                else
                {
                    store.Remove(entry); // passou do novo término
                }
            }

            foreach (var purchase in set.EligiblePurchases)
            {
                affected++;
                if (plan.TryGetValue(YearMonth.From(purchase.RecurrenceMonth!.Value), out var occurrence))
                {
                    await ApplyAsync(purchase, recurrence, card!, occurrence, selection, set.Invoices, cancellationToken);
                }
                else
                {
                    purchases.Remove(purchase);
                }
            }

            var allTagIds = selection.SettlementTagId is { } settlement ? [settlement, .. selection.CommonTagIds] : selection.CommonTagIds;
            await generator.StageAsync(recurrence, allTagIds, cancellationToken);
            await store.SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return new RecurrenceChangeResult((await views.BuildAsync([recurrence], cancellationToken))[0], affected);
    }

    private async Task ApplyAsync(
        Entry entry, Recurrence recurrence, RecurrenceOccurrence occurrence, TagSelection selection, CancellationToken cancellationToken)
    {
        entry.ChangeDescription(recurrence.Description);
        entry.ChangeAmount(recurrence.Amount);
        entry.ChangeWallet(recurrence.WalletId!.Value);
        entry.ChangeDate(occurrence.Date);
        entry.ChangeReferenceMonth(occurrence.ReferenceMonth!.Value);
        entry.ChangeSettlementTag(selection.SettlementTagId);
        await store.ReplaceOccurrenceTagsAsync(entry, selection.CommonTagIds, cancellationToken);
    }

    private async Task ApplyAsync(
        CardPurchase purchase,
        Recurrence recurrence,
        CreditCard card,
        RecurrenceOccurrence occurrence,
        TagSelection selection,
        IReadOnlyDictionary<Guid, Invoice> invoiceRows,
        CancellationToken cancellationToken)
    {
        purchase.ChangeDescription(recurrence.Description);
        purchase.ChangeAmount(recurrence.Amount);
        purchase.ChangeDate(occurrence.Date);
        purchase.ChangeSettlementTag(selection.SettlementTagId);
        await purchases.ReplaceTagsAsync(purchase, selection.CommonTagIds, cancellationToken);

        // Dia novo recalcula a fatura (003 FR-008); compra movida à mão mantém o mês (no cartão novo, se mudou).
        var oldMonth = invoiceRows[purchase.InvoiceId].DueYearMonth;
        var month = purchase.ManuallyMoved ? oldMonth : occurrence.InvoiceDueMonth!.Value;
        if (card.Id != purchase.CreditCardId || month != oldMonth)
        {
            purchase.Reassign(card.Id, (await invoices.GetOrCreateAsync(card.Id, month, cancellationToken)).Id);
        }
    }
}

/// <param name="FromMonth"><c>AAAA-MM</c>; ausente = mês de hoje.</param>
public sealed record EndRecurrenceCommand(Guid Id, string? FromMonth);

/// <param name="DeletionBatchId">Lote das ocorrências excluídas (a interface não oferece "Desfazer": o cliente confirma antes).</param>
public sealed record EndRecurrenceResult(RecurrenceView Recurrence, int DeletedCount, Guid DeletionBatchId);

/// <summary>
/// Encerra a recorrência "a partir de M" (005 FR-007, research R4): o término passa a ser <c>M − 1</c> e as ocorrências
/// elegíveis de M em diante são excluídas, num lote. Pagas e alteradas permanecem como lançamentos comuns ligados à recorrência.
/// </summary>
public sealed class EndRecurrenceHandler(
    IRecurrenceStore store,
    ICardPurchaseStore purchases,
    ICurrentContext context,
    RecurrenceOccurrenceLoader loader,
    RecurrenceViewBuilder views,
    IClock clock)
{
    public async Task<EndRecurrenceResult> HandleAsync(EndRecurrenceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var recurrence = await store.FindAsync(command.Id, cancellationToken) ?? throw new DomainException(RecurrenceErrors.NotFound);
        var fromMonth = RecurrenceInput.ParseOptionalMonth(command.FromMonth, "fromMonth") ?? YearMonth.From(clock.Today);

        // Já encerrada, ou o término atual já é anterior a M: encerrar de novo estenderia ou repetiria.
        if (RecurrenceViewBuilder.StatusOf(recurrence, clock.Today) == RecurrenceViewBuilder.Ended
            || recurrence.EndYearMonth is { } end && end < fromMonth)
        {
            throw new DomainException(RecurrenceErrors.AlreadyEnded);
        }

        recurrence.End(fromMonth);

        var batchId = context.BeginDeletionBatch();
        var set = await loader.LoadAsync(recurrence.Id, fromMonth, cancellationToken);
        foreach (var entry in set.EligibleEntries)
        {
            store.Remove(entry);
        }

        foreach (var purchase in set.EligiblePurchases)
        {
            purchases.Remove(purchase);
        }

        await store.SaveAsync(cancellationToken);
        return new EndRecurrenceResult(
            (await views.BuildAsync([recurrence], cancellationToken))[0],
            set.EligibleEntries.Count + set.EligiblePurchases.Count,
            batchId);
    }
}

/// <param name="DeletedCount">Ocorrências excluídas (elegíveis do mês de hoje em diante).</param>
/// <param name="UnlinkedCount">Ocorrências que sobraram e viraram lançamentos comuns.</param>
public sealed record DeleteRecurrenceResult(int DeletedCount, int UnlinkedCount);

/// <summary>
/// Exclui a recorrência = encerrar a partir do mês de hoje; o que sobra das ocorrências perde o vínculo e vira lançamento
/// comum (005 FR-007, research R4).
/// </summary>
public sealed class DeleteRecurrenceHandler(
    IRecurrenceStore store,
    ICardPurchaseStore purchases,
    ICurrentContext context,
    RecurrenceOccurrenceLoader loader,
    IClock clock)
{
    public async Task<DeleteRecurrenceResult> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var recurrence = await store.FindAsync(id, cancellationToken) ?? throw new DomainException(RecurrenceErrors.NotFound);

        context.BeginDeletionBatch();
        var set = await loader.LoadAsync(recurrence.Id, YearMonth.From(clock.Today), cancellationToken);
        foreach (var entry in set.EligibleEntries)
        {
            store.Remove(entry);
        }

        foreach (var purchase in set.EligiblePurchases)
        {
            purchases.Remove(purchase);
        }

        foreach (var entry in set.OtherEntries)
        {
            entry.UnlinkFromRecurrence();
        }

        foreach (var purchase in set.OtherPurchases)
        {
            purchase.UnlinkFromRecurrence();
        }

        store.Remove(recurrence);
        await store.SaveAsync(cancellationToken);
        return new DeleteRecurrenceResult(
            set.EligibleEntries.Count + set.EligiblePurchases.Count,
            set.OtherEntries.Count + set.OtherPurchases.Count);
    }
}
