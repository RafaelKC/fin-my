using FinMy.Application.Abstractions;
using FinMy.Domain.Identity;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Infrastructure.Auth;

/// <summary>Busca usuário, Espaço e participação pelo e-mail normalizado (tabelas globais, sem filtro de Espaço).</summary>
public sealed class LoginAccountReader(FinMyDbContext db) : ILoginAccountReader
{
    public async Task<LoginAccount?> FindAsync(string emailNormalized, CancellationToken cancellationToken)
    {
        var row = await (
            from user in db.Set<User>().AsNoTracking()
            join member in db.Set<TenantMember>().AsNoTracking() on user.Id equals member.UserId
            join tenant in db.Set<Tenant>().AsNoTracking() on member.TenantId equals tenant.Id
            where user.EmailNormalized == emailNormalized
            select new LoginAccount(user, tenant, member))
            .FirstOrDefaultAsync(cancellationToken);

        return row;
    }

    public async Task<LoginAccount?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var row = await (
            from user in db.Set<User>().AsNoTracking()
            join member in db.Set<TenantMember>().AsNoTracking() on user.Id equals member.UserId
            join tenant in db.Set<Tenant>().AsNoTracking() on member.TenantId equals tenant.Id
            where user.Id == userId
            select new LoginAccount(user, tenant, member))
            .FirstOrDefaultAsync(cancellationToken);

        return row;
    }
}
