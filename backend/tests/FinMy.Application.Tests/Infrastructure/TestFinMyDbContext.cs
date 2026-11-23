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

    public DbSet<ProbeNamed> Named => Set<ProbeNamed>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProbeParent>().ToTable("probe_parents");
        modelBuilder.Entity<ProbeItem>(b =>
        {
            b.ToTable("probe_items");
            b.HasOne<ProbeParent>().WithMany().HasTenantForeignKey(i => i.ParentId);
        });

        modelBuilder.Entity<ProbeNamed>(b =>
        {
            b.ToTable("probe_named");
            b.HasIndex("TenantId", nameof(ProbeNamed.Name)).IsUnique().HasFilter("deleted_at IS NULL");
        });

        base.OnModelCreating(modelBuilder);
    }
}
