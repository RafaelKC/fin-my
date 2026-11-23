namespace FinMy.Domain.Identity;

/// <summary>Contador de tentativas de login por e-mail normalizado (<c>login_throttles</c>), exista ou não a conta.</summary>
public sealed class LoginThrottle
{
    // Construtor para o EF Core.
    private LoginThrottle()
    {
    }

    public LoginThrottle(string emailNormalized, DateTimeOffset now)
    {
        EmailNormalized = emailNormalized;
        WindowStartedAt = now;
    }

    public string EmailNormalized { get; private set; } = string.Empty;

    public int FailedCount { get; private set; }

    public DateTimeOffset WindowStartedAt { get; private set; }

    public DateTimeOffset? LockedUntil { get; private set; }
}
