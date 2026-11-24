using FinMy.Application.Abstractions;
using FinMy.Application.Cards;
using FinMy.Application.Entries;
using FinMy.Domain.Common;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Planning;

/// <summary>
/// <c>POST /years/{yyyy}/planning/fill</c> (007 research R3, cenários 1.8 e 2.5): cria uma Prevista (ou, no cartão, define a
/// estimativa) em cada mês escolhido que ainda esteja vazio na linha; os demais são pulados e contados. Valor e meses são
/// validados antes de gravar; as Previstas entram numa única gravação (tudo ou nada).
/// </summary>
public sealed class FillPlanningMonthsHandler(
    GetPlanningGridHandler grid,
    IEntryStore entries,
    IWalletStore wallets,
    ITagStore tags,
    ICreditCardStore cards,
    SetInvoiceEstimateHandler estimate)
{
    public async Task<FillPlanningMonthsResult> HandleAsync(FillPlanningMonthsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var before = await grid.HandleAsync(command.Year, cancellationToken);
        var months = ParseMonths(command.Months, before.Year);

        if (PlanningCellSupport.IsInvoice(command.Kind))
        {
            return await FillInvoiceAsync(command, before, months, cancellationToken);
        }

        var (kind, key) = PlanningCellSupport.RowKey(command.Kind, command.Description);
        var amount = EntryInput.ParseAmount(command.Amount);
        var row = PlanningCellSupport.FindRow(before, kind, key);
        var description = row?.Description ?? command.Description!.Trim();
        var targets = months.Where(m => PlanningCellSupport.CellOf(row, m) is null or { ItemCount: 0 }).ToList();

        if (targets.Count > 0)
        {
            var defaults = await PlanningCellSupport.DefaultsAsync(row, command.WalletId, command.Day, entries, wallets, tags, cancellationToken);
            await entries.AddManyAsync(
                targets.Select(m => (PlanningCellSupport.NewPlanned(kind, description, amount, m, defaults, before.Today), (IReadOnlyCollection<Guid>)defaults.Tags.CommonTagIds)).ToList(),
                cancellationToken);
        }

        return new FillPlanningMonthsResult(
            targets.Count,
            months.Count - targets.Count,
            await PlanningCellSupport.EntryOutcomeAsync(grid, command.Year, kind, key, description, cancellationToken));
    }

    private async Task<FillPlanningMonthsResult> FillInvoiceAsync(
        FillPlanningMonthsCommand command, PlanningGridResult before, List<YearMonth> months, CancellationToken cancellationToken)
    {
        var cardId = PlanningCellSupport.RequireCard(command.CreditCardId);
        _ = await cards.FindAsync(cardId, cancellationToken) ?? throw new DomainException(CreditCardErrors.NotFound);
        _ = CardPurchaseInput.ParsePositiveAmount(command.Amount);

        var row = before.InvoiceRows.FirstOrDefault(r => r.CreditCardId == cardId);
        var targets = months.Where(m => row?.Cells.FirstOrDefault(c => c.Month == m)?.EstimateCents is null).ToList();
        foreach (var month in targets)
        {
            await estimate.HandleAsync(new SetInvoiceEstimateCommand(cardId, month.ToString(), command.Amount), cancellationToken);
        }

        return new FillPlanningMonthsResult(
            targets.Count,
            months.Count - targets.Count,
            await PlanningCellSupport.InvoiceOutcomeAsync(grid, cards, command.Year, cardId, cancellationToken));
    }

    private static List<YearMonth> ParseMonths(IReadOnlyList<string>? months, int year)
    {
        if (months is null || months.Count == 0)
        {
            throw new DomainException(PlanningErrors.Field("months.required", "months", "Escolha ao menos um mês"));
        }

        return [.. months.Select(m => PlanningCellSupport.ParseMonth(m, year)).Distinct().OrderBy(m => m)];
    }
}
