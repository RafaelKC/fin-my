using System.Text.RegularExpressions;
using FinMy.Domain.Monetary;
using FinMy.Domain.Wallets;
using FluentValidation;

namespace FinMy.Application.Wallets;

/// <summary>Campos editáveis de uma carteira, como chegam da API (contracts/openapi.yaml <c>WalletInput</c>).</summary>
public sealed record WalletInput(string? Name, string? Type, string? Color, string? InitialBalance, DateOnly? InitialBalanceDate);

/// <summary>Entrada já validada e convertida.</summary>
public sealed record ParsedWalletInput(string Name, WalletType Type, string Color, Money InitialBalance, DateOnly InitialBalanceDate);

/// <summary>Nomes de <see cref="WalletType"/> na API: <c>snake_case</c> (<c>meal_voucher</c>).</summary>
public static partial class WalletTypeNames
{
    public static string ToText(WalletType type) => Separator().Replace(type.ToString(), "$1_$2").ToLowerInvariant();

    public static bool TryParse(string? text, out WalletType type)
    {
        foreach (var candidate in Enum.GetValues<WalletType>())
        {
            if (string.Equals(ToText(candidate), text, StringComparison.Ordinal))
            {
                type = candidate;
                return true;
            }
        }

        type = default;
        return false;
    }

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex Separator();
}

public sealed partial class WalletInputValidator : AbstractValidator<WalletInput>
{
    public WalletInputValidator()
    {
        RuleFor(w => w.Name)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Informe o nome da carteira.")
            .Must(n => n is null || n.Trim().Length <= Wallet.NameMaxLength)
            .WithMessage($"O nome da carteira pode ter no máximo {Wallet.NameMaxLength} caracteres.");

        RuleFor(w => w.Type)
            .Must(t => WalletTypeNames.TryParse(t, out _)).WithMessage("Escolha um tipo de carteira válido.");

        RuleFor(w => w.Color)
            .Must(c => c is not null && ColorPattern().IsMatch(c)).WithMessage("Cor inválida. Use o formato #RRGGBB.");

        RuleFor(w => w.InitialBalance)
            .Must(b => ValueExpressionParser.Parse(b, allowNonPositive: true).IsSuccess)
            .WithMessage(w => InitialBalanceMessage(w.InitialBalance));

        RuleFor(w => w.InitialBalanceDate)
            .Must(d => d is not null).WithMessage("Informe a data do saldo inicial.")
            .Must(d => d is null || (d >= new DateOnly(2000, 1, 1) && d <= new DateOnly(2100, 12, 31)))
            .WithMessage("A data deve estar entre 01/01/2000 e 31/12/2100.");
    }

    /// <summary>Valida e converte; lança <see cref="ValidationException"/> com as mensagens por campo.</summary>
    public static ParsedWalletInput ValidateAndParse(WalletInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        new WalletInputValidator().ValidateAndThrow(input);

        // Saldo inicial é a única exceção à RT-02: aceita zero e negativo.
        var balance = ValueExpressionParser.Parse(input.InitialBalance, allowNonPositive: true).Money;
        var type = WalletTypeNames.TryParse(input.Type, out var parsedType) ? parsedType : throw new InvalidOperationException("Tipo validado acima.");
        return new ParsedWalletInput(input.Name!.Trim(), type, input.Color!, balance, input.InitialBalanceDate!.Value);
    }

    private static string InitialBalanceMessage(string? text)
    {
        var parsed = ValueExpressionParser.Parse(text, allowNonPositive: true);
        return parsed.Error is null ? string.Empty : ValueExpressionErrors.ToDomainError(parsed.Error, "initialBalance").Message;
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex ColorPattern();
}
