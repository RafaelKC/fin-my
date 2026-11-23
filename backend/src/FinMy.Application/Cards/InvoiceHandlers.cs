using FinMy.Application.Abstractions;
using FinMy.Application.Tags;
using FinMy.Domain.Cards;
using FinMy.Domain.Common;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Cards;

internal static class InvoiceInput
{
    private static readonly YearMonth First = YearMonth.Of(2000, 1);
    private static readonly YearMonth Last = YearMonth.Of(2100, 12);

    public static YearMonth ParseMonth(string? month) =>
        YearMonth.TryParse(month, out var parsed) && parsed >= First && parsed <= Last
            ? parsed
            : throw new DomainException(InvoiceErrors.InvalidMonth);

    /// <summary>A carteira do pagamento tem de existir no Espaço e estar ativa.</summary>
    public static async Task EnsureActiveWalletAsync(IWalletStore wallets, Guid walletId, CancellationToken cancellationToken)
    {
        var wallet = await wallets.FindAsync(walletId, cancellationToken) ?? throw new DomainException(InvoiceErrors.WalletNotFound);
        if (wallet.IsArchived)
        {
            throw new DomainException(InvoiceErrors.WalletArchived);
        }
    }

    public static async Task<long> RemainingAsync(ICardPurchaseStore purchases, Guid invoiceId, CancellationToken cancellationToken)
    {
        var totals = (await purchases.GetInvoiceTotalsAsync([invoiceId], cancellationToken))[invoiceId];
        return totals.TotalCents - totals.PaidCents;
    }
}

/// <summary>Fatura de um cartão num mês de vencimento (US5, US6.2), exista a linha ou não (research R2). Cartão arquivado também.</summary>
public sealed class GetInvoiceHandler(ICreditCardStore cards, IInvoiceStore invoices, IClock clock)
{
    /// <param name="tagIds">Filtro por tags (004 FR-004): compras com qualquer uma delas; vazio/ausente = todas.</param>
    public async Task<InvoiceDetailView> HandleAsync(Guid creditCardId, string? month, string? tagIds, CancellationToken cancellationToken)
    {
        var card = await cards.FindAsync(creditCardId, cancellationToken) ?? throw new DomainException(CreditCardErrors.NotFound);
        return await invoices.GetDetailAsync(card, InvoiceInput.ParseMonth(month), clock.Today, TagSelector.ParseFilter(tagIds), cancellationToken);
    }
}

/// <summary>
/// Registra um pagamento (FR-017, research R4). Com a linha da fatura bloqueada, recalcula o Restante e recusa valor acima dele.
/// Corpo vazio = padrões: valor = Restante, data = hoje, carteira = a de pagamento do cartão (caixinha da visão do mês, FR-019).
/// </summary>
public sealed class RegisterInvoicePaymentHandler(
    ICreditCardStore cards, ICardPurchaseStore purchases, IInvoiceStore invoices, IWalletStore wallets, IClock clock)
{
    public async Task<InvoiceDetailView> HandleAsync(RegisterInvoicePaymentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var card = await cards.FindAsync(command.CreditCardId, cancellationToken) ?? throw new DomainException(CreditCardErrors.NotFound);
        var month = InvoiceInput.ParseMonth(command.Month);
        var requested = string.IsNullOrWhiteSpace(command.Amount) ? (Money?)null : CardPurchaseInput.ParsePositiveAmount(command.Amount);
        var walletId = command.WalletId ?? card.PaymentWalletId;
        await InvoiceInput.EnsureActiveWalletAsync(wallets, walletId, cancellationToken);
        var date = command.Date ?? clock.Today;

        // Sem linha de fatura não há valor em aberto (research R2).
        var invoice = await cards.FindInvoiceAsync(card.Id, month, cancellationToken) ?? throw new DomainException(InvoiceErrors.NothingToPay);

        await using var transaction = await purchases.BeginTransactionAsync(cancellationToken);
        await invoices.LockAsync(invoice.Id, cancellationToken);

        var remaining = await InvoiceInput.RemainingAsync(purchases, invoice.Id, cancellationToken);
        if (remaining <= 0)
        {
            throw new DomainException(InvoiceErrors.NothingToPay);
        }

        var amount = requested ?? Money.FromCents(remaining);
        if (amount.Cents > remaining)
        {
            throw new DomainException(InvoiceErrors.PaymentExceedsRemaining(remaining));
        }

        invoices.Add(InvoicePayment.Create(invoice.Id, walletId, amount, date));
        await invoices.SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await invoices.GetDetailAsync(card, month, clock.Today, [], cancellationToken);
    }
}

