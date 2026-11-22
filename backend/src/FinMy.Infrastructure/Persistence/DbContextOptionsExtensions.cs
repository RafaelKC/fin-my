using Microsoft.EntityFrameworkCore;

namespace FinMy.Infrastructure.Persistence;

public static class DbContextOptionsExtensions
{
    /// <summary>PostgreSQL com nomes em <c>snake_case</c> (docs/arquitetura.md §2).</summary>
    public static DbContextOptionsBuilder UseFinMyNpgsql(this DbContextOptionsBuilder builder, string connectionString)
        => builder.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();
}
