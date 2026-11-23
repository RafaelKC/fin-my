using FinMy.Application.Abstractions;
using FinMy.Domain.Common;
using FinMy.Domain.Identity;
using FinMy.Infrastructure.Context;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FinMy.Infrastructure.Auth;

/// <summary>Grava Usuário, Espaço e Participação num único <c>SaveChanges</c> (uma transação).</summary>
public sealed class AccountStore(FinMyDbContext db, CurrentContext currentContext) : IAccountStore
{
    private const string UniqueViolation = "23505";

    public async Task<AccountCreation> CreateAsync(User user, Tenant tenant, TenantMember member, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(member);

        // Cadastro é anônimo, mas o AuditInterceptor exige autor: a nova pessoa e seu Espaço passam a ser o contexto.
        currentContext.Populate(user.Id, tenant.Id, Channel.App);

        db.Add(user);
        db.Add(tenant);
        db.Add(member);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return AccountCreation.Created;
        }
        catch (DbUpdateException ex) when (IsEmailViolation(ex))
        {
            return AccountCreation.EmailAlreadyRegistered;
        }
    }

    private static bool IsEmailViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: UniqueViolation, ConstraintName: { } name }
           && name.Contains("email_normalized", StringComparison.Ordinal);
}
