using System.Text.Json;
using FinMy.Domain.Common;
using FinMy.Domain.Idempotency;
using FinMy.Domain.Identity;
using FinMy.Infrastructure.Auth.OAuth;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Api.Tests.Isolation;

/// <summary>
/// Retrato de tudo o que pertence à conta/Espaço de A no banco ("nada gravado"): linhas de todo tipo
/// <see cref="ITenantEntity"/> e tabela de ligação (<see cref="ITenantLink"/>) do Espaço e as linhas globais ligadas a A. Tipo mapeado sem regra de dono falha
/// o teste — assim a suíte cresce sozinha com as próximas features.
/// </summary>
public static class AccountSnapshot
{
    /// <summary>Tabelas globais: como achar as linhas de A (propriedade comparada com o usuário ou o Espaço de A).</summary>
    private static readonly Dictionary<Type, (string Property, bool ByTenant)> GlobalOwners = new()
    {
        [typeof(User)] = (nameof(User.Id), false),
        [typeof(Tenant)] = (nameof(Tenant.Id), true),
        [typeof(TenantMember)] = (nameof(TenantMember.TenantId), true),
        [typeof(RefreshToken)] = (nameof(RefreshToken.UserId), false),
        [typeof(PasswordResetToken)] = (nameof(PasswordResetToken.UserId), false),
        [typeof(IdempotencyKey)] = (nameof(IdempotencyKey.TenantId), true),
        [typeof(OAuthConsentTicket)] = (nameof(OAuthConsentTicket.UserId), false),
        [typeof(OAuthAuthorizationUsage)] = (nameof(OAuthAuthorizationUsage.UserId), false),
    };

    /// <summary>Contadores por e-mail/IP e as tabelas do OpenIddict (aplicativos e autorizações do conector, 008): não têm dono por coluna; o isolamento delas é testado nos testes do OAuth.</summary>
    private static readonly HashSet<Type> Unowned = [typeof(LoginThrottle), .. OAuthModel.EntityTypes];

    public static async Task<string> TakeAsync(FinMyDbContext db, Guid userId, Guid tenantId)
    {
        var parts = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var entityType in db.Model.GetEntityTypes().Where(t => !t.IsOwned() && t.BaseType is null))
        {
            var type = entityType.ClrType;
            if (Unowned.Contains(type))
            {
                continue;
            }

            Func<object, bool> isA;
            if (typeof(ITenantEntity).IsAssignableFrom(type) || typeof(ITenantLink).IsAssignableFrom(type))
            {
                var tenantProperty = type.GetProperty(nameof(ITenantScoped.TenantId))!;
                isA = row => (Guid)tenantProperty.GetValue(row)! == tenantId;
            }
            else if (GlobalOwners.TryGetValue(type, out var owner))
            {
                var property = type.GetProperty(owner.Property)!;
                var expected = owner.ByTenant ? tenantId : userId;
                isA = row => (Guid)property.GetValue(row)! == expected;
            }
            else
            {
                throw new InvalidOperationException(
                    $"{type.Name} é uma entidade mapeada sem regra de dono em {nameof(AccountSnapshot)}: acrescente-a a {nameof(GlobalOwners)} (global) ou torne-a ITenantEntity.");
            }

            var rows = await LoadAsync(db, type);
            parts[type.Name] = rows.Where(isA)
                .Select(row => JsonSerializer.Serialize(row, type))
                .OrderBy(json => json, StringComparer.Ordinal)
                .ToList();
        }

        return JsonSerializer.Serialize(parts);
    }

    private static async Task<IReadOnlyList<object>> LoadAsync(DbContext db, Type type)
    {
        var method = typeof(AccountSnapshot).GetMethod(nameof(LoadTypedAsync), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .MakeGenericMethod(type);
        return await (Task<IReadOnlyList<object>>)method.Invoke(null, [db])!;
    }

    private static async Task<IReadOnlyList<object>> LoadTypedAsync<T>(DbContext db)
        where T : class =>
        (await db.Set<T>().IgnoreQueryFilters().AsNoTracking().ToListAsync()).Cast<object>().ToList();
}
