using System.Text.RegularExpressions;
using FinMy.Domain.Common;
using FinMy.Domain.Monetary;

namespace FinMy.Domain.Wallets;

/// <summary>
/// Carteira (<c>wallets</c>, 002 FR-001..004): onde o dinheiro está. Nome único no Espaço sem diferenciar
/// maiúsculas/minúsculas e acentos (garantido por índice sobre <see cref="NameNormalized"/>).
/// Quem controla "exatamente uma principal" é a aplicação (research R6); aqui só valem as regras da própria linha.
/// </summary>
public sealed partial class Wallet : TenantEntity, IArchivable
{
    public const int NameMaxLength = 40;

    // Construtor para o EF Core.
    private Wallet()
    {
    }

    private Wallet(string name, WalletType type, string color, long initialBalanceCents, DateOnly initialBalanceDate, bool isPrimary)
    {
        SetName(name);
        Type = type;
        SetColor(color);
        SetInitialBalance(initialBalanceCents, initialBalanceDate);
        IsPrimary = isPrimary;
    }

    public string Name { get; private set; } = string.Empty;

    public string NameNormalized { get; private set; } = string.Empty;

    public WalletType Type { get; private set; }

    /// <summary>Cor no formato <c>#RRGGBB</c>.</summary>
    public string Color { get; private set; } = string.Empty;

    /// <summary>Saldo inicial em centavos, de qualquer sinal (exceção à RT-02), no máximo R$ 9.999.999,99 em módulo.</summary>
    public long InitialBalanceCents { get; private set; }

    public DateOnly InitialBalanceDate { get; private set; }

    public bool IsPrimary { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    public bool IsArchived => ArchivedAt is not null;

    public Money InitialBalance => new(InitialBalanceCents);

    public static Wallet Create(
        string name,
        WalletType type,
        string color,
        Money initialBalance,
        DateOnly initialBalanceDate,
        bool isPrimary = false) =>
        new(name, type, color, initialBalance.Cents, initialBalanceDate, isPrimary);

    public void Rename(string name) => SetName(name);

    public void ChangeType(WalletType type) => Type = type;

    public void ChangeColor(string color) => SetColor(color);

    public void ChangeInitialBalance(Money balance, DateOnly date) => SetInitialBalance(balance.Cents, date);

    /// <summary>Arquiva a carteira. A principal não pode ser arquivada: troque a principal antes (research R6).</summary>
    public void Archive(DateTimeOffset now)
    {
        if (IsPrimary)
        {
            throw Rules.Invalid("wallet.new_primary_required", "newPrimaryWalletId", "Escolha outra carteira principal antes de arquivar esta");
        }

        ArchivedAt ??= now;
    }

    public void Unarchive() => ArchivedAt = null;

    /// <summary>Marca como principal; só carteiras ativas podem ser principais.</summary>
    public void MarkPrimary()
    {
        if (IsArchived)
        {
            throw Rules.Invalid("wallet.archived", "walletId", "Uma carteira arquivada não pode ser a principal");
        }

        IsPrimary = true;
    }

    public void UnmarkPrimary() => IsPrimary = false;

    private void SetName(string name)
    {
        Name = Rules.RequiredText(name, NameMaxLength, "name", "wallet.name", "o nome da carteira");
        NameNormalized = TextNormalizer.Normalize(Name);
    }

    private void SetColor(string color)
    {
        if (color is null || !ColorPattern().IsMatch(color))
        {
            throw Rules.Invalid("wallet.color_invalid", "color", "Cor inválida. Use o formato #RRGGBB");
        }

        Color = color;
    }

    private void SetInitialBalance(long cents, DateOnly date)
    {
        if (Math.Abs(cents) > Money.MaxCents)
        {
            throw Rules.AmountError(ValueExpressionErrors.TooLarge, "initialBalance");
        }

        InitialBalanceCents = cents;
        InitialBalanceDate = Rules.Date(date, "initialBalanceDate");
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex ColorPattern();
}
