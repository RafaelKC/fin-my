using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Cards;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;
using FinMy.Domain.Recurrences;
using FinMy.Domain.Tags;
using FinMy.Domain.Transfers;
using FinMy.Domain.Wallets;

namespace FinMy.Api.Tests.Isolation;

/// <summary>
/// Fábricas de dados por tipo <c>ITenantEntity</c> (docs/arquitetura.md §3.7). A suíte descobre por reflexão
/// todos os tipos de Espaço mapeados e <b>falha</b> se algum não tiver fábrica aqui — quem criar uma entidade
/// de Espaço nas próximas features acrescenta a fábrica dela neste dicionário.
/// Além das sondas do contexto de teste (research R1), há as entidades da 002 (carteiras, lançamentos e transferências) da 003 (cartões, faturas, planos, compras e pagamentos) da 004 (tags) e da 005 (recorrências).
/// </summary>
public static class TenantDataFactories
{
    /// <summary>
    /// Para cada tipo, devolve as instâncias a gravar (o próprio tipo e o que ele exige, como o pai de uma FK composta).
    /// O <c>label</c> distingue os dados de cada Espaço.
    /// </summary>
    public static IReadOnlyDictionary<Type, Func<string, IReadOnlyList<object>>> ByType { get; } =
        new Dictionary<Type, Func<string, IReadOnlyList<object>>>
        {
            [typeof(ProbeParent)] = label => [new ProbeParent { Name = label }],
            [typeof(ProbeNamed)] = label => [new ProbeNamed { Name = label }],
            [typeof(ProbeItem)] = label =>
            {
                var parent = new ProbeParent { Name = label };
                return [parent, new ProbeItem { ParentId = parent.Id, Label = label }];
            },
            [typeof(Wallet)] = label => [NewWallet(label, primary: false)],
            [typeof(Entry)] = label =>
            {
                var wallet = NewWallet(label, primary: false);
                return [wallet, Entry.Create(EntryKind.Expense, label, Money.FromCents(4499), Today, wallet.Id, null, null, null, Today)];
            },
            [typeof(Tag)] = label => [Tag.Create(label, "#E53935", isSettlement: true)],
            [typeof(Recurrence)] = label =>
            {
                var wallet = NewWallet(label, primary: false);
                return
                [
                    wallet,
                    Recurrence.Create(
                        RecurrenceKind.Expense, label, Money.FromCents(150_000), 5, wallet.Id, null, null,
                        YearMonth.Of(2026, 3), RecurrenceEndType.None, null, null, ReferenceMonthRule.SameMonth),
                ];
            },
            [typeof(Transfer)] = label =>
            {
                var from = NewWallet($"{label} origem", primary: false);
                var to = NewWallet($"{label} destino", primary: false);
                return [from, to, Transfer.Create(from.Id, to.Id, Money.FromCents(1000), Today, null, null)];
            },
            [typeof(CreditCard)] = label => [.. CardGraph(label).Take(2)],
            [typeof(Invoice)] = label => [.. CardGraph(label).Take(3)],
            [typeof(InstallmentPlan)] = label => [.. CardGraph(label).Take(4)],
            [typeof(CardPurchase)] = label => [.. CardGraph(label).Take(5)],
            [typeof(InvoicePayment)] = label => CardGraph(label),
        };

    private static readonly DateOnly Today = new(2026, 3, 10);

    /// <summary>Carteira, cartão, fatura, plano, parcela e pagamento — cada tipo da 003 pede os anteriores (FKs compostas).</summary>
    private static IReadOnlyList<object> CardGraph(string label)
    {
        var wallet = NewWallet(label, primary: false);
        var card = CreditCard.Create(label, "#43A047", 5, 15, wallet.Id);
        var invoice = Invoice.Create(card.Id, YearMonth.Of(2026, 3), new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 15));
        var plan = InstallmentPlan.Create(card.Id, label, InstallmentMode.TotalAmount, 3, 1, Money.FromCents(30_000), Today, YearMonth.Of(2026, 3));
        var installment = CardPurchase.CreateInstallment(card.Id, invoice.Id, plan, 1, Money.FromCents(10_000), null);
        var payment = InvoicePayment.Create(invoice.Id, wallet.Id, Money.FromCents(1_000), Today);
        return [wallet, card, invoice, plan, installment, payment];
    }

    private static Wallet NewWallet(string name, bool primary) =>
        Wallet.Create(name, WalletType.Checking, "#1E88E5", Money.Zero, Today, primary);
}
