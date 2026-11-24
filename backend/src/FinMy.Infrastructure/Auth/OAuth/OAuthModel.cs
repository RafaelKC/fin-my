using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;

namespace FinMy.Infrastructure.Auth.OAuth;

/// <summary>Tabelas do OpenIddict no mesmo banco, com nomes em <c>snake_case</c> (008 data-model). Todas são globais (sem Espaço).</summary>
public static class OAuthModel
{
    /// <summary>Nomes das entidades do OpenIddict (a lista de entidades globais do teste de arquitetura).</summary>
    public static readonly IReadOnlyList<Type> EntityTypes =
    [
        typeof(OpenIddictEntityFrameworkCoreApplication),
        typeof(OpenIddictEntityFrameworkCoreAuthorization),
        typeof(OpenIddictEntityFrameworkCoreScope),
        typeof(OpenIddictEntityFrameworkCoreToken),
    ];

    public static IEnumerable<string> EntityNames => EntityTypes.Select(t => t.Name);

    public static ModelBuilder UseFinMyOpenIddict(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.UseOpenIddict();
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreApplication>().ToTable("openiddict_applications");
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreAuthorization>().ToTable("openiddict_authorizations");
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreScope>().ToTable("openiddict_scopes");
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreToken>().ToTable("openiddict_tokens");
        return modelBuilder;
    }
}
