using FinMy.Application.Abstractions;
using FinMy.Application.OAuth;
using FinMy.Domain.Identity;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace FinMy.Infrastructure.Auth.OAuth;

/// <summary>"Aplicativos conectados" sobre as autorizações permanentes do OpenIddict (<c>sub</c> = id da pessoa) e <c>oauth_authorization_usage</c>.</summary>
public sealed class ConnectedAppsStore(
    FinMyDbContext db,
    IOpenIddictAuthorizationManager authorizations,
    IOpenIddictApplicationManager applications,
    IOpenIddictTokenManager tokens,
    IClock clock) : IConnectedAppsStore
{
    /// <summary>Intervalo mínimo entre duas anotações de uso da mesma autorização.</summary>
    public static readonly TimeSpan UsageInterval = TimeSpan.FromMinutes(5);

    public async Task<IReadOnlyList<ConnectedApp>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var found = new List<(string Id, string Name, DateTimeOffset ConnectedAt)>();
        await foreach (var authorization in authorizations.FindAsync(userId.ToString(), client: null, Statuses.Valid, AuthorizationTypes.Permanent, scopes: null, cancellationToken))
        {
            var id = await authorizations.GetIdAsync(authorization, cancellationToken);
            var applicationId = await authorizations.GetApplicationIdAsync(authorization, cancellationToken);
            if (id is null || applicationId is null)
            {
                continue;
            }

            var application = await applications.FindByIdAsync(applicationId, cancellationToken);
            var name = application is null ? null : await applications.GetDisplayNameAsync(application, cancellationToken);
            var created = await authorizations.GetCreationDateAsync(authorization, cancellationToken) ?? clock.UtcNow;
            found.Add((id, string.IsNullOrWhiteSpace(name) ? "Aplicativo" : name, created));
        }

        var usage = await db.Set<OAuthAuthorizationUsage>().AsNoTracking()
            .Where(u => u.UserId == userId)
            .ToDictionaryAsync(u => u.AuthorizationId, u => u.LastUsedAt, StringComparer.Ordinal, cancellationToken);

        return [.. found
            .OrderByDescending(a => a.ConnectedAt)
            .Select(a => new ConnectedApp(a.Id, a.Name, a.ConnectedAt, usage.TryGetValue(a.Id, out var last) ? last : null))];
    }

    public async Task<bool> DisconnectAsync(Guid userId, string authorizationId, CancellationToken cancellationToken)
    {
        var authorization = await authorizations.FindByIdAsync(authorizationId, cancellationToken);
        if (authorization is null
            || await authorizations.GetSubjectAsync(authorization, cancellationToken) != userId.ToString()
            || await authorizations.GetStatusAsync(authorization, cancellationToken) != Statuses.Valid
            || await authorizations.GetTypeAsync(authorization, cancellationToken) != AuthorizationTypes.Permanent)
        {
            return false;
        }

        // A validação consulta a autorização e o token a cada chamada: revogar vale na próxima.
        await authorizations.TryRevokeAsync(authorization, cancellationToken);
        await tokens.RevokeByAuthorizationIdAsync(authorizationId, cancellationToken);
        await db.Set<OAuthAuthorizationUsage>().Where(u => u.AuthorizationId == authorizationId).ExecuteDeleteAsync(cancellationToken);
        return true;
    }

    public async Task RecordUseAsync(string authorizationId, Guid userId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var threshold = now - UsageInterval;

        // Um único comando: cria a linha ou atualiza só se a última anotação tem 5 minutos ou mais (vale entre instâncias).
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO oauth_authorization_usage (authorization_id, user_id, last_used_at)
            VALUES ({authorizationId}, {userId}, {now})
            ON CONFLICT (authorization_id) DO UPDATE SET last_used_at = EXCLUDED.last_used_at
            WHERE oauth_authorization_usage.last_used_at <= {threshold}
            """,
            cancellationToken);
    }
}
