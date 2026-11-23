using FinMy.Domain.Common;
using FinMy.Infrastructure.Context;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace FinMy.Architecture.Tests;

/// <summary>docs/arquitetura.md §3.2: toda entidade mapeada é <see cref="ITenantEntity"/>, tabela de ligação (<see cref="ITenantLink"/>) ou está na lista global.</summary>
public class TenantEntityMappingTests
{
    /// <summary>Entidades globais (não pertencem a Espaço): users, tenants, tenant_members, refresh_tokens, password_reset_tokens, login_throttles, idempotency_keys (esta tem filtro de Espaço próprio, via ITenantScoped).</summary>
    public static readonly IReadOnlySet<string> GlobalEntities = new HashSet<string>
    {
        "User",
        "Tenant",
        "TenantMember",
        "RefreshToken",
        "PasswordResetToken",
        "LoginThrottle",
        "IdempotencyKey",
    };

    [Fact]
    public void Toda_entidade_mapeada_e_ITenantEntity_ou_esta_na_lista_global()
    {
        // O modelo é construído sem abrir conexão; entidades de teste (sondas) ficam fora, pois vivem em outro assembly.
        var options = new DbContextOptionsBuilder<FinMyDbContext>();
        options.UseFinMyNpgsql("Host=localhost;Database=arquitetura");
        using var db = new FinMyDbContext(options.Options, new CurrentContext());

        Violacoes(db.Model.GetEntityTypes()).ShouldBeEmpty();
    }

    [Fact]
    public void Regra_aponta_entidade_que_nao_e_de_Espaco_nem_global()
    {
        var tipos = new[] { typeof(DeEspaco), typeof(User), typeof(SemMarcacao) };

        Violacoes(tipos).ShouldBe([nameof(SemMarcacao)]);
    }

    [Fact]
    public void Tabela_de_ligacao_ITenantLink_e_aceita()
    {
        Violacoes([typeof(DeEspaco), typeof(Ligacao)]).ShouldBeEmpty();
    }

    [Fact]
    public void Entidade_global_que_implementa_ITenantEntity_continua_valida()
    {
        Violacoes([typeof(DeEspaco), typeof(Tenant)]).ShouldBeEmpty();
    }

    private static IEnumerable<string> Violacoes(IEnumerable<IEntityType> entityTypes) =>
        Violacoes(entityTypes.Where(t => !t.IsOwned()).Select(t => t.ClrType));

    private static IEnumerable<string> Violacoes(IEnumerable<Type> clrTypes) =>
        clrTypes
            .Where(t => !typeof(ITenantEntity).IsAssignableFrom(t) && !typeof(ITenantLink).IsAssignableFrom(t) && !GlobalEntities.Contains(t.Name))
            .Select(t => t.Name);

    private sealed class DeEspaco : ITenantEntity
    {
        public Guid Id { get; init; }

        public Guid TenantId { get; init; }
    }

    private sealed class Ligacao : ITenantLink
    {
        public Guid TenantId { get; init; }
    }

    private sealed class SemMarcacao : IEntity
    {
        public Guid Id { get; init; }
    }

    private sealed class User;

    private sealed class Tenant : ITenantEntity
    {
        public Guid Id { get; init; }

        public Guid TenantId { get; init; }
    }
}
