namespace FinMy.Domain.Common;

/// <summary>Código COMPE do banco de uma carteira ou cartão (logo e cor no app); vazio = sem banco.</summary>
public static class BankCode
{
    public static string? Normalize(string? code)
    {
        var trimmed = code?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length == 3 && trimmed.All(char.IsAsciiDigit)
            ? trimmed
            : throw new DomainException(new DomainError("bank.invalid", "Banco inválido.", DomainErrorKind.Validation));
    }
}