/// <summary>Edita um pagamento: o máximo é o Restante mais o valor atual do próprio pagamento (FR-018). Exige <c>version</c>.</summary>
public sealed class UpdateInvoicePaymentHandler(
    ICardPurchaseStore purchases, IInvoiceStore invoices, IWalletStore wallets)
{
    public async Task<InvoicePaymentView> HandleAsync(UpdateInvoicePaymentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var payment = await invoices.FindPaymentAsync(command.Id, cancellationToken) ?? throw new DomainException(InvoiceErrors.PaymentNotFound);
        var version = command.Version ?? throw new DomainException(InvoiceErrors.VersionRequired);
        var amount = string.IsNullOrWhiteSpace(command.Amount)
            ? throw new DomainException(InvoiceErrors.AmountRequired)
            : CardPurchaseInput.ParsePositiveAmount(command.Amount);
        var date = command.Date ?? throw new DomainException(InvoiceErrors.DateRequired);
        var walletId = command.WalletId is { } w && w != Guid.Empty ? w : throw new DomainException(InvoiceErrors.WalletRequired);
        if (walletId != payment.WalletId)
        {
            await InvoiceInput.EnsureActiveWalletAsync(wallets, walletId, cancellationToken);
        }

        if (invoices.VersionOf(payment) != version)
        {
            throw new DomainException(InvoiceErrors.ConcurrencyConflict);
        }

        invoices.ExpectVersion(payment, version);

        await using var transaction = await purchases.BeginTransactionAsync(cancellationToken);
        await invoices.LockAsync(payment.InvoiceId, cancellationToken);

        var maximum = await InvoiceInput.RemainingAsync(purchases, payment.InvoiceId, cancellationToken) + payment.AmountCents;
        if (amount.Cents > maximum)
        {
            throw new DomainException(InvoiceErrors.PaymentExceedsRemaining(Math.Max(maximum, 0)));
        }

        payment.ChangeAmount(amount);
        payment.ChangeDate(date);
        payment.ChangeWallet(walletId);
        await invoices.SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await invoices.GetPaymentViewAsync(payment.Id, cancellationToken) ?? throw new InvalidOperationException("Pagamento editado não encontrado.");
    }
}

/// <summary>Exclui um pagamento (RT-09): lote que a pessoa pode desfazer.</summary>
public sealed class DeleteInvoicePaymentHandler(IInvoiceStore invoices, ICurrentContext context)
{
    public async Task<Guid> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var payment = await invoices.FindPaymentAsync(id, cancellationToken) ?? throw new DomainException(InvoiceErrors.PaymentNotFound);

        var batchId = context.BeginDeletionBatch();
        invoices.Remove(payment);
        await invoices.SaveAsync(cancellationToken);
        return batchId;
    }
}

/// <summary>Exclui todos os pagamentos da fatura num só lote (desmarcar a caixinha, FR-019, US5.6).</summary>
public sealed class DeleteInvoicePaymentsHandler(
    ICreditCardStore cards, ICardPurchaseStore purchases, IInvoiceStore invoices, ICurrentContext context)
{
    public async Task<Guid> HandleAsync(Guid creditCardId, string? month, CancellationToken cancellationToken)
    {
        var card = await cards.FindAsync(creditCardId, cancellationToken) ?? throw new DomainException(CreditCardErrors.NotFound);
        var parsed = InvoiceInput.ParseMonth(month);
        var invoice = await cards.FindInvoiceAsync(card.Id, parsed, cancellationToken) ?? throw new DomainException(InvoiceErrors.NoPayments);

        await using var transaction = await purchases.BeginTransactionAsync(cancellationToken);
        await invoices.LockAsync(invoice.Id, cancellationToken);

        var payments = await invoices.ListPaymentsAsync(invoice.Id, cancellationToken);
        if (payments.Count == 0)
        {
            throw new DomainException(InvoiceErrors.NoPayments);
        }

        var batchId = context.BeginDeletionBatch();
        foreach (var payment in payments)
        {
            invoices.Remove(payment);
        }

        await invoices.SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return batchId;
    }
}

/// <summary>
/// "Levar restante para a próxima fatura" (FR-020, US5.4): depois do fechamento e com Restante ≠ 0, cria o par ligado de
/// compras — com Restante &gt; 0, Crédito "Saldo levado para MM/AAAA" nesta e Débito "Saldo da fatura MM/AAAA" na próxima; com
/// Restante &lt; 0 (total negativo), o inverso. Com a fatura bloqueada, para duas chamadas não levarem o mesmo restante.
/// </summary>
public sealed class CarryOverInvoiceHandler(
    ICreditCardStore cards, ICardPurchaseStore purchases, IInvoiceStore invoices, IInvoiceRepository invoiceRepository, IClock clock)
{
    public async Task<InvoiceDetailView> HandleAsync(Guid creditCardId, string? month, CancellationToken cancellationToken)
    {
        var card = await cards.FindAsync(creditCardId, cancellationToken) ?? throw new DomainException(CreditCardErrors.NotFound);
        var parsed = InvoiceInput.ParseMonth(month);
        var today = clock.Today;
        var invoice = await cards.FindInvoiceAsync(card.Id, parsed, cancellationToken) ?? throw new DomainException(InvoiceErrors.CarryOverNotAvailable);
        if (today < invoice.ClosingDate)
        {
            throw new DomainException(InvoiceErrors.CarryOverNotAvailable);
        }

        await using var transaction = await purchases.BeginTransactionAsync(cancellationToken);
        await invoices.LockAsync(invoice.Id, cancellationToken);

        var remaining = await InvoiceInput.RemainingAsync(purchases, invoice.Id, cancellationToken);
        if (remaining == 0)
        {
            throw new DomainException(InvoiceErrors.CarryOverNotAvailable);
        }

        var next = parsed.Next();
        var target = await invoiceRepository.GetOrCreateAsync(card.Id, next, cancellationToken);
        var amount = Money.FromCents(Math.Abs(remaining));
        var here = remaining > 0 ? PurchaseNature.Credit : PurchaseNature.Debit;
        var there = remaining > 0 ? PurchaseNature.Debit : PurchaseNature.Credit;
        var pairId = Guid.CreateVersion7();

        purchases.Add(CardPurchase.CreateCarryOver(card.Id, invoice.Id, $"Saldo levado para {Label(next)}", amount, here, today, pairId));
        purchases.Add(CardPurchase.CreateCarryOver(card.Id, target.Id, $"Saldo da fatura {Label(parsed)}", amount, there, today, pairId));
        await purchases.SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await invoices.GetDetailAsync(card, parsed, today, [], cancellationToken);
    }

    private static string Label(YearMonth month) => $"{month.Month:00}/{month.Year}";
}
