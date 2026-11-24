using FinMy.Application.Abstractions;
using OpenIddict.Abstractions;

namespace FinMy.Infrastructure.Auth.OAuth;

/// <summary>Revoga, no OpenIddict, as autorizações e os tokens (<c>sub</c> = id da pessoa); a validação consulta o banco a cada chamada.</summary>
public sealed class OAuthAuthorizationRevoker(IOpenIddictAuthorizationManager authorizations, IOpenIddictTokenManager tokens) : IOAuthAuthorizationRevoker
{
    public async Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken)
    {
        var subject = userId.ToString();
        await authorizations.RevokeBySubjectAsync(subject, cancellationToken);
        await tokens.RevokeBySubjectAsync(subject, cancellationToken);
    }
}
