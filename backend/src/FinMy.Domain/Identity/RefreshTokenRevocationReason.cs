namespace FinMy.Domain.Identity;

/// <summary>Motivo do encerramento de um refresh token (gravado em <c>snake_case</c>).</summary>
public enum RefreshTokenRevocationReason
{
    Logout,
    Rotated,
    ReuseDetected,
    PasswordChanged,
    PasswordReset,
}
