using FinMy.Application.Abstractions;
using FinMy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Application.Tests.Infrastructure;

/// <summary>
/// Contexto de testes (research R1): herda do contexto real e acrescenta as sondas, de modo que
/// filtros, convenções e FKs compostas exercitados são os de produção.
/// </summary>
public sealed class TestFinMyDbContext : FinMyDbContext
{
    public TestFinMyDbContext(DbContextOptions<TestFinMyDbContext> options, ICurrentContext currentContext)
        : base(options, currentContext)
    {
    }

    public DbSet<ProbeParent> Parents => Set<ProbeParent>();

    public DbSet<ProbeItem> Items => Set<ProbeItem>();

    /// <summary>
    /// Atribui o Espaço à entidade pelo estado do EF. Os interceptors (T010) farão isso sozinhos;
    /// até lá, os testes de banco precisam informar o Espaço por conta própria.
    /// </summary>
    public T InTenant<T>(T entity, Guid tenantId)
        where T : class
    {
        Entry(entity).Property(nameof(ProbeParent.TenantId)).CurrentValue = tenantId;
        return entity;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProbeParent>().ToTable("probe_parents");
        modelBuilder.Entity<ProbeItem>(b =>
        {
            b.ToTable("probe_items");
            b.HasOne<ProbeParent>().WithMany().HasTenantForeignKey(i => i.ParentId);
        });

        base.OnModelCreating(modelBuilder);
    }
}
