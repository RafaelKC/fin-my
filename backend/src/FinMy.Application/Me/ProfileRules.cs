using FluentValidation;

namespace FinMy.Application.Profile;

/// <summary>Regras compartilhadas do perfil (data-model.md: nome do usuário e do Espaço têm 1–60 caracteres após trim).</summary>
internal static class ProfileRules
{
    public const int NameMaxLength = 60;
    public const int PasswordMinLength = 8;
    public const int PasswordMaxLength = 128;

    public static IRuleBuilderOptions<T, string?> ValidName<T>(this IRuleBuilder<T, string?> rule, string requiredMessage)
        => rule
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage(requiredMessage)
            .Must(n => n is null || n.Trim().Length <= NameMaxLength).WithMessage($"O nome pode ter no máximo {NameMaxLength} caracteres.");
}
