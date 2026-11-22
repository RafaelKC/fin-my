using FinMy.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace FinMy.Infrastructure.Persistence;

/// <summary>
/// Contexto do EF Core do Fin-My. Os filtros de Espaço e de exclusão lógica leem o
/// <see cref="ICurrentContext"/> a cada consulta (fail-closed: sem Espaço, nenhuma linha).
/// </summary>
public class FinMyDbContext : DbContext
{
    private readonly ICurrentContext _currentContext;

    public FinMyDbContext(DbContextOptions<FinMyDbContext> options, ICurrentContext currentContext)
        : base(options)
    {
        _currentContext = currentContext;
    }

    protected FinMyDbContext(DbContextOptions options, ICurrentContext currentContext)
        : base(options)
    {
        _currentContext = currentContext;
    }

    /// <summary>Contexto ambiente da requisição/job, lido pelos interceptors de gravação.</summary>
    public ICurrentContext CurrentContext => _currentContext;

    /// <summary>Espaço do contexto ou <see cref="Guid.Empty"/>; referenciado pelo filtro nomeado <c>Tenant</c>.</summary>
    public Guid TenantIdOrEmpty => _currentContext.TenantIdOrEmpty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FinMyDbContext).Assembly);
        modelBuilder.ApplyFinMyConventions(this);
    }
}
