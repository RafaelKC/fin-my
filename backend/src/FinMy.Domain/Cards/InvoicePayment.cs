using FinMy.Domain.Common;
using FinMy.Domain.Monetary;

namespace FinMy.Domain.Cards;

/// <summary>
/// Pagamento de fatura (<c>invoice_payments</c>, 003 FR-017..019): sai de uma Carteira. O limite pelo Restante é
/// validado na aplicação, com a fatura bloqueada na transação (research R4). O mês de referência é o da fatura (RT-04).
/// </summary>
public sealed class InvoicePayment : TenantEntity
{
    // Construtor para o EF Core.
    private InvoicePayment()
    {
    }

    private InvoicePayment(Guid invoiceId, Guid walletId, Money amount, DateOnly paymentDate)
    {
        InvoiceId = invoiceId;
        WalletId = walletId;
        ChangeAmount(amount);
        ChangeDate(paymentDate);
    }

    public Guid InvoiceId { get; private set; }

    public Guid WalletId { get; private set; }

    public long AmountCents { get; private set; }

    public DateOnly PaymentDate { get; private set; }

    public Money Amount => new(AmountCents);

    public static InvoicePayment Create(Guid invoiceId, Guid walletId, Money amount, DateOnly paymentDate) =>
        new(invoiceId, walletId, amount, paymentDate);

    public void ChangeAmount(Money amount) => AmountCents = Rules.EntryAmount(amount);

    public void ChangeDate(DateOnly paymentDate) => PaymentDate = Rules.Date(paymentDate, "paymentDate");

    public void ChangeWallet(Guid walletId) => WalletId = walletId;
}
