using FinMy.Application.Abstractions;
using FinMy.Application.Cards;
using FinMy.Application.Entries;
using FinMy.Application.Tags;
using FinMy.Domain.Common;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Planning;

/// <summary>
/// <c>POST /years/{yyyy}/planning/copy</c> (007 US3.2, US3.4, FR-013, research R4): cria no ano as Previstas dos lançamentos das
/// linhas escolhidas do ano anterior — mesmo mês, mesmo dia (RT-03), mesma carteira (a principal se arquivada), mesmas tags
/// ativas, Mês de referência + 1 ano — e, se pedido, as estimativas de fatura que o ano ainda não tem. Pula o que já tem valor
/// na linha de destino (mesma chave) no mês. Sem vínculo com a origem. Chaves e valores são validados antes de gravar; as
/// Previstas entram numa única gravação (tudo ou nada).
/// </summary>
public sealed class CopyPlanningHandler(
    IPlanningReader reader,
    IEntryStore entries,
    IWalletStore wallets,
    ITagStore tags,
    SetInvoiceEstimateHandler estimate,
    IClock clock)
{
    public async Task<CopyPlanningResult> HandleAsync(CopyPlanningCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var year = PlanningCopySupport.ParseYear(command.Year);
        var keys = (command.RowKeys ?? []).Distinct().ToList();
        var copyEstimates = command.CopyEstimates == true;
        if (keys.Count == 0 && !copyEstimates)
        {
            throw new DomainException(PlanningErrors.Field("rowKeys.required", "rowKeys", "Escolha ao menos uma linha ou as estimativas de cartão"));
        }

        var today = clock.Today;
        var previous = await reader.ReadAsync(year - 1, today, cancellationToken);
        var current = await reader.ReadAsync(year, today, cancellationToken);

        var sources = PlanningCopySupport.Copyable(previous).ToLookup(PlanningCopySupport.Key);
        var unknown = keys.Where(k => !sources.Contains(k)).ToList();
        if (unknown.Count > 0)
        {
            throw new DomainException(PlanningErrors.Field("rowKeys.invalid", "rowKeys", "Há linhas que não existem no ano anterior"));
        }

        var created = 0;
        var skipped = 0;
        var items = new List<(Entry Entry, IReadOnlyCollection<Guid> CommonTagIds)>();
        if (keys.Count > 0)
        {
            var taken = current.Entries.Select(e => (PlanningCopySupport.Key(e), e.Month)).ToHashSet();
            var primary = await PrimaryWalletAsync(previous, sources, keys, wallets, cancellationToken);
            var activeTags = await ActiveTagsAsync(keys.SelectMany(k => sources[k]).SelectMany(e => e.TagIds).Distinct().ToList(), cancellationToken);

            foreach (var source in keys.SelectMany(k => sources[k]).OrderBy(e => e.Date).ThenBy(e => e.CreatedAt).ThenBy(e => e.Id))
            {
                var month = YearMonth.Of(year, source.Month.Month);
                if (taken.Contains((PlanningCopySupport.Key(source), month)))
                {
                    skipped++;
                    continue;
                }

                var selection = await TagSelector.ResolveAsync(tags, [.. source.TagIds.Where(activeTags.Contains)], [], cancellationToken);
                var entry = Entry.Create(
                    source.Kind,
                    source.Description,
                    new Money(source.AmountCents),
                    source.Date.AddYears(1),
                    current.ActiveWalletIds.Contains(source.WalletId) ? source.WalletId : primary!.Value,
                    EntryStatus.Planned,
                    source.Month.AddMonths(12),
                    null,
                    today);
                entry.ChangeSettlementTag(selection.SettlementTagId);
                items.Add((entry, selection.CommonTagIds));
                created++;
            }
        }

        if (items.Count > 0)
        {
            await entries.AddManyAsync(items, cancellationToken);
        }

        if (copyEstimates)
        {
            var (estimatesCreated, estimatesSkipped) = await CopyEstimatesAsync(previous, current, year, cancellationToken);
            created += estimatesCreated;
            skipped += estimatesSkipped;
        }

        return new CopyPlanningResult(created, skipped);
    }

    /// <summary>Cada fatura do ano sem estimativa recebe a do mesmo mês do ano anterior; as que já têm são puladas.</summary>
    private async Task<(int Created, int Skipped)> CopyEstimatesAsync(
        PlanningData previous, PlanningData current, int year, CancellationToken cancellationToken)
    {
        var created = 0;
        var skipped = 0;
        foreach (var card in GetPlanningCopyCandidatesHandler.EstimateCards(previous))
        {
            foreach (var source in card.Months)
            {
                var month = YearMonth.Of(year, source.Month.Month);
                if (current.Invoices.Any(i => i.CreditCardId == card.CreditCardId && i.DueMonth == month && i.EstimateCents is not null))
                {
                    skipped++;
                    continue;
                }

                await estimate.HandleAsync(
                    new SetInvoiceEstimateCommand(card.CreditCardId, month.ToString(), Reais(source.EstimateCents)),
                    cancellationToken);
                created++;
            }
        }

        return (created, skipped);
    }

    private static string Reais(long cents) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{cents / 100},{cents % 100:00}");

    private async Task<HashSet<Guid>> ActiveTagsAsync(List<Guid> ids, CancellationToken cancellationToken) =>
        ids.Count == 0 ? [] : (await tags.FindManyAsync(ids, cancellationToken)).Where(t => !t.IsArchived).Select(t => t.Id).ToHashSet();

    /// <summary>A carteira principal, só buscada se alguma origem usa carteira arquivada (ou ausente do conjunto ativo).</summary>
    private static async Task<Guid?> PrimaryWalletAsync(
        PlanningData previous, ILookup<string, PlanningEntry> sources, List<string> keys, IWalletStore wallets, CancellationToken cancellationToken)
    {
        if (keys.SelectMany(k => sources[k]).All(e => previous.ActiveWalletIds.Contains(e.WalletId)))
        {
            return null;
        }

        return (await wallets.FindPrimaryAsync(cancellationToken))?.Id
            ?? throw new DomainException(EntryErrors.Field("wallet.required", "walletId", "Escolha a carteira"));
    }
}
