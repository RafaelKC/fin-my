using System.ComponentModel;
using FinMy.Application.Abstractions;
using FinMy.Application.Cards;
using FinMy.Application.Catalog;
using FinMy.Domain.Cards;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FinMy.Api.Mcp.Tools;

/// <summary>Faturas dos cartões (008 contracts: <c>list_invoices</c>, <c>get_invoice</c>). Mesmos handlers da 003.</summary>
[McpServerToolType]
public sealed class InvoiceTools(
    CatalogResolver catalog, ListInvoicesHandler listInvoices, GetInvoiceHandler getInvoice, GetCreditCardHandler getCard, IClock clock)
{
    [McpServerTool(Name = "list_invoices", Title = "Faturas do cartão", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Faturas de um cartão por mês de vencimento: fechamento, vencimento, situação, total, pago e restante. Padrão: 3 meses antes e 3 depois da fatura atual (no máximo 24 meses).")]
    public Task<CallToolResult> ListInvoices(
        [Description("Nome do cartão.")] string creditCard,
        [Description("Primeiro mês de vencimento, AAAA-MM.")] string? from = null,
        [Description("Último mês de vencimento, AAAA-MM.")] string? to = null,
        CancellationToken cancellationToken = default) => McpResult.RunAsync(async () =>
    {
        var cardId = await catalog.ResolveCreditCardAsync(creditCard, "creditCard", cancellationToken);
        var items = await listInvoices.HandleAsync(cardId, McpArgs.NullIfBlank(from), McpArgs.NullIfBlank(to), cancellationToken);
        return new McpObject()
            .Set("today", clock.Today)
            .Set("creditCard", (await catalog.LoadNamesAsync(cancellationToken)).CreditCard(cardId))
            .Set("invoices", items.Select(Summary).ToList());
    });

    [McpServerTool(Name = "get_invoice", Title = "Fatura do cartão", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Uma fatura: situação, total, pago, restante, pagamentos, subtotais por tag de acerto e, se pedido, as compras. Padrão: a fatura atual.")]
    public Task<CallToolResult> GetInvoice(
        [Description("Nome do cartão.")] string creditCard,
        [Description("Mês de vencimento da fatura, AAAA-MM. Padrão: a fatura atual.")] string? month = null,
        [Description("Incluir as compras da fatura. Padrão: não.")] bool? includePurchases = null,
        CancellationToken cancellationToken = default) => McpResult.RunAsync(async () =>
    {
        var cardId = await catalog.ResolveCreditCardAsync(creditCard, "creditCard", cancellationToken);
        var monthText = McpArgs.NullIfBlank(month)
            ?? (await getCard.HandleAsync(cardId, cancellationToken)).CurrentInvoice.DueMonth.ToString();
        var invoice = await getInvoice.HandleAsync(cardId, monthText, null, cancellationToken);
        var names = await catalog.LoadNamesAsync(cancellationToken);

        var result = Summary(invoice.Summary)
            .Set("today", clock.Today)
            .Set("creditCard", names.CreditCard(cardId))
            .Money("reconciled", invoice.ReconciledCents)
            .Set("previousMonth", invoice.PreviousMonth.ToString())
            .Set("nextMonth", invoice.NextMonth.ToString())
            .Set("payments", invoice.Payments.Select(p => new McpObject()
                .Set("id", p.Id).Set("date", p.Date).Set("wallet", names.Wallet(p.WalletId)).Money("amount", p.AmountCents)).ToList())
            .Set("settlementSubtotals", invoice.SettlementSubtotals.Select(s => new McpObject()
                .Set("tag", s.TagId is { } id ? names.Tag(id) : null).Money("amount", s.Cents)).ToList());

        if (includePurchases == true)
        {
            result.Set("purchases", invoice.Purchases.Select(p => new McpObject()
                .Set("id", p.Id)
                .Set("description", p.DisplayDescription)
                .Set("date", p.Date)
                .Set("nature", p.Nature == PurchaseNature.Credit ? "credit" : "debit")
                .Set("installment", p.InstallmentNumber is { } n ? $"{n}/{p.InstallmentTotal}" : null)
                .Set("tags", (p.SettlementTagId is { } s ? p.TagIds.Append(s) : p.TagIds).Distinct().Select(names.Tag).ToList())
                .Money("amount", p.AmountCents)).ToList());
        }

        return result;
    });

    private static McpObject Summary(InvoiceSummaryView i) => new McpObject()
        .Set("dueMonth", i.DueMonth.ToString())
        .Set("closingDate", i.ClosingDate)
        .Set("dueDate", i.DueDate)
        .Set("status", i.Status.ToCode())
        .Money("total", i.TotalCents)
        .Money("paid", i.PaidCents)
        .Money("remaining", i.RemainingCents);
}
