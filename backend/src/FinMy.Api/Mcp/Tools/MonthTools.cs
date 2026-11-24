using System.ComponentModel;
using FinMy.Application.Abstractions;
using FinMy.Application.Catalog;
using FinMy.Application.Entries;
using FinMy.Application.Months;
using FinMy.Application.Settlements;
using FinMy.Domain.Common;
using FinMy.Domain.Months;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FinMy.Api.Mcp.Tools;

/// <summary>Consultas do mês: resumo, pendências, acertos e busca (008 contracts). Os números vêm dos mesmos handlers do app (SC-002).</summary>
[McpServerToolType]
public sealed class MonthTools(
    CatalogResolver catalog,
    GetMonthHandler monthHandler,
    ListPendingEntriesHandler pending,
    GetSettlementsHandler settlements,
    GetSettlementItemsHandler settlementItems,
    SearchLedgerHandler search,
    IClock clock)
{
    private const int MaxSearchLimit = 50;

    [McpServerTool(Name = "get_month_summary", Title = "Resumo do mês", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Resumo do mês nas duas leituras: previsto (planned) e realizado (actual) — saldo inicial, entradas, saídas, resultado, transferências e saldo final — e as pendências. Valores em centavos e formatados.")]
    public Task<CallToolResult> GetMonthSummary(
        [Description("Mês no formato AAAA-MM. Padrão: o mês de hoje.")] string? month = null,
        [Description("Nomes das carteiras para restringir o resumo. Padrão: todas.")] string[]? wallets = null,
        CancellationToken cancellationToken = default) => McpResult.RunAsync(async () =>
    {
        var walletIds = await catalog.ResolveWalletsAsync(wallets, "wallets", cancellationToken);
        var result = await monthHandler.HandleAsync(
            McpArgs.Month(month, clock.Today), walletIds.Count == 0 ? null : string.Join(',', walletIds), null, cancellationToken);

        return new McpObject()
            .Set("today", result.Today)
            .Set("month", result.Month)
            .Set("planned", Column(result.Summary.Planned))
            .Set("actual", Column(result.Summary.Actual))
            .Set("pending", new McpObject().Set("count", result.Pending.Count).Money("toReceive", result.Pending.ToReceiveCents).Money("toPay", result.Pending.ToPayCents));
    });

    [McpServerTool(Name = "list_pending", Title = "Pendências", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Entradas e saídas previstas de meses anteriores ao atual, que ainda não foram marcadas como realizadas.")]
    public Task<CallToolResult> ListPending(CancellationToken cancellationToken) => McpResult.RunAsync(async () =>
    {
        var items = await pending.HandleAsync(cancellationToken);
        return new McpObject()
            .Set("today", clock.Today)
            .Set("count", items.Count)
            .Set("items", items.Select(e => new McpObject()
                .Set("id", e.Id)
                .Set("kind", Code(e.Kind))
                .Set("description", e.Description)
                .Set("date", e.Date)
                .Set("referenceMonth", e.ReferenceMonth.ToString())
                .Money("amount", e.AmountCents)).ToList());
    });

    [McpServerTool(Name = "get_settlements", Title = "Acertos", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Acertos por tag de acerto no mês: gasto, cobrado, recebido, a cobrar, a receber e acumulado. Com `tag`, traz também os lançamentos que compõem os números.")]
    public Task<CallToolResult> GetSettlements(
        [Description("Mês no formato AAAA-MM. Padrão: o mês de hoje.")] string? month = null,
        [Description("Nome de uma tag de acerto. Padrão: todas.")] string? tag = null,
        CancellationToken cancellationToken = default) => McpResult.RunAsync(async () =>
    {
        var monthText = McpArgs.Month(month, clock.Today);
        var rows = await settlements.HandleAsync(monthText, cancellationToken);

        var result = new McpObject().Set("today", clock.Today).Set("month", monthText);
        if (McpArgs.NullIfBlank(tag) is null)
        {
            return result.Set("settlements", rows.Select(r => Row(r)).ToList());
        }

        var tagId = await catalog.ResolveTagAsync(tag, "tag", settlementOnly: true, cancellationToken);
        var items = await settlementItems.HandleAsync(monthText, tagId, cancellationToken);
        var names = await catalog.LoadNamesAsync(cancellationToken);
        return result
            .Set("settlements", rows.Where(r => r.TagId == tagId).Select(r => Row(r)).ToList())
            .Set("items", items.Select(i => new McpObject()
                .Set("group", i.Group == SettlementGroup.Spent ? "spent" : "charged")
                .Set("type", i.Type)
                .Set("id", i.Id)
                .Set("description", i.Description)
                .Set("date", i.Date)
                .Money("signed", i.SignedCents)
                .Set("received", i.Received)
                .Set("creditCard", i.CreditCardId is { } cardId ? names.CreditCard(cardId) : null)).ToList());
    });

    [McpServerTool(Name = "search_entries", Title = "Buscar lançamentos", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Busca entradas, saídas, transferências e compras no cartão de qualquer mês, do mais recente ao mais antigo. Devolve os itens (até 50), o total e a contagem de tudo que casou.")]
    public Task<CallToolResult> SearchEntries(
        [Description("Trecho da descrição (sem acento e sem diferenciar maiúsculas).")] string? text = null,
        [Description("Tipos: income, expense, transfer, card_purchase. Padrão: todos.")] string[]? kinds = null,
        [Description("Nomes das carteiras. Padrão: todas.")] string[]? wallets = null,
        [Description("Nomes das tags (mostra os itens que têm qualquer uma). Padrão: sem filtro.")] string[]? tags = null,
        [Description("Data inicial AAAA-MM-DD.")] string? from = null,
        [Description("Data final AAAA-MM-DD.")] string? to = null,
        [Description("Quantidade máxima de itens, de 1 a 50. Padrão: 50.")] int? limit = null,
        CancellationToken cancellationToken = default) => McpResult.RunAsync(async () =>
    {
        if (limit is < 1 or > MaxSearchLimit)
        {
            throw new DomainException(EntryErrors.Field("limit.invalid", "limit", $"Use um limite entre 1 e {MaxSearchLimit}"));
        }

        var walletIds = await catalog.ResolveWalletsAsync(wallets, "wallets", cancellationToken);
        var tagIds = await catalog.ResolveTagsAsync(tags, "tags", cancellationToken);
        var result = await search.HandleAsync(
            new SearchLedgerCommand(
                McpArgs.NullIfBlank(text),
                kinds is { Length: > 0 } ? string.Join(',', kinds) : null,
                walletIds.Count == 0 ? null : string.Join(',', walletIds),
                null,
                McpArgs.Date(from, "from"),
                McpArgs.Date(to, "to"),
                null,
                limit ?? MaxSearchLimit,
                tagIds.Count == 0 ? null : string.Join(',', tagIds)),
            cancellationToken);

        var names = await catalog.LoadNamesAsync(cancellationToken);
        return new McpObject()
            .Set("today", clock.Today)
            .Set("items", result.Items.Select(i => Item(i, names)).ToList())
            .Money("total", result.TotalCents)
            .Set("totalCount", result.TotalCount)
            .Set("hasMore", result.NextCursor is not null);
    });

    private static McpObject Column(SummaryColumn c) => new McpObject()
        .Money("openingBalance", c.OpeningBalanceCents)
        .Money("walletsInitialBalance", c.WalletsInitialBalanceCents)
        .Money("income", c.IncomeCents)
        .Money("expense", c.ExpenseCents)
        .Money("result", c.ResultCents)
        .Money("transfersIn", c.TransfersInCents)
        .Money("transfersOut", c.TransfersOutCents)
        .Money("finalBalance", c.FinalBalanceCents);

    private static McpObject Row(SettlementRow r) => new McpObject()
        .Set("tag", r.TagName)
        .Set("archived", r.Archived)
        .Money("spent", r.Figures.SpentCents)
        .Money("charged", r.Figures.ChargedCents)
        .Money("received", r.Figures.ReceivedCents)
        .Money("toCharge", r.Figures.ToChargeCents)
        .Money("toReceive", r.Figures.ToReceiveCents)
        .Money("accumulated", r.Figures.AccumulatedToReceiveCents);

    private static McpObject Item(LedgerItemView item, CatalogNames names)
    {
        var tagNames = (IEnumerable<Guid> ids, Guid? settlement) =>
            (settlement is { } s ? ids.Append(s) : ids).Distinct().Select(names.Tag).ToList();

        switch (item.Type)
        {
            case LedgerItemKind.Transfer:
                var t = item.Transfer!;
                return new McpObject()
                    .Set("type", "transfer").Set("id", t.Id).Set("description", t.Description).Set("date", t.Date)
                    .Set("fromWallet", names.Wallet(t.FromWalletId)).Set("toWallet", names.Wallet(t.ToWalletId))
                    .Money("amount", t.AmountCents);
            case LedgerItemKind.CardPurchase:
                var p = item.Purchase!;
                return new McpObject()
                    .Set("type", "card_purchase").Set("id", p.Id).Set("description", p.DisplayDescription).Set("date", p.Date)
                    .Set("creditCard", names.CreditCard(p.CreditCardId)).Set("dueMonth", p.DueMonth.ToString())
                    .Set("nature", p.Nature.ToString().ToLowerInvariant())
                    .Set("installment", p.InstallmentNumber is { } n ? $"{n}/{p.InstallmentTotal}" : null)
                    .Set("tags", tagNames(p.TagIds, p.SettlementTagId))
                    .Money("amount", p.AmountCents);
            default:
                var e = item.Entry!;
                return new McpObject()
                    .Set("type", Code(e.Kind)).Set("id", e.Id).Set("description", e.Description).Set("date", e.Date)
                    .Set("referenceMonth", e.ReferenceMonth.ToString()).Set("status", e.Status.ToString().ToLowerInvariant())
                    .Set("wallet", names.Wallet(e.WalletId))
                    .Set("tags", tagNames(e.TagIds, e.SettlementTagId))
                    .Money("amount", e.AmountCents);
        }
    }

    private static string Code(FinMy.Domain.Entries.EntryKind kind) => kind.ToString().ToLowerInvariant();
}
