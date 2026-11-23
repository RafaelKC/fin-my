using FinMy.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FinMy.Infrastructure.Persistence;

/// <summary>
/// Usada apenas por <c>dotnet ef</c> (migrações). Não conecta ao banco ao gerar migrações;
/// a conexão pode ser trocada por <c>FINMY_DESIGN_CONNECTION</c> (ex.: <c>dotnet ef database update</c>).
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FinMyDbContext>
{
    public FinMyDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("FINMY_DESIGN_CONNECTION")
            ?? "Host=localhost;Database=finmy;Username=postgres;Password=postgres";

        var builder = new DbContextOptionsBuilder<FinMyDbContext>();
        builder.UseFinMyNpgsql(connection);
        return new FinMyDbContext(builder.Options, new CurrentContext());
    }
}
