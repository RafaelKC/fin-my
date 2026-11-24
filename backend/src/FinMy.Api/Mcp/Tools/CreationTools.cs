using System.ComponentModel;
using FinMy.Application.Abstractions;
using FinMy.Application.Cards;
using FinMy.Application.Catalog;
using FinMy.Application.Entries;
using FinMy.Application.Splits;
using FinMy.Application.Transfers;
using FinMy.Application.Undo;
using FinMy.Domain.Cards;
using FinMy.Domain.Common;
using FinMy.Domain.Monetary;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FinMy.Api.Mcp.Tools;

/// <summary>Divisão de uma Saída ou compra entre pessoas (004): partes por tag de acerto ou "Eu".</summary>
public sealed class McpSplit
{
    [Description("`equal` (partes iguais, o resto dos centavos vai para a primeira) ou `amounts` (cada parte com o seu valor, somando o total).")]
    public string? Mode { get; set; }

    [Description("As partes. Cada uma tem `tag` (nome de uma tag de acerto; nulo ou ausente = \"Eu\", sem tag de acerto) e, em `amounts`, o `amount`.")]
    public List<McpSplitPart>? Parts { get; set; }
}

public sealed class McpSplitPart
{
    [Description("Nome da tag de acerto da parte; nulo = \"Eu\".")]
    public string? Tag { get; set; }

    [Description("Valor da parte, ex.: \"50,00\". Só em `mode: amounts`.")]
    public string? Amount { get; set; }
}

/// <summary>Parcelamento de uma compra no cartão (003).</summary>
public sealed class McpInstallments
{
    [Description("Número de parcelas, de 1 a 48.")]
    public int? Total { get; set; }

    [Description("`total_amount` (o `amount` é o total; padrão), `installment_amount` (o `amount` é o valor de cada parcela) ou `in_progress` (parcelamento já em andamento).")]
    public string? Mode { get; set; }

    [Description("Só `in_progress`: número da próxima parcela a vencer.")]
    public int? NextNumber { get; set; }

    [Description("Só `in_progress`: mês de vencimento da fatura dessa parcela, AAAA-MM. Padrão: a fatura atual.")]
    public string? FirstInvoiceMonth { get; set; }
}

