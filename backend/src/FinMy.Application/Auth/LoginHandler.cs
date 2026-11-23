using FinMy.Application.Abstractions;
using FinMy.Domain.Auth;
using FinMy.Domain.Common;
using FinMy.Domain.Identity;

namespace FinMy.Application.Auth;

/// <summary>
/// Entra com e-mail e senha. Erro único <c>invalid_credentials</c> (FR-014) para e-mail inexistente ou senha errada,
/// com o mesmo custo de hash nos dois casos (SC-004). Cinco falhas em 15 min bloqueiam o e-mail por 15 min:
/// 429 <c>login_locked</c> com <c>retryAfterSeconds</c>, mesmo com a senha certa (research R5).
/// </summary>
public sealed class LoginHandler(
    ILoginAccountReader accounts,
    ILoginThrottleStore throttles,
    IPasswordHasher passwordHasher,
    IRefreshTokenStore refreshTokens,
    ITokenService tokens,
    IClock clock)
{
    public const string InvalidCredentialsCode = "invalid_credentials";
    public const string LoginLockedCode = "login_locked";

    public static readonly DomainError InvalidCredentials = new(
        InvalidCredentialsCode,
        "E-mail ou senha incorretos",
        DomainErrorKind.Unauthorized);

    // Hash de uma senha qualquer: conferido quando o e-mail não existe, para o tempo de resposta não revelar a conta.
    private static string? _phantomHash;

    public async Task<SignUpResult> HandleAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Email) || string.IsNullOrEmpty(command.Password))
        {
            throw new DomainException(InvalidCredentials);
        }

        var emailNormalized = User.NormalizeEmail(command.Email);

        var state = await throttles.GetAsync(emailNormalized, cancellationToken);
        if (LoginThrottleRule.RemainingLock(state, clock.UtcNow) is { } remaining)
        {
            throw Locked(remaining);
        }

        var account = await accounts.FindAsync(emailNormalized, cancellationToken);
        var check = passwordHasher.Verify(account?.User.PasswordHash ?? PhantomHash(), command.Password);

        if (account is null || check == PasswordCheck.Failed)
        {
            await throttles.RegisterFailureAsync(emailNormalized, clock.UtcNow, cancellationToken);
            throw new DomainException(InvalidCredentials);
        }

        await throttles.ResetAsync(emailNormalized, cancellationToken);

        var user = account.User;
        var refresh = await refreshTokens.IssueAsync(user.Id, null, command.DeviceLabel, cancellationToken);
        var access = tokens.IssueAccessToken(new AccessTokenRequest(user.Id, account.Tenant.Id, refresh.SessionId, user.Name, Channel.App));

        return new SignUpResult(
            access.Token,
            access.ExpiresAt,
            refresh.Token,
            new UserSummary(user.Id, user.Name, user.Email),
            new TenantSummary(account.Tenant.Id, account.Tenant.Name, account.Member.Role));
    }

    private string PhantomHash() => _phantomHash ??= passwordHasher.Hash(Guid.NewGuid().ToString("N"));

    private static DomainException Locked(TimeSpan remaining) => new(new DomainError(
        LoginLockedCode,
        "Muitas tentativas. Tente novamente em alguns minutos.",
        DomainErrorKind.TooManyRequests,
        RetryAfterSeconds: Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds))));
}
