using FinMy.Application.Abstractions;
using FinMy.Application.Cards;
using FinMy.Application.Entries;
using FinMy.Application.Tags;
using FinMy.Domain.Cards;
using FinMy.Domain.Common;
using FinMy.Domain.Entries;

using FinMy.Domain.Monetary;

namespace FinMy.Application.Planning;

/// <summary>Valores de uma Prevista nova da grade (FR-004, FR-005): carteira, dia e tags da linha, ou os de "Nova linha", ou os padrões.</summary>
internal sealed record PlanningNewEntryDefaults(Guid WalletId, int Day, TagSelection Tags);

/// <summary>Regras compartilhadas por <see cref="SetPlanningCellHandler"/> e <see cref="FillPlanningMonthsHandler"/>.</summary>
internal static class PlanningCellSupport
{
    public static YearMonth ParseMonth(string? text, int year)
    {
        if (!YearMonth.TryParse(text, out var month) || month.Year != year)
        {
            throw new DomainException(PlanningErrors.Field("month.invalid", "month", $"Use um mês de {year} no formato AAAA-MM"));
        }

        return month;
    }

    public static bool IsInvoice(string? kind) => kind == PlanningKinds.Invoice;

    /// <summary>Chave da linha: o tipo e a descrição normalizada (FR-002).</summary>
    public static (EntryKind Kind, string Key) RowKey(string? kind, string? description)
    {
        var parsed = EntryInput.ParseKind(kind);
        var normalized = TextNormalizer.Normalize(description ?? string.Empty);
        if (normalized.Length == 0)
        {
            throw new DomainException(PlanningErrors.Field("description.required", "description", "Informe a descrição"));
        }

        return (parsed, $"{PlanningKinds.Text(parsed)}:{normalized}");
    }

    public static PlanningRow? FindRow(PlanningGridResult grid, EntryKind kind, string key) =>
        (kind == EntryKind.Income ? grid.IncomeRows : grid.ExpenseRows).FirstOrDefault(r => r.Key == key);

    public static PlanningCell? CellOf(PlanningRow? row, YearMonth month) => row?.Cells.FirstOrDefault(c => c.Month == month);

    /// <summary>Linha vazia: a que some do ano ao perder o último lançamento (o cliente a mantém enquanto a tela está aberta).</summary>
    public static PlanningRow EmptyRow(int year, EntryKind kind, string key, string description) => new(
        kind, key, description, null, 1, [],
        Enumerable.Range(1, 12).Select(m => new PlanningCell(YearMonth.Of(year, m), 0, 0, false, true, null)).ToList());

    public static InvoiceRow EmptyInvoiceRow(int year, CreditCard card) => new(
        card.Id, card.Name, card.IsArchived,
        Enumerable.Range(1, 12).Select(m => new InvoiceCell(YearMonth.Of(year, m), 0, 0, null)).ToList());

    public static async Task<PlanningNewEntryDefaults> DefaultsAsync(
        PlanningRow? row, Guid? requestedWalletId, int? requestedDay, IEntryStore entries, IWalletStore wallets, ITagStore tags, CancellationToken cancellationToken)
    {
        if (requestedDay is < 1 or > 31)
        {
            throw new DomainException(PlanningErrors.Field("day.invalid", "day", "Use um dia entre 1 e 31"));
        }

        Guid walletId;
        if (row?.LatestWalletId is { } latest)
        {
            walletId = latest;
        }
        else if (requestedWalletId is { } requested && requested != Guid.Empty)
        {
            walletId = requested;
        }
        else
        {
            walletId = (await wallets.FindPrimaryAsync(cancellationToken))?.Id
                ?? throw new DomainException(EntryErrors.Field("wallet.required", "walletId", "Escolha a carteira"));
        }

        var wallet = await entries.GetWalletAsync(walletId, cancellationToken) ?? throw new DomainException(EntryErrors.WalletNotFound);
        if (wallet.IsArchived)
        {
            throw new DomainException(EntryErrors.WalletArchived);
        }

        // As tags da linha vêm do lançamento mais recente; as arquivadas não são levadas para lançamentos novos.
        var selection = TagSelection.None;
        if (row is { LatestTagIds.Count: > 0 })
        {
            var found = await tags.FindManyAsync(row.LatestTagIds, cancellationToken);
            selection = await TagSelector.ResolveAsync(tags, found.Where(t => !t.IsArchived).Select(t => t.Id).ToList(), [], cancellationToken);
        }

        return new PlanningNewEntryDefaults(walletId, row?.LatestDay ?? requestedDay ?? 1, selection);
    }

    public static Entry NewPlanned(
        EntryKind kind, string description, Money amount, YearMonth month, PlanningNewEntryDefaults defaults, DateOnly today)
    {
        var date = new DateOnly(month.Year, month.Month, Math.Min(defaults.Day, DateTime.DaysInMonth(month.Year, month.Month)));
        var entry = Entry.Create(kind, description, amount, date, defaults.WalletId, EntryStatus.Planned, month, null, today);
        entry.ChangeSettlementTag(defaults.Tags.SettlementTagId);
        return entry;
    }

    /// <summary>Lê a linha (ou, sem ela, uma vazia) já com os totais após a edição.</summary>
    public static async Task<PlanningRowOutcome> EntryOutcomeAsync(
        GetPlanningGridHandler grid, string? year, EntryKind kind, string key, string description, CancellationToken cancellationToken)
    {
        var after = await grid.HandleAsync(year, cancellationToken);
        return new PlanningRowOutcome(FindRow(after, kind, key) ?? EmptyRow(after.Year, kind, key, description), null, after.Totals);
    }

    public static async Task<PlanningRowOutcome> InvoiceOutcomeAsync(
        GetPlanningGridHandler grid, ICreditCardStore cards, string? year, Guid cardId, CancellationToken cancellationToken)
    {
        var after = await grid.HandleAsync(year, cancellationToken);
        var row = after.InvoiceRows.FirstOrDefault(r => r.CreditCardId == cardId)
            ?? EmptyInvoiceRow(after.Year, await cards.FindAsync(cardId, cancellationToken) ?? throw new DomainException(CreditCardErrors.NotFound));
        return new PlanningRowOutcome(null, row, after.Totals);
    }

    public static Guid RequireCard(Guid? cardId) =>
        cardId is { } id && id != Guid.Empty
            ? id
            : throw new DomainException(PlanningErrors.Field("creditCardId.required", "creditCardId", "Escolha o cartão"));
}
