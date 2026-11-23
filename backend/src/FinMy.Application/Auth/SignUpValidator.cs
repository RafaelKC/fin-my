using FluentValidation;

namespace FinMy.Application.Auth;

/// <summary>Regras de entrada do cadastro (data-model.md <c>User</c>; spec FR-001 e FR-002). Mensagens em pt-BR.</summary>
public sealed class SignUpValidator : AbstractValidator<SignUpCommand>
{
    public const int NameMaxLength = 60;
    public const int EmailMaxLength = 254;
    public const int PasswordMinLength = 8;
    public const int PasswordMaxLength = 128;

    public SignUpValidator()
    {
        RuleFor(c => c.Name)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Informe seu nome.")
            .Must(n => n is null || n.Trim().Length <= NameMaxLength).WithMessage($"O nome pode ter no máximo {NameMaxLength} caracteres.");

        RuleFor(c => c.Email)
            .Must(e => !string.IsNullOrWhiteSpace(e)).WithMessage("Informe seu e-mail.")
            .Must(e => e is null || e.Trim().Length <= EmailMaxLength).WithMessage($"O e-mail pode ter no máximo {EmailMaxLength} caracteres.")
            .Must(e => string.IsNullOrWhiteSpace(e) || e.Trim().Length > EmailMaxLength || IsValidEmail(e.Trim()))
            .WithMessage("Informe um e-mail válido.");

        RuleFor(c => c.Password)
            .Must(p => !string.IsNullOrEmpty(p)).WithMessage("Informe uma senha.")
            .Must(p => string.IsNullOrEmpty(p) || p.Length >= PasswordMinLength).WithMessage($"A senha precisa ter pelo menos {PasswordMinLength} caracteres.")
            .Must(p => p is null || p.Length <= PasswordMaxLength).WithMessage($"A senha pode ter no máximo {PasswordMaxLength} caracteres.");
    }

    /// <summary>Formato <c>local@dominio.tld</c> sem espaços e com um único <c>@</c>; a prova real é o e-mail de recuperação.</summary>
    private static bool IsValidEmail(string email)
    {
        var at = email.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0 || at != email.LastIndexOf('@') || email.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var domain = email[(at + 1)..];
        var dot = domain.LastIndexOf('.');
        return dot > 0 && dot < domain.Length - 1 && !domain.StartsWith('.') && !domain.Contains("..", StringComparison.Ordinal);
    }
}
