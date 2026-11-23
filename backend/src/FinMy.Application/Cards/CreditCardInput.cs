using FinMy.Domain.Common;
using FinMy.Domain.Monetary;

namespace FinMy.Application.Cards;

/// <summary>Campos editáveis de um cartão, como chegam da API (contrato <c>CreditCardInput</c>). <c>Limit</c> é expressão RT-05; vazio = sem limite.</summary>
public sealed record CreditCardInput(string? Name, int? ClosingDay, int? DueDay, Guid? PaymentWalletId, string? Color, string? Limit);

/// <summary>Entrada já convertida (as regras de nome, cor e dias ficam nas entidades).</summary>
internal sealed record ParsedCreditCardInput(string Name, string Color, int ClosingDay, int DueDay, Guid PaymentWalletId, Money? Limit);

internal static class CreditCardInputParser
{
    public static ParsedCreditCardInput Parse(CreditCardInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        Money? limit = null;
        if (!string.IsNullOrWhiteSpace(input.Limit))
        {
            // Limite zero é válido (CHECK >= 0); negativo cai na regra do domínio.
            var parsed = ValueExpressionParser.Parse(input.Limit, allowNonPositive: true);
            limit = parsed.IsSuccess ? parsed.Money : throw new DomainException(ValueExpressionErrors.ToDomainError(parsed.Error!, "limit"));
        }

        var walletId = input.PaymentWalletId is { } id && id != Guid.Empty ? id : throw new DomainException(CreditCardErrors.PaymentWalletRequired);

        // Dia ausente vira 0, que a entidade recusa com a mensagem do campo.
        return new ParsedCreditCardInput(input.Name ?? string.Empty, input.Color ?? string.Empty, input.ClosingDay ?? 0, input.DueDay ?? 0, walletId, limit);
    }
}
