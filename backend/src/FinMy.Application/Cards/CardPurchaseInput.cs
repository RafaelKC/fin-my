using FinMy.Application.Abstractions;
using FinMy.Domain.Cards;
using FinMy.Domain.Common;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Cards;

/// <summary>Leitura dos textos de compra vindos da borda e regras compartilhadas pelos casos de uso de compras (003 research R5–R7).</summary>
internal static class CardPurchaseInput
{
    public const string RecordedAsCredit = "recorded_as_credit";

    private static readonly DateOnly MinDate = new(2000, 1, 1);
    private static readonly DateOnly MaxDate = new(2100, 12, 31);

    /// <summary>Valor com sinal (RT-05): negativo vira crédito com valor absoluto (research R6); zero é recusado.</summary>
    public static (Money Amount, bool Negative) ParseSignedAmount(string? text)
    {
        var result = ValueExpressionParser.Parse(text, allowNonPositive: true);
        if (!result.IsSuccess)
        {
            throw new DomainException(ValueExpressionErrors.ToDomainError(result.Error!, "amount"));
        }

        if (result.Cents == 0)
        {
            throw new DomainException(ValueExpressionErrors.ToDomainError(ValueExpressionErrors.MustBePositive, "amount"));
        }

        return (Money.FromCents(Math.Abs(result.Cents)), result.Cents < 0);
    }

    public static Money ParsePositiveAmount(string? text)
    {
        var result = ValueExpressionParser.Parse(text);
        return result.IsSuccess
            ? result.Money
            : throw new DomainException(ValueExpressionErrors.ToDomainError(result.Error!, "amount"));
    }

    public static PurchaseNature? ParseNature(string? text) => text switch
    {
        null or "" => null,
        "debit" => PurchaseNature.Debit,
        "credit" => PurchaseNature.Credit,
        _ => throw new DomainException(CardPurchaseErrors.InvalidNature),
    };

    public static string ToText(PurchaseNature nature) => nature == PurchaseNature.Credit ? "credit" : "debit";

    public static string ToText(PurchaseOrigin origin) => origin switch
    {
        PurchaseOrigin.Installment => "installment",
        PurchaseOrigin.CarryOver => "carry_over",
        _ => "manual",
    };

    public static InstallmentMode ParseMode(string? text) => text switch
    {
        "total_amount" => InstallmentMode.TotalAmount,
        "installment_amount" => InstallmentMode.InstallmentAmount,
        "in_progress" => InstallmentMode.InProgress,
        _ => throw new DomainException(CardPurchaseErrors.InvalidInstallmentMode),
    };

    /// <summary>Escopo da edição: <c>this</c> (padrão) ou <c>all</c>.</summary>
    public static bool ParseEditAll(string? scope) => scope switch
    {
        null or "" or "this" => false,
        "all" => true,
        _ => throw new DomainException(CardPurchaseErrors.InvalidScope),
    };

    /// <summary>Escopo de exclusão e movimento (research R7); <paramref name="allowAll"/> é falso no movimento.</summary>
    public static PurchaseScope ParseScope(string? scope, bool allowAll) => scope switch
    {
        null or "" or "this" => PurchaseScope.This,
        "this_and_following" => PurchaseScope.ThisAndFollowing,
        "all" when allowAll => PurchaseScope.All,
        _ => throw new DomainException(CardPurchaseErrors.InvalidScope),
    };

    public static Guid RequireCardId(Guid? id) =>
        id is { } value && value != Guid.Empty ? value : throw new DomainException(CardPurchaseErrors.CardRequired);

    public static DateOnly RequireDate(DateOnly? date)
    {
        var value = date ?? throw new DomainException(CardPurchaseErrors.DateRequired);
        return value >= MinDate && value <= MaxDate ? value : throw new DomainException(CreditCardErrors.DateOutOfRange);
    }

    public static long Signed(CardPurchase purchase) =>
        purchase.Nature == PurchaseNature.Credit ? -purchase.AmountCents : purchase.AmountCents;

    /// <summary>Cartão do Espaço (404 se não existe); <paramref name="mustBeActive"/> recusa o arquivado (nova compra, troca de cartão).</summary>
    public static async Task<CreditCard> RequireCardAsync(ICreditCardStore cards, Guid id, bool mustBeActive, CancellationToken cancellationToken)
    {
        var card = await cards.FindAsync(id, cancellationToken) ?? throw new DomainException(CreditCardErrors.NotFound);
        return mustBeActive && card.IsArchived ? throw new DomainException(CardPurchaseErrors.CardArchived) : card;
    }

    /// <summary>
    /// Alguma das faturas de destino (que existem) está Paga? Com <paramref name="requireClosed"/>, só vale depois do
    /// fechamento (research R5: antes dele o pagamento antecipado não pede confirmação).
    /// </summary>
    public static async Task<bool> AnyPaidAsync(
        ICreditCardStore cards,
        ICardPurchaseStore purchases,
        IEnumerable<(Guid CardId, YearMonth Month)> targets,
        DateOnly today,
        bool requireClosed,
        CancellationToken cancellationToken)
    {
        var rows = new List<Invoice>();
        foreach (var (cardId, month) in targets.Distinct())
        {
            if (await cards.FindInvoiceAsync(cardId, month, cancellationToken) is { } row)
            {
                rows.Add(row);
            }
        }

        if (rows.Count == 0)
        {
            return false;
        }

        var totals = await purchases.GetInvoiceTotalsAsync(rows.Select(r => r.Id).ToList(), cancellationToken);
        return rows.Any(r =>
        {
            var t = totals.GetValueOrDefault(r.Id);
            return (!requireClosed || today >= r.ClosingDate)
                && InvoiceStatusRule.Evaluate(t.TotalCents, t.PaidCents, today, r.ClosingDate, r.DueDate) == InvoiceStatus.Paid;
        });
    }
}

/// <summary>Quais parcelas uma exclusão ou movimento alcança (research R7).</summary>
public enum PurchaseScope
{
    This,
    ThisAndFollowing,
    All,
}