/// <summary>
/// Ferramentas de lançamento (008 contracts, US2): <c>create_entry</c>, <c>create_card_purchase</c>, <c>create_transfer</c> e
/// <c>undo_creation</c>. Chamam os mesmos handlers do app; o canal Claude fica gravado em <c>created_via</c> pelo contexto.
/// Nomes por <see cref="CatalogResolver"/>, padrões da FR-005 por <see cref="CreationDefaults"/>, <c>requestId</c> por <see cref="McpIdempotency"/>.
/// </summary>
[McpServerToolType]
public sealed class CreationTools(
    CatalogResolver catalog,
    CreationDefaults defaults,
    CreateEntryHandler createEntry,
    SplitEntryHandler splitEntry,
    CreateCardPurchaseHandler createPurchase,
    SplitCardPurchaseHandler splitPurchase,
    CreateTransferHandler createTransfer,
    UndoCreationHandler undo,
    ICreditCardStore cards,
    McpIdempotency idempotency,
    IClock clock)
{
    [McpServerTool(Name = "create_entry", Title = "Lançar entrada ou saída", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Cria uma Entrada (income) ou Saída (expense) numa carteira. O que não for informado usa o padrão: data = hoje, situação = regra do app, carteira = a do lançamento mais recente com a mesma descrição, senão a principal. A resposta traz todos os valores usados. Tags inexistentes são criadas como tags comuns. Com `split`, cria uma Saída por parte.")]
    public Task<CallToolResult> CreateEntry(
        [Description("`income` (entrada) ou `expense` (saída).")] string kind,
        [Description("Descrição do lançamento.")] string description,
        [Description("Valor em reais como texto, ex.: \"32,90\" ou \"29+73,2\". Sempre positivo.")] string amount,
        [Description("Data AAAA-MM-DD. Padrão: hoje.")] string? date = null,
        [Description("Nome da carteira. Padrão: a do lançamento mais recente com a mesma descrição, senão a principal.")] string? wallet = null,
        [Description("`planned` (prevista) ou `settled` (recebida/paga). Padrão: a regra do app pela data.")] string? status = null,
        [Description("Mês de referência AAAA-MM. Padrão: o mês da data.")] string? referenceMonth = null,
        [Description("Nomes das tags. As que não existem são criadas (comuns).")] string[]? tags = null,
        [Description("Dividir a saída entre pessoas (só `expense`).")] McpSplit? split = null,
        [Description("Observação.")] string? notes = null,
        [Description("UUID opcional; repetir o mesmo requestId com os mesmos dados não cria de novo.")] string? requestId = null,
        CancellationToken cancellationToken = default) =>
        idempotency.RunAsync(requestId, "create_entry", new { kind, description, amount, date, wallet, status, referenceMonth, tags, split, notes }, async () =>
        {
            var parsedKind = EntryInput.ParseKind(kind);
            EntryInput.ParseAmount(amount);
            var used = new List<object>();

            var day = McpArgs.Date(date, "date");
            if (day is null)
            {
                used.Add(Used("date", "today"));
            }

            Guid walletId;
            if (McpArgs.NullIfBlank(wallet) is { } walletName)
            {
                walletId = await catalog.ResolveWalletAsync(walletName, "wallet", cancellationToken);
            }
            else
            {
                var choice = await defaults.WalletAsync(parsedKind, description, cancellationToken);
                walletId = choice.Id;
                used.Add(Used("wallet", choice.Source));
            }

            if (McpArgs.NullIfBlank(status) is null)
            {
                used.Add(Used("status", "default_rule"));
            }

            var splitCommand = split is null ? null : await ToSplitAsync(split, cancellationToken);
            var tagIds = await catalog.ResolveOrCreateTagsAsync(tags, "tags", cancellationToken);
            var command = new CreateEntryCommand(
                kind, description, amount, day ?? clock.Today, walletId, McpArgs.NullIfBlank(status), McpArgs.NullIfBlank(referenceMonth), McpArgs.NullIfBlank(notes), tagIds);

            IReadOnlyList<EntryView> created = splitCommand is null
                ? [await createEntry.HandleAsync(command, cancellationToken)]
                : await splitEntry.HandleAsync(command, splitCommand, cancellationToken);

            var names = await catalog.LoadNamesAsync(cancellationToken);
            return new McpObject()
                .Set("today", clock.Today)
                .Set("defaultsUsed", used)
                .Set("entries", created.Select(e => new McpObject()
                    .Set("id", e.Id)
                    .Set("kind", EntryInput.ToText(e.Kind))
                    .Set("description", e.Description)
                    .Set("date", e.Date)
                    .Set("referenceMonth", e.ReferenceMonth.ToString())
                    .Set("status", EntryInput.ToText(e.Status))
                    .Set("wallet", names.Wallet(e.WalletId))
                    .Set("tags", e.TagIds.Select(names.Tag).ToList())
                    .Set("settlementTag", e.SettlementTagId is { } s ? names.Tag(s) : null)
                    .Set("notes", e.Notes)
                    .Set("splitGroupId", e.SplitGroupId)
                    .Money("amount", e.AmountCents)).ToList());
        }, cancellationToken);

    [McpServerTool(Name = "create_card_purchase", Title = "Lançar compra no cartão", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Cria uma compra no cartão de crédito, à vista ou parcelada, e diz em qual fatura (mês e vencimento) cada parcela caiu. Valor negativo é estorno (crédito). Padrões: data = hoje; cartão = o da compra mais recente com a mesma descrição, senão o com mais compras nos últimos 30 dias (se nenhum, devolve `card_required` com as opções: pergunte à pessoa). Compra que cairia em fatura já paga exige `confirmPaidInvoice: true` depois de a pessoa confirmar.")]
    public Task<CallToolResult> CreateCardPurchase(
        [Description("Descrição da compra.")] string description,
        [Description("Valor em reais como texto, ex.: \"293,70\". Negativo = estorno. Com parcelamento, é o total ou o valor da parcela conforme `installments.mode`.")] string amount,
        [Description("Nome do cartão. Padrão: o da compra mais recente com a mesma descrição, senão o mais usado nos últimos 30 dias.")] string? creditCard = null,
        [Description("Data da compra AAAA-MM-DD. Padrão: hoje.")] string? date = null,
        [Description("Parcelamento. Ausente = à vista.")] McpInstallments? installments = null,
        [Description("Nomes das tags. As que não existem são criadas (comuns).")] string[]? tags = null,
        [Description("Dividir a compra entre pessoas (não vale para estorno nem parcelamento em andamento).")] McpSplit? split = null,
        [Description("Observação.")] string? notes = null,
        [Description("Confirma lançar numa fatura que já está paga (só depois de a pessoa concordar).")] bool? confirmPaidInvoice = null,
        [Description("UUID opcional; repetir o mesmo requestId com os mesmos dados não cria de novo.")] string? requestId = null,
        CancellationToken cancellationToken = default) =>
        idempotency.RunAsync(requestId, "create_card_purchase", new { description, amount, creditCard, date, installments, tags, split, notes, confirmPaidInvoice }, async () =>
        {
            var used = new List<object>();

            var day = McpArgs.Date(date, "date");
            if (day is null)
            {
                used.Add(Used("date", "today"));
            }

            Guid cardId;
            if (McpArgs.NullIfBlank(creditCard) is { } cardName)
            {
                cardId = await catalog.ResolveCreditCardAsync(cardName, "creditCard", cancellationToken);
            }
            else
            {
                var choice = await defaults.CreditCardAsync(description, cancellationToken);
                cardId = choice.Id;
                used.Add(Used("creditCard", choice.Source));
            }

            var splitCommand = split is null ? null : await ToSplitAsync(split, cancellationToken);
            var tagIds = await catalog.ResolveOrCreateTagsAsync(tags, "tags", cancellationToken);
            var installmentsCommand = installments is null
                ? null
                : new InstallmentsCommand(installments.Total, McpArgs.NullIfBlank(installments.Mode) ?? "total_amount", installments.NextNumber, McpArgs.NullIfBlank(installments.FirstInvoiceMonth));
            var command = new CreateCardPurchaseCommand(
                description, amount, null, day ?? clock.Today, cardId, McpArgs.NullIfBlank(notes), installmentsCommand, confirmPaidInvoice == true, tagIds);

            var result = splitCommand is null
                ? await createPurchase.HandleAsync(command, cancellationToken)
                : await splitPurchase.HandleAsync(command, splitCommand, cancellationToken);

            var names = await catalog.LoadNamesAsync(cancellationToken);
            var invoices = new Dictionary<(Guid, YearMonth), Invoice?>();
            var purchases = new List<McpObject>();
            foreach (var p in result.Purchases)
            {
                if (!invoices.TryGetValue((p.CreditCardId, p.DueMonth), out var invoice))
                {
                    invoice = await cards.FindInvoiceAsync(p.CreditCardId, p.DueMonth, cancellationToken);
                    invoices[(p.CreditCardId, p.DueMonth)] = invoice;
                }

                var credit = p.Nature == PurchaseNature.Credit;
                purchases.Add(new McpObject()
                    .Set("id", p.Id)
                    .Set("description", p.DisplayDescription)
                    .Set("creditCard", names.CreditCard(p.CreditCardId))
                    .Set("date", p.Date)
                    .Set("nature", credit ? "credit" : "debit")
                    .Set("installment", p.InstallmentNumber is { } n ? $"{n}/{p.InstallmentTotal}" : null)
                    .Set("invoice", new McpObject()
                        .Set("dueMonth", p.DueMonth.ToString())
                        .Set("closingDate", invoice?.ClosingDate)
                        .Set("dueDate", invoice?.DueDate))
                    .Set("tags", p.TagIds.Select(names.Tag).ToList())
                    .Set("settlementTag", p.SettlementTagId is { } s ? names.Tag(s) : null)
                    .Set("notes", p.Notes)
                    .Set("splitGroupId", p.SplitGroupId)
                    .Money("amount", credit ? -p.AmountCents : p.AmountCents));
            }

            return new McpObject()
                .Set("today", clock.Today)
                .Set("defaultsUsed", used)
                .Set("purchases", purchases)
                .Set("warnings", result.Warnings.Select(Warning).ToList());
        }, cancellationToken);

    [McpServerTool(Name = "create_transfer", Title = "Transferir entre carteiras", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Cria uma transferência entre duas carteiras ativas. Data padrão: hoje; descrição padrão: \"Transferência\".")]
    public Task<CallToolResult> CreateTransfer(
        [Description("Nome da carteira de origem.")] string fromWallet,
        [Description("Nome da carteira de destino.")] string toWallet,
        [Description("Valor em reais como texto, ex.: \"200,00\".")] string amount,
        [Description("Data AAAA-MM-DD. Padrão: hoje.")] string? date = null,
        [Description("Descrição. Padrão: \"Transferência\".")] string? description = null,
        [Description("UUID opcional; repetir o mesmo requestId com os mesmos dados não cria de novo.")] string? requestId = null,
        CancellationToken cancellationToken = default) =>
        idempotency.RunAsync(requestId, "create_transfer", new { fromWallet, toWallet, amount, date, description }, async () =>
        {
            var used = new List<object>();
            var day = McpArgs.Date(date, "date");
            if (day is null)
            {
                used.Add(Used("date", "today"));
            }

            var from = await catalog.ResolveWalletAsync(fromWallet, "fromWallet", cancellationToken);
            var to = await catalog.ResolveWalletAsync(toWallet, "toWallet", cancellationToken);
            var transfer = await createTransfer.HandleAsync(
                new TransferInput(from, to, amount, day ?? clock.Today, McpArgs.NullIfBlank(description), null), cancellationToken);

            var names = await catalog.LoadNamesAsync(cancellationToken);
            return new McpObject()
                .Set("today", clock.Today)
                .Set("defaultsUsed", used)
                .Set("transfer", new McpObject()
                    .Set("id", transfer.Id)
                    .Set("fromWallet", names.Wallet(transfer.FromWalletId))
                    .Set("toWallet", names.Wallet(transfer.ToWalletId))
                    .Set("date", transfer.Date)
                    .Set("description", transfer.Description)
                    .Money("amount", transfer.AmountCents));
        }, cancellationToken);

    [McpServerTool(Name = "undo_creation", Title = "Desfazer lançamento", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Desfaz um lançamento, compra ou transferência que o Claude criou para a pessoa nos últimos 10 minutos, junto com o grupo criado com ele (parcelas ou partes da divisão). Use o `id` devolvido pela criação. Não serve para outros lançamentos (`undo_not_allowed`).")]
    public Task<CallToolResult> UndoCreation(
        [Description("Id de um item devolvido por create_entry, create_card_purchase ou create_transfer.")] string id,
        CancellationToken cancellationToken = default) => McpResult.RunAsync(async () =>
    {
        if (!Guid.TryParse(id, out var itemId))
        {
            throw new DomainException(EntryErrors.Field("id.invalid", "id", "Informe o id devolvido pela criação."));
        }

        return new McpObject().Set("undone", await undo.HandleAsync(itemId, cancellationToken));
    });

    private async Task<SplitCommand> ToSplitAsync(McpSplit split, CancellationToken cancellationToken)
    {
        var parts = new List<SplitPartCommand>();
        foreach (var (part, index) in (split.Parts ?? []).Select((p, i) => (p, i)))
        {
            Guid? tagId = McpArgs.NullIfBlank(part.Tag) is { } tag
                ? await catalog.ResolveTagAsync(tag, $"split.parts[{index}].tag", settlementOnly: true, cancellationToken)
                : null;
            parts.Add(new SplitPartCommand(tagId, part.Amount));
        }

        return new SplitCommand(McpArgs.NullIfBlank(split.Mode), parts);
    }

    private static object Used(string field, string source) => new { field, source };

    private static McpObject Warning(string code) => new McpObject().Set("code", code).Set("message", code switch
    {
        "recorded_as_credit" => "Valor negativo: foi registrado como crédito (estorno) que abate a fatura.",
        _ => code,
    });
}
