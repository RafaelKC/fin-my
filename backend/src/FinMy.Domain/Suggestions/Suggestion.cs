using FinMy.Domain.Common;

namespace FinMy.Domain.Suggestions;

/// <summary>Situação de uma sugestão: pendente, lançada (virou lançamento) ou descartada.</summary>
public enum SuggestionStatus
{
    Pending,
    Added,
    Dismissed,
}

/// <summary>
/// Sugestão de lançamento lida de uma notificação de banco no celular (010): o app Android extrai valor, estabelecimento e
/// cartão; a pessoa lança (formulário pré-preenchido) ou descarta. <see cref="DedupeKey"/> evita repetir a mesma notificação.
/// </summary>
public sealed class Suggestion : TenantEntity
{
    public const int DescriptionMaxLength = 120;
    public const int RawTextMaxLength = 500;
    public const int SourceMaxLength = 120;
    public const int DedupeKeyMaxLength = 64;

    private Suggestion()
    {
    }

    public string Source { get; private set; } = string.Empty;
    public string? Bank { get; private set; }
    /// <summary><c>expense</c>, <c>income</c> ou <c>card_purchase</c>.</summary>
    public string Kind { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public long AmountCents { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string? CardLast4 { get; private set; }
    public string RawText { get; private set; } = string.Empty;
    public string DedupeKey { get; private set; } = string.Empty;
    public SuggestionStatus Status { get; private set; }
    /// <summary>Lançamento ou compra criado a partir da sugestão.</summary>
    public Guid? LinkedId { get; private set; }

    public static Suggestion Create(
        string source, string? bank, string kind, string description, long amountCents, DateTimeOffset occurredAt,
        string? cardLast4, string rawText, string dedupeKey)
    {
        if (kind is not ("expense" or "income" or "card_purchase"))
        {
            throw Invalid("kind", "Tipo inválido.");
        }

        if (amountCents <= 0)
        {
            throw Invalid("amount", "Valor inválido.");
        }

        if (string.IsNullOrWhiteSpace(dedupeKey) || dedupeKey.Length > DedupeKeyMaxLength)
        {
            throw Invalid("dedupeKey", "Chave inválida.");
        }

        var last4 = cardLast4?.Trim();
        return new Suggestion
        {
            Source = Clip(source, SourceMaxLength),
            Bank = BankCode.Normalize(bank),
            Kind = kind,
            Description = Clip(string.IsNullOrWhiteSpace(description) ? "Lançamento" : description, DescriptionMaxLength),
            AmountCents = amountCents,
            OccurredAt = occurredAt,
            CardLast4 = last4 is { Length: 4 } && last4.All(char.IsAsciiDigit) ? last4 : null,
            RawText = Clip(rawText, RawTextMaxLength),
            DedupeKey = dedupeKey,
            Status = SuggestionStatus.Pending,
        };
    }

    public void Dismiss() => Status = SuggestionStatus.Dismissed;

    public void MarkAdded(Guid? linkedId)
    {
        Status = SuggestionStatus.Added;
        LinkedId = linkedId;
    }

    private static string Clip(string? text, int max)
    {
        var clean = (text ?? string.Empty).Trim();
        return clean.Length <= max ? clean : clean[..max];
    }

    private static DomainException Invalid(string field, string message) =>
        new(new DomainError($"suggestion.{field}.invalid", message, DomainErrorKind.Validation));
}
