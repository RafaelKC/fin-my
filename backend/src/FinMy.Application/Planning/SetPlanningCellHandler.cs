using FinMy.Application.Abstractions;
using FinMy.Application.Cards;
using FinMy.Application.Entries;
using FinMy.Domain.Common;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Planning;

/// <summary>
/// <c>PUT /years/{yyyy}/planning/cells</c> (007 research R3, FR-003..FR-006): célula de fatura define/remove a estimativa;
/// de Entrada/Saída com 0 itens cria uma Prevista, com 1 Prevista altera o valor (ou exclui, se vazio) e nos demais casos
/// responde 409 <c>planning.cell_not_editable</c> com os lançamentos da célula. Devolve a linha e os totais já atualizados.
/// </summary>
public sealed class SetPlanningCellHandler(
    GetPlanningGridHandler grid,
    IPlanningReader reader,
    IEntryStore entries,
    IWalletStore wallets,
    ITagStore tags,
    ICreditCardStore cards,
    UpdateEntryHandler update,
    DeleteEntryHandler delete,
    SetInvoiceEstimateHandler estimate)
{
    public async Task<SetPlanningCellResult> HandleAsync(SetPlanningCellCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var before = await grid.HandleAsync(command.Year, cancellationToken);
        var month = PlanningCellSupport.ParseMonth(command.Month, before.Year);

        if (PlanningCellSupport.IsInvoice(command.Kind))
        {
            var cardId = PlanningCellSupport.RequireCard(command.CreditCardId);
            await estimate.HandleAsync(new SetInvoiceEstimateCommand(cardId, month.ToString(), command.Value), cancellationToken);
            return new SetPlanningCellResult(
                await PlanningCellSupport.InvoiceOutcomeAsync(grid, cards, command.Year, cardId, cancellationToken), null);
        }

        var (kind, key) = PlanningCellSupport.RowKey(command.Kind, command.Description);
        var row = PlanningCellSupport.FindRow(before, kind, key);
        var cell = PlanningCellSupport.CellOf(row, month);
        var description = row?.Description ?? command.Description!.Trim();
        var empty = string.IsNullOrWhiteSpace(command.Value);
        Guid? batchId = null;

        if (cell is null or { ItemCount: 0 })
        {
            if (!empty)
            {
                var amount = EntryInput.ParseAmount(command.Value);
                var defaults = await PlanningCellSupport.DefaultsAsync(row, command.WalletId, command.Day, entries, wallets, tags, cancellationToken);
                await entries.AddAsync(
                    PlanningCellSupport.NewPlanned(kind, description, amount, month, defaults, before.Today), defaults.Tags.CommonTagIds, cancellationToken);
            }
        }
        else if (cell is { Editable: true, EntryId: { } entryId })
        {
            if (empty)
            {
                batchId = await delete.HandleAsync(entryId, cancellationToken);
            }
            else
            {
                var current = await entries.GetAsync(entryId, cancellationToken) ?? throw new DomainException(EntryErrors.NotFound);
                await update.HandleAsync(
                    new UpdateEntryCommand(
                        entryId, EntryInput.ToText(current.Kind), current.Description, command.Value, current.Date, current.WalletId,
                        EntryInput.ToText(current.Status), current.ReferenceMonth.ToString(), current.Notes, current.Version, current.TagIds),
                    cancellationToken);
            }
        }
        else
        {
            throw new DomainException(PlanningErrors.CellNotEditable(await ItemsAsync(before, kind, key, month, cancellationToken)));
        }

        return new SetPlanningCellResult(
            await PlanningCellSupport.EntryOutcomeAsync(grid, command.Year, kind, key, description, cancellationToken), batchId);
    }

    private async Task<IReadOnlyList<PlanningCellItem>> ItemsAsync(
        PlanningGridResult before, EntryKind kind, string key, YearMonth month, CancellationToken cancellationToken)
    {
        var data = await reader.ReadAsync(before.Year, before.Today, cancellationToken);
        return data.Entries
            .Where(e => e.Kind == kind && e.Month == month && $"{PlanningKinds.Text(kind)}:{e.DescriptionNormalized}" == key)
            .OrderBy(e => e.Date).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .Select(e => new PlanningCellItem(e.Id, e.Description, e.AmountCents, e.Date, EntryInput.ToText(e.Status), e.WalletId))
            .ToList();
    }
}
