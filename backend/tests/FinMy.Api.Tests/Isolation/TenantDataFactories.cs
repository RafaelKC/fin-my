using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Entries;
using FinMy.Domain.Monetary;
using FinMy.Domain.Transfers;
using FinMy.Domain.Wallets;

namespace FinMy.Api.Tests.Isolation;

/// <summary>
/// Fábricas de dados por tipo <c>ITenantEntity</c> (docs/arquitetura.md §3.7). A suíte descobre por reflexão
/// todos os tipos de Espaço mapeados e <b>falha</b> se algum não tiver fábrica aqui — quem criar uma entidade
/// de Espaço nas próximas features acrescenta a fábrica dela neste dicionário.
/// Além das sondas do contexto de teste (research R1), há as entidades da 002: carteiras, lançamentos e transferências.
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
            [typeof(Transfer)] = label =>
            {
                var from = NewWallet($"{label} origem", primary: false);
                var to = NewWallet($"{label} destino", primary: false);
                return [from, to, Transfer.Create(from.Id, to.Id, Money.FromCents(1000), Today, null, null)];
            },
        };

    private static readonly DateOnly Today = new(2026, 3, 10);

    private static Wallet NewWallet(string name, bool primary) =>
        Wallet.Create(name, WalletType.Checking, "#1E88E5", Money.Zero, Today, primary);
}
