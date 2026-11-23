using FinMy.Application.Abstractions;
using FinMy.Domain.Common;
using FinMy.Domain.Identity;
using FinMy.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace FinMy.Api.Tests.Support;

/// <summary>Sessão de teste: o pedido do token (com o <c>sid</c> real) e o access token já emitido.</summary>
public sealed record TestSession(AccessTokenRequest Request, string AccessToken, string RefreshToken)
{
    public Guid SessionId => Request.SessionId;
}

/// <summary>Cria sessões ativas direto no banco do teste (usuário + refresh token), sem passar pelos endpoints de auth.</summary>
public static class SessionHelpers
{
    public static async Task<TestSession> CreateSessionAsync(this ApiFactory factory, Channel channel = Channel.App, Guid? tenantId = null)
    {
        ArgumentNullException.ThrowIfNull(factory);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMyDbContext>();
        var user = new User("Maria Teste", $"maria.{Guid.NewGuid():N}@exemplo.com", "hash", DateTimeOffset.UtcNow);
        db.Add(user);
        await db.SaveChangesAsync();

        var refresh = await scope.ServiceProvider.GetRequiredService<IRefreshTokenStore>()
            .IssueAsync(user.Id, null, null, CancellationToken.None);

        var request = new AccessTokenRequest(user.Id, tenantId ?? Guid.NewGuid(), refresh.SessionId, user.Name, channel);
        var token = scope.ServiceProvider.GetRequiredService<ITokenService>().IssueAccessToken(request).Token;
        return new TestSession(request, token, refresh.Token);
    }

    /// <summary>Encerra a sessão como o logout faz (revoga os refresh tokens e invalida o cache).</summary>
    public static async Task RevokeSessionAsync(this ApiFactory factory, Guid sessionId)
    {
        ArgumentNullException.ThrowIfNull(factory);

        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IRefreshTokenStore>()
            .RevokeSessionAsync(sessionId, RefreshTokenRevocationReason.Logout, CancellationToken.None);
    }
}
