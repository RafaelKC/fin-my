using System.ComponentModel;
using FinMy.Application.Abstractions;
using FinMy.Application.Catalog;
using FinMy.Application.Wallets;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FinMy.Api.Mcp.Tools;

/// <summary>Catálogo do Espaço e saldos das carteiras (008 contracts: <c>list_catalog</c>, <c>get_wallet_balances</c>).</summary>
[McpServerToolType]
public sealed class CatalogTools(CatalogResolver catalog, ListWalletsHandler wallets, IClock clock)
{
    [McpServerTool(Name = "list_catalog", Title = "Catálogo", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Lista as carteiras, os cartões de crédito e as tags ativos do Espaço, e a data de hoje. Use antes de lançar ou consultar por nome.")]
    public Task<CallToolResult> ListCatalog(CancellationToken cancellationToken) => McpResult.RunAsync(async () =>
    {
        var view = await catalog.GetAsync(cancellationToken);
        return new McpObject()
            .Set("today", view.Today)
            .Set("wallets", view.Wallets.Select(w => new { w.Name, w.IsPrimary }))
            .Set("creditCards", view.CreditCards.Select(c => new { c.Name, c.ClosingDay, c.DueDay }))
            .Set("tags", view.Tags.Select(t => new { t.Name, t.IsSettlement }));
    });

    [McpServerTool(Name = "get_wallet_balances", Title = "Saldos das carteiras", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Saldo atual de cada carteira ativa e o total, em centavos e formatado (R$).")]
    public Task<CallToolResult> GetWalletBalances(CancellationToken cancellationToken) => McpResult.RunAsync(async () =>
    {
        var items = await wallets.HandleAsync(null, cancellationToken);
        return new McpObject()
            .Set("today", clock.Today)
            .Set("wallets", items.Select(w => new McpObject().Set("name", w.Name).Money("currentBalance", w.CurrentBalanceCents)).ToList())
            .Money("total", items.Sum(w => w.CurrentBalanceCents));
    });
}
