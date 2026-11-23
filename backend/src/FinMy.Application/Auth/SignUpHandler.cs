using FinMy.Application.Abstractions;
using FinMy.Domain.Common;
using FinMy.Domain.Identity;
using FinMy.Domain.Tenants;
using FluentValidation;

namespace FinMy.Application.Auth;

/// <summary>
/// Cria a conta: valida a entrada, grava Usuário + Espaço + Participação (<c>owner</c>) numa transação e já
/// abre a sessão (refresh token + access token). E-mail repetido → 409 <c>email_already_registered</c> (research R7).
/// </summary>
public sealed class SignUpHandler(
    IAccountStore accounts,
    IPasswordHasher passwordHasher,
    IRefreshTokenStore refreshTokens,
    ITokenService tokens,
    IClock clock)
{
    public const string EmailAlreadyRegisteredCode = "email_already_registered";

    private static readonly SignUpValidator Validator = new();

    public static readonly DomainError EmailAlreadyRegistered = new(
        EmailAlreadyRegisteredCode,
        "Este e-mail já tem conta. Entrar ou recuperar senha?",
        DomainErrorKind.Conflict);

    public async Task<SignUpResult> HandleAsync(SignUpCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        await Validator.ValidateAndThrowAsync(command, cancellationToken);

        var user = new User(command.Name!, command.Email!, passwordHasher.Hash(command.Password!), clock.UtcNow);
        var tenant = new Tenant(TenantNaming.DefaultName(user.Name));
        var member = new TenantMember(tenant.Id, user.Id, clock.UtcNow);

        if (await accounts.CreateAsync(user, tenant, member, cancellationToken) == AccountCreation.EmailAlreadyRegistered)
        {
            throw new DomainException(EmailAlreadyRegistered);
        }

        var refresh = await refreshTokens.IssueAsync(user.Id, null, command.DeviceLabel, cancellationToken);
        var access = tokens.IssueAccessToken(new AccessTokenRequest(user.Id, tenant.Id, refresh.SessionId, user.Name, Channel.App));

        return new SignUpResult(
            access.Token,
            access.ExpiresAt,
            refresh.Token,
            new UserSummary(user.Id, user.Name, user.Email),
            new TenantSummary(tenant.Id, tenant.Name, member.Role));
    }
}
