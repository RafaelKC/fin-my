using System.Text.RegularExpressions;
using FinMy.Domain.Common;
using FinMy.Domain.Monetary;

namespace FinMy.Domain.Cards;

/// <summary>
/// Cartão de crédito (<c>credit_cards</c>, 003 FR-001). Nome único no Espaço sem diferenciar maiúsculas/minúsculas
/// e acentos (índice sobre <see cref="NameNormalized"/>). Os dias de fechamento e vencimento definem o ciclo (<see cref="InvoiceCycle"/>).
/// </summary>
public sealed partial class CreditCard : TenantEntity, IArchivable
{
    public const int NameMaxLength = 40;

    // Construtor para o EF Core.
    private CreditCard()
    {
    }

    private CreditCard(string name, string color, int closingDay, int dueDay, Guid paymentWalletId, Money? limit)
    {
        SetName(name);
        SetColor(color);
        ChangeDays(closingDay, dueDay);
        PaymentWalletId = paymentWalletId;
        ChangeLimit(limit);
    }

    public string Name { get; private set; } = string.Empty;

    public string NameNormalized { get; private set; } = string.Empty;

    /// <summary>Cor no formato <c>#RRGGBB</c>.</summary>
    public string Color { get; private set; } = string.Empty;

    public int ClosingDay { get; private set; }

    public int DueDay { get; private set; }

    /// <summary>Carteira que paga a fatura por padrão.</summary>
    public Guid PaymentWalletId { get; private set; }

    /// <summary>Limite em centavos; <c>null</c> = sem limite informado.</summary>
    public long? LimitCents { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    public bool IsArchived => ArchivedAt is not null;

    public static CreditCard Create(string name, string color, int closingDay, int dueDay, Guid paymentWalletId, Money? limit = null) =>
        new(name, color, closingDay, dueDay, paymentWalletId, limit);

    public void Rename(string name) => SetName(name);

    public void ChangeColor(string color) => SetColor(color);

    public void ChangeDays(int closingDay, int dueDay)
    {
        ClosingDay = Day(closingDay, "closingDay", "credit_card.closing_day_invalid", "o dia de fechamento");
        DueDay = Day(dueDay, "dueDay", "credit_card.due_day_invalid", "o dia de vencimento");
    }

    public void ChangePaymentWallet(Guid walletId) => PaymentWalletId = walletId;

    public void ChangeLimit(Money? limit)
    {
        if (limit is { Cents: < 0 })
        {
            throw Rules.Invalid("credit_card.limit_invalid", "limit", "O limite não pode ser negativo");
        }

        if (limit is { } value && value.Cents > Money.MaxCents)
        {
            throw Rules.AmountError(ValueExpressionErrors.TooLarge, "limit");
        }

        LimitCents = limit?.Cents;
    }

    public void Archive(DateTimeOffset now) => ArchivedAt ??= now;

    public void Unarchive() => ArchivedAt = null;

    private static int Day(int day, string field, string code, string label) =>
        day is >= 1 and <= 31
            ? day
            : throw Rules.Invalid(code, field, $"Informe {label} entre 1 e 31");

    private void SetName(string name)
    {
        Name = Rules.RequiredText(name, NameMaxLength, "name", "credit_card.name", "o nome do cartão");
        NameNormalized = TextNormalizer.Normalize(Name);
    }

    private void SetColor(string color)
    {
        if (color is null || !ColorPattern().IsMatch(color))
        {
            throw Rules.Invalid("credit_card.color_invalid", "color", "Cor inválida. Use o formato #RRGGBB");
        }

        Color = color;
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex ColorPattern();
}
