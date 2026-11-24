using FinMy.Domain.Common;
using FinMy.Domain.Monetary;

namespace FinMy.Domain.Recurrences;

/// <summary>
/// Recorrência (<c>recurrences</c>, 005): modelo mensal que gera Entradas, Saídas ou compras no cartão à vista.
/// Guarda a regra; as ocorrências são lançamentos comuns ligados a ela.
/// </summary>
public sealed class Recurrence : TenantEntity
{
    public const int MaxOccurrenceLimit = 360;

    // Construtor para o EF Core.
    private Recurrence()
    {
    }

    private Recurrence(
        RecurrenceKind kind,
        string description,
        Money amount,
        int dayOfMonth,
        Guid? walletId,
        Guid? creditCardId,
        Guid? settlementTagId,
        YearMonth startMonth,
        RecurrenceEndType endType,
        YearMonth? endMonth,
        int? occurrenceLimit,
        ReferenceMonthRule? referenceMonthRule)
    {
        Kind = kind;
        StartMonth = Rules.Date(startMonth.FirstDay, "startMonth");
        Configure(description, amount, dayOfMonth, walletId, creditCardId, settlementTagId, endType, endMonth, occurrenceLimit, referenceMonthRule);
    }

    public RecurrenceKind Kind { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public string DescriptionNormalized { get; private set; } = string.Empty;

    public long AmountCents { get; private set; }

    /// <summary>Dia do mês de 1 a 31; mês mais curto usa o último dia (RT-03).</summary>
    public int DayOfMonth { get; private set; }

    /// <summary>Carteira das entradas/saídas; nula para compra no cartão.</summary>
    public Guid? WalletId { get; private set; }

    /// <summary>Cartão das compras; nulo para entrada/saída.</summary>
    public Guid? CreditCardId { get; private set; }

    public Guid? SettlementTagId { get; private set; }

    /// <summary>Primeiro mês (dia 1).</summary>
    public DateOnly StartMonth { get; private set; }

    public RecurrenceEndType EndType { get; private set; }

    /// <summary>Último mês gerado (dia 1); obrigatório quando há término. Em <c>after_count</c> é derivado do limite.</summary>
    public DateOnly? EndMonth { get; private set; }

    public int? OccurrenceLimit { get; private set; }

    /// <summary>Nula para compra no cartão (a fatura vem do ciclo do cartão).</summary>
    public ReferenceMonthRule? ReferenceMonthRule { get; private set; }

    /// <summary>Último mês já gerado; o gerador só cria meses posteriores (005 FR-008).</summary>
    public DateOnly? GeneratedUntilMonth { get; private set; }

    public Money Amount => new(AmountCents);

    public YearMonth StartYearMonth => YearMonth.From(StartMonth);

    public YearMonth? EndYearMonth => EndMonth is { } m ? YearMonth.From(m) : null;

    public YearMonth? GeneratedUntil => GeneratedUntilMonth is { } m ? YearMonth.From(m) : null;

    public static Recurrence Create(
        RecurrenceKind kind,
        string description,
        Money amount,
        int dayOfMonth,
        Guid? walletId,
        Guid? creditCardId,
        Guid? settlementTagId,
        YearMonth startMonth,
        RecurrenceEndType endType,
        YearMonth? endMonth,
        int? occurrenceLimit,
        ReferenceMonthRule? referenceMonthRule) =>
        new(kind, description, amount, dayOfMonth, walletId, creditCardId, settlementTagId, startMonth, endType, endMonth, occurrenceLimit, referenceMonthRule);

    /// <summary>
    /// Atualiza o modelo (005 FR-006). O tipo e o mês inicial não mudam; quem chama aplica as mudanças às
    /// ocorrências elegíveis.
    /// </summary>
    public void ChangeFrom(
        string description,
        Money amount,
        int dayOfMonth,
        Guid? walletId,
        Guid? creditCardId,
        Guid? settlementTagId,
        RecurrenceEndType endType,
        YearMonth? endMonth,
        int? occurrenceLimit,
        ReferenceMonthRule? referenceMonthRule) =>
        Configure(description, amount, dayOfMonth, walletId, creditCardId, settlementTagId, endType, endMonth, occurrenceLimit, referenceMonthRule);

    /// <summary>Encerra a recorrência: o último mês gerado passa a ser <c>fromMonth − 1</c> (005 FR-007).</summary>
    public void End(YearMonth fromMonth)
    {
        if (fromMonth <= StartYearMonth)
        {
            throw Rules.Invalid("recurrence.end_before_start", "fromMonth", "O encerramento deve ser depois do mês inicial");
        }

        EndType = RecurrenceEndType.UntilMonth;
        EndMonth = Rules.Date(fromMonth.Previous().FirstDay, "endMonth");
        OccurrenceLimit = null;
    }

    /// <summary>Registra o último mês gerado; nunca retrocede.</summary>
    public void AdvanceGeneratedUntil(YearMonth month)
    {
        if (GeneratedUntil is { } current && month <= current)
        {
            return;
        }

        GeneratedUntilMonth = month.FirstDay;
    }

    private void Configure(
        string description,
        Money amount,
        int dayOfMonth,
        Guid? walletId,
        Guid? creditCardId,
        Guid? settlementTagId,
        RecurrenceEndType endType,
        YearMonth? endMonth,
        int? occurrenceLimit,
        ReferenceMonthRule? referenceMonthRule)
    {
        var text = Rules.RequiredText(description, Rules.DescriptionMaxLength, "description", "description", "a descrição");
        var cents = Rules.EntryAmount(amount);

        if (dayOfMonth is < 1 or > 31)
        {
            throw Rules.Invalid("recurrence.day_invalid", "dayOfMonth", "O dia do mês deve ficar entre 1 e 31");
        }

        var isCard = Kind == RecurrenceKind.CardPurchase;
        if (isCard)
        {
            if (creditCardId is null)
            {
                throw Rules.Invalid("recurrence.card_required", "creditCardId", "Informe o cartão");
            }

            if (walletId is not null)
            {
                throw Rules.Invalid("recurrence.wallet_not_allowed", "walletId", "Compra no cartão não tem carteira");
            }

            if (referenceMonthRule is not null)
            {
                throw Rules.Invalid("recurrence.reference_rule_not_allowed", "referenceMonthRule", "Compra no cartão não tem regra de mês de referência");
            }
        }
        else
        {
            if (walletId is null)
            {
                throw Rules.Invalid("recurrence.wallet_required", "walletId", "Informe a carteira");
            }

            if (creditCardId is not null)
            {
                throw Rules.Invalid("recurrence.card_not_allowed", "creditCardId", "Entrada ou saída não tem cartão");
            }

            if (referenceMonthRule is null)
            {
                throw Rules.Invalid("recurrence.reference_rule_required", "referenceMonthRule", "Informe o mês de referência");
            }
        }

        var (resolvedEnd, resolvedLimit) = ResolveEnd(endType, endMonth, occurrenceLimit);

        Description = text;
        DescriptionNormalized = TextNormalizer.Normalize(text);
        AmountCents = cents;
        DayOfMonth = dayOfMonth;
        WalletId = walletId;
        CreditCardId = creditCardId;
        SettlementTagId = settlementTagId;
        EndType = endType;
        EndMonth = resolvedEnd;
        OccurrenceLimit = resolvedLimit;
        ReferenceMonthRule = referenceMonthRule;
    }

    private (DateOnly? EndMonth, int? Limit) ResolveEnd(RecurrenceEndType endType, YearMonth? endMonth, int? limit)
    {
        switch (endType)
        {
            case RecurrenceEndType.None:
                return (null, null);

            case RecurrenceEndType.UntilMonth:
                if (endMonth is not { } until)
                {
                    throw Rules.Invalid("recurrence.end_month_required", "endMonth", "Informe o mês final");
                }

                if (until < StartYearMonth)
                {
                    throw Rules.Invalid("recurrence.end_before_start", "endMonth", "O mês final deve ser igual ou depois do mês inicial");
                }

                return (Rules.Date(until.FirstDay, "endMonth"), null);

            case RecurrenceEndType.AfterCount:
                if (limit is null or < 1 or > MaxOccurrenceLimit)
                {
                    throw Rules.Invalid("recurrence.limit_invalid", "occurrenceLimit", $"O número de ocorrências deve ficar entre 1 e {MaxOccurrenceLimit}");
                }

                return (Rules.Date(StartYearMonth.AddMonths(limit.Value - 1).FirstDay, "endMonth"), limit);

            default:
                throw new ArgumentOutOfRangeException(nameof(endType));
        }
    }
}
