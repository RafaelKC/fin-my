using FinMy.Application.Abstractions;
using FinMy.Application.Auth;
using FinMy.Application.Profile;
using FinMy.Domain.Identity;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Infrastructure.Auth;

/// <summary>Grava as alterações de perfil: nome do usuário, senha e nome do Espaço.</summary>
public sealed class ProfileStore(FinMyDbContext db) : IProfileStore
{
    public async Task<MeResult?> RenameUserAsync(
        Guid userId, string name, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        user.Rename(name, now);
        await db.SaveChangesAsync(cancellationToken);

        var tenant = await db.Set<TenantMember>()
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .Join(db.Set<Tenant>().AsNoTracking(), m => m.TenantId, t => t.Id, (m, t) => new TenantSummary(t.Id, t.Name, m.Role))
            .SingleOrDefaultAsync(cancellationToken);

        return tenant is null ? null : new MeResult(new UserSummary(user.Id, user.Name, user.Email), tenant);
    }

    public Task<string?> GetPasswordHashAsync(Guid userId, CancellationToken cancellationToken)
        => db.Set<User>().AsNoTracking().Where(u => u.Id == userId).Select(u => (string?)u.PasswordHash).SingleOrDefaultAsync(cancellationToken);

    public async Task ChangePasswordAsync(Guid userId, string newPasswordHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().SingleAsync(u => u.Id == userId, cancellationToken);
        user.ChangePassword(newPasswordHash, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<TenantSummary?> RenameTenantAsync(Guid tenantId, Guid userId, string name, CancellationToken cancellationToken)
    {
        var member = await db.Set<TenantMember>().AsNoTracking()
            .SingleOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == userId, cancellationToken);
        if (member is null)
        {
            return null;
        }

        var tenant = await db.Set<Tenant>().SingleAsync(t => t.Id == tenantId, cancellationToken);
        tenant.Rename(name);
        await db.SaveChangesAsync(cancellationToken);
        return new TenantSummary(tenant.Id, tenant.Name, member.Role);
    }
}
