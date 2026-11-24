using FinMy.Application.Abstractions;

namespace FinMy.Application.Tests.Auth;

/// <summary>Registra as pessoas cujas autorizações OAuth foram revogadas.</summary>
internal sealed class RecordingOAuthRevoker : IOAuthAuthorizationRevoker
{
    public List<Guid> Revoked { get; } = [];

    public Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken)
    {
        Revoked.Add(userId);
        return Task.CompletedTask;
    }
}
