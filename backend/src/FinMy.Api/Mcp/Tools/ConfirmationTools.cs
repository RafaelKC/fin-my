using System.ComponentModel;
using FinMy.Application.Abstractions;
using FinMy.Application.Cards;
using FinMy.Application.Catalog;
using FinMy.Application.Entries;
using FinMy.Application.Settlements;
using FinMy.Application.Transfers;
using FinMy.Domain.Cards;
using FinMy.Domain.Common;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FinMy.Api.Mcp.Tools;

/// <summary>
/// Ferramentas que exigem confirmação (008 contracts, US4, FR-007, SC-003): <c>set_entry_status</c>, <c>pay_invoice</c>,
/// <c>generate_settlement_charge</c> e <c>delete_entry</c>. Sem <c>confirmed: true</c> nada é alterado: a ferramenta devolve
/// <c>preview</c> com exatamente o que faria (valores validados pelas mesmas regras). Com <c>confirmed: true</c> chama os handlers do app.
/// </summary>
[McpServerToolType]
public sealed class ConfirmationTools(
    CatalogResolver catalog,
    CreationDefaults defaults,
    GetEntryHandler getEntry,
    ChangeEntryStatusHandler changeStatus,
    DeleteEntryHandler deleteEntry,
    GetCardPurchaseHandler getPurchase,
    DeleteCardPurchaseHandler deletePurchase,
    GetTransferHandler getTransfer,
    DeleteTransferHandler deleteTransfer,
    ICardPurchaseStore purchases,
    GetCreditCardHandler getCard,
    GetInvoiceHandler getInvoice,
    RegisterInvoicePaymentHandler payInvoice,
    GetSettlementsHandler settlements,
    CreateEntryHandler createEntry,
    IClock clock)
{
    private const string NothingToCharge = "settlement.nothing_to_charge";

    // ---- set_entry_status ----

    [McpServerTool(Name = "set_entry_status", Title = "Marcar como recebida/paga", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Marca uma Entrada ou Saída como recebida/paga (`settled`) ou de volta como prevista (`planned`). EXIGE confirmação: sem `confirmed: true` nada é alterado e devolve `preview`; mostre-o à pessoa e só chame de novo com `confirmed: true` depois de ela dizer sim.")]
    public Task<CallToolResult> SetEntryStatus(
        [Description("Id do lançamento (de list_pending, search_entries ou get_month_summary).")] string id,
        [Description("`planned` (prevista) ou `settled` (recebida/paga).")] string status,
        [Description("`true` só depois de a pessoa confirmar o preview.")] bool? confirmed = null,
        CancellationToken cancellationToken = default) => McpResult.RunAsync(async () =>
    {
        var entryId = ParseId(id);
        var target = EntryInput.ParseStatus(status);
        var entry = await getEntry.HandleAsync(entryId, cancellationToken);
        var names = await catalog.LoadNamesAsync(cancellationToken);

        if (confirmed != true)
        {
            return Preview("set_entry_status", new McpObject()
                .Set("entry", EntrySummary(entry, names))
                .Set("fromStatus", EntryInput.ToText(entry.Status))
                .Set("toStatus", EntryInput.ToText(target)));
        }

        var updated = await changeStatus.HandleAsync(entryId, status, cancellationToken);
        return new McpObject().Set("today", clock.Today).Set("confirmed", true).Set("entry", EntrySummary(updated, names));
    });

    // ---- pay_invoice ----

    [McpServerTool(Name = "pay_invoice", Title = "Pagar fatura", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Registra o pagamento de uma fatura de cartão. Padrões: fatura = a vencida/fechada mais antiga em aberto, senão a atual; valor = o restante; carteira = a de pagamento do cartão; data = hoje. EXIGE confirmação: sem `confirmed: true` nada é registrado e devolve `preview`; mostre-o à pessoa e só chame de novo com `confirmed: true` depois de ela dizer sim.")]
    public Task<CallToolResult> PayInvoice(
        [Description("Nome do cartão.")] string creditCard,
        [Description("Mês de vencimento da fatura, AAAA-MM. Padrão: a vencida/fechada mais antiga em aberto, senão a atual.")] string? month = null,
        [Description("Valor em reais como texto, ex.: \"200,00\". Padrão: o restante da fatura.")] string? amount = null,
        [Description("Nome da carteira que paga. Padrão: a carteira de pagamento do cartão.")] string? wallet = null,
        [Description("Data do pagamento AAAA-MM-DD. Padrão: hoje.")] string? date = null,
        [Description("`true` só depois de a pessoa confirmar o preview.")] bool? confirmed = null,
        CancellationToken cancellationToken = default) => McpResult.RunAsync(async () =>
    {
        var cardId = await catalog.ResolveCreditCardAsync(creditCard, "creditCard", cancellationToken);
        var overview = await getCard.HandleAsync(cardId, cancellationToken);
        var monthText = McpArgs.NullIfBlank(month)
            ?? (overview.OpenPastInvoices.OrderBy(i => i.DueMonth).FirstOrDefault() ?? overview.CurrentInvoice).DueMonth.ToString();
        var walletId = McpArgs.NullIfBlank(wallet) is { } walletName
            ? await catalog.ResolveWalletAsync(walletName, "wallet", cancellationToken)
            : overview.Card.PaymentWalletId;
        var day = McpArgs.Date(date, "date") ?? clock.Today;
        var requested = McpArgs.NullIfBlank(amount) is { } text ? EntryInput.ParseAmount(text) : (Money?)null;

        var invoice = (await getInvoice.HandleAsync(cardId, monthText, null, cancellationToken)).Summary;
        var remaining = invoice.RemainingCents;
        if (remaining <= 0)
        {
            throw new DomainException(InvoiceErrors.NothingToPay);
        }

        if (requested is { } r && (r.Cents <= 0 || r.Cents > remaining))
        {
            throw new DomainException(InvoiceErrors.PaymentExceedsRemaining(remaining));
        }

        var paying = requested?.Cents ?? remaining;
        var names = await catalog.LoadNamesAsync(cancellationToken);
        var payment = new McpObject()
            .Set("creditCard", names.CreditCard(cardId))
            .Set("dueMonth", invoice.DueMonth.ToString())
            .Set("dueDate", invoice.DueDate)
            .Set("wallet", names.Wallet(walletId))
            .Set("date", day)
            .Money("amount", paying)
            .Money("remainingBefore", remaining)
            .Money("remainingAfter", remaining - paying);

        if (confirmed != true)
        {
            return Preview("pay_invoice", payment);
        }

        var updated = (await payInvoice.HandleAsync(
            new RegisterInvoicePaymentCommand(cardId, monthText, requested is null ? null : amount, day, walletId), cancellationToken)).Summary;
        return new McpObject()
            .Set("today", clock.Today)
            .Set("confirmed", true)
            .Set("payment", payment)
            .Set("invoice", new McpObject()
                .Set("dueMonth", updated.DueMonth.ToString())
                .Set("status", updated.Status.ToCode())
                .Money("total", updated.TotalCents)
                .Money("paid", updated.PaidCents)
                .Money("remaining", updated.RemainingCents));
    });

    // ---- generate_settlement_charge ----

    [McpServerTool(Name = "generate_settlement_charge", Title = "Gerar cobrança de acerto", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Cria a Entrada prevista de cobrança de uma tag de acerto (descrição \"<Tag> — acerto MM/AAAA\", tag de acerto, carteira principal). Padrões: mês = o atual; valor = o A cobrar do mês; data = hoje se for o mês atual, senão o dia 1 do mês. EXIGE confirmação: sem `confirmed: true` nada é criado e devolve `preview`; mostre-o à pessoa e só chame de novo com `confirmed: true` depois de ela dizer sim.")]
    public Task<CallToolResult> GenerateSettlementCharge(
        [Description("Nome da tag de acerto.")] string tag,
        [Description("Mês de referência AAAA-MM. Padrão: o mês de hoje.")] string? month = null,
        [Description("Valor em reais como texto, ex.: \"150,00\". Padrão: o A cobrar do mês.")] string? amount = null,
        [Description("Data AAAA-MM-DD. Padrão: hoje se o mês é o atual, senão o dia 1 do mês.")] string? date = null,
        [Description("Nome da carteira. Padrão: a principal.")] string? wallet = null,
        [Description("`true` só depois de a pessoa confirmar o preview.")] bool? confirmed = null,
        CancellationToken cancellationToken = default) => McpResult.RunAsync(async () =>
    {
        var tagId = await catalog.ResolveTagAsync(tag, "tag", settlementOnly: true, cancellationToken);
        var monthText = McpArgs.Month(month, clock.Today);
        var referenceMonth = EntryInput.ParseReferenceMonth(monthText);
        var row = (await settlements.HandleAsync(monthText, cancellationToken)).FirstOrDefault(r => r.TagId == tagId);
        var toCharge = row?.Figures.ToChargeCents ?? 0;

        var cents = McpArgs.NullIfBlank(amount) is { } text ? EntryInput.ParseAmount(text).Cents : toCharge;
        if (cents <= 0)
        {
            throw new DomainException(new DomainError(
                NothingToCharge,
                "Não há valor a cobrar dessa tag neste mês. Informe um valor se quiser cobrar mesmo assim.",
                DomainErrorKind.Validation,
                new Dictionary<string, string[]> { ["amount"] = ["Não há valor a cobrar."] }));
        }

        var day = McpArgs.Date(date, "date") ?? (referenceMonth.Contains(clock.Today) ? clock.Today : referenceMonth.FirstDay);
        var walletId = McpArgs.NullIfBlank(wallet) is { } walletName
            ? await catalog.ResolveWalletAsync(walletName, "wallet", cancellationToken)
            : (await defaults.WalletAsync(EntryKind.Income, null, cancellationToken)).Id;
        var names = await catalog.LoadNamesAsync(cancellationToken);
        var description = $"{names.Tag(tagId)} — acerto {referenceMonth.Month:00}/{referenceMonth.Year}";

        var charge = new McpObject()
            .Set("description", description)
            .Set("kind", "income")
            .Set("status", "planned")
            .Set("tag", names.Tag(tagId))
            .Set("wallet", names.Wallet(walletId))
            .Set("referenceMonth", referenceMonth.ToString())
            .Set("date", day)
            .Money("amount", cents)
            .Money("toCharge", toCharge);

        if (confirmed != true)
        {
            return Preview("generate_settlement_charge", charge);
        }

        var created = await createEntry.HandleAsync(
            new CreateEntryCommand(
                "income", description, $"{cents / 100},{cents % 100:00}", day, walletId, "planned", referenceMonth.ToString(), null, [tagId]),
            cancellationToken);
        return new McpObject().Set("today", clock.Today).Set("confirmed", true).Set("entry", EntrySummary(created, names));
    });

    // ---- delete_entry ----

    [McpServerTool(Name = "delete_entry", Title = "Excluir lançamento", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Exclui uma Entrada/Saída (`entry`), uma compra no cartão (`card_purchase`, com `scope` para parcelas: `this`, `this_and_following` ou `all`) ou uma transferência (`transfer`). A pessoa pode restaurar pelo app. EXIGE confirmação: sem `confirmed: true` nada é excluído e devolve `preview` com o que seria excluído; mostre-o e só chame de novo com `confirmed: true` depois de a pessoa dizer sim.")]
    public Task<CallToolResult> DeleteEntry(
        [Description("Id do lançamento, compra ou transferência.")] string id,
        [Description("`entry` (Entrada/Saída), `card_purchase` (compra no cartão) ou `transfer` (transferência).")] string type,
        [Description("Só `card_purchase` parcelada: `this` (padrão), `this_and_following` ou `all`.")] string? scope = null,
        [Description("`true` só depois de a pessoa confirmar o preview.")] bool? confirmed = null,
        CancellationToken cancellationToken = default) => McpResult.RunAsync(async () =>
    {
        var itemId = ParseId(id);
        var names = await catalog.LoadNamesAsync(cancellationToken);
        var execute = confirmed == true;
        McpObject target;
        int count;
        Guid batchId = Guid.Empty;

        switch (McpArgs.NullIfBlank(type)?.ToLowerInvariant())
        {
            case "entry":
                var entry = await getEntry.HandleAsync(itemId, cancellationToken);
                target = EntrySummary(entry, names);
                count = 1;
                if (execute)
                {
                    batchId = await deleteEntry.HandleAsync(itemId, cancellationToken);
                }

                break;

            case "card_purchase":
                var view = await getPurchase.HandleAsync(itemId, cancellationToken);
                var parsedScope = ParseScope(scope);
                count = await CountPurchasesAsync(itemId, parsedScope, cancellationToken);
                target = new McpObject()
                    .Set("id", view.Id)
                    .Set("type", "card_purchase")
                    .Set("description", view.DisplayDescription)
                    .Set("creditCard", names.CreditCard(view.CreditCardId))
                    .Set("date", view.Date)
                    .Set("dueMonth", view.DueMonth.ToString())
                    .Set("installment", view.InstallmentNumber is { } n ? $"{n}/{view.InstallmentTotal}" : null)
                    .Set("scope", parsedScope)
                    .Money("amount", view.Nature == PurchaseNature.Credit ? -view.AmountCents : view.AmountCents);
                if (execute)
                {
                    batchId = await deletePurchase.HandleAsync(itemId, parsedScope, cancellationToken);
                }

                break;

            case "transfer":
                var transfer = await getTransfer.HandleAsync(itemId, cancellationToken);
                count = 1;
                target = new McpObject()
                    .Set("id", transfer.Id)
                    .Set("type", "transfer")
                    .Set("description", transfer.Description)
                    .Set("fromWallet", names.Wallet(transfer.FromWalletId))
                    .Set("toWallet", names.Wallet(transfer.ToWalletId))
                    .Set("date", transfer.Date)
                    .Money("amount", transfer.AmountCents);
                if (execute)
                {
                    batchId = await deleteTransfer.HandleAsync(itemId, cancellationToken);
                }

                break;

            default:
                throw new DomainException(EntryErrors.Field("type.invalid", "type", "Use `entry`, `card_purchase` ou `transfer`."));
        }

        if (!execute)
        {
            return Preview("delete_entry", target.Set("willDelete", count));
        }

        return new McpObject().Set("today", clock.Today).Set("confirmed", true).Set("deleted", count).Set("deletionId", batchId);
    });

    private static Guid ParseId(string id) =>
        Guid.TryParse(id, out var parsed)
            ? parsed
            : throw new DomainException(EntryErrors.Field("id.invalid", "id", "Informe o id do lançamento."));

    private static string ParseScope(string? scope)
    {
        var value = McpArgs.NullIfBlank(scope)?.ToLowerInvariant() ?? "this";
        return value is "this" or "this_and_following" or "all"
            ? value
            : throw new DomainException(CardPurchaseErrors.InvalidScope);
    }

    /// <summary>Quantas compras a exclusão levaria: as parcelas do escopo, o par de "saldo levado" ou só a compra.</summary>
    private async Task<int> CountPurchasesAsync(Guid id, string scope, CancellationToken cancellationToken)
    {
        var purchase = await purchases.FindAsync(id, cancellationToken) ?? throw new DomainException(CardPurchaseErrors.NotFound);
        if (purchase.CarryOverPairId is { } pairId)
        {
            return (await purchases.ListCarryOverPairAsync(pairId, cancellationToken)).Count;
        }

        if (purchase.InstallmentPlanId is { } planId)
        {
            var parcels = await purchases.ListPlanAsync(planId, cancellationToken);
            return scope switch
            {
                "all" => parcels.Count,
                "this_and_following" => parcels.Count(p => p.InstallmentNumber >= purchase.InstallmentNumber),
                _ => 1,
            };
        }

        return 1;
    }

    private static McpObject Preview(string action, McpObject details) => new McpObject()
        .Set("confirmed", false)
        .Set("requiresConfirmation", true)
        .Set("message", "Nada foi alterado. Mostre o preview à pessoa e, só se ela confirmar, chame a ferramenta de novo com os mesmos argumentos e `confirmed: true`.")
        .Set("action", action)
        .Set("preview", details);

    private static McpObject EntrySummary(EntryView e, CatalogNames names) => new McpObject()
        .Set("id", e.Id)
        .Set("type", "entry")
        .Set("kind", EntryInput.ToText(e.Kind))
        .Set("description", e.Description)
        .Set("date", e.Date)
        .Set("referenceMonth", e.ReferenceMonth.ToString())
        .Set("status", EntryInput.ToText(e.Status))
        .Set("wallet", names.Wallet(e.WalletId))
        .Set("tags", e.TagIds.Select(names.Tag).ToList())
        .Set("settlementTag", e.SettlementTagId is { } s ? names.Tag(s) : null)
        .Money("amount", e.AmountCents);
}
