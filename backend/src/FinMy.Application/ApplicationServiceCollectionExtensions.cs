using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace FinMy.Application;

public static class ApplicationServiceCollectionExtensions
{
    private const string HandlerSuffix = "Handler";

    /// <summary>Registra os casos de uso (handlers) da camada Application (research R8).</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
        => services.AddHandlersFrom(typeof(ApplicationServiceCollectionExtensions).Assembly)
            .AddScoped<Auth.PasswordResetMailer>()
            .AddScoped<Catalog.CatalogResolver>()
            .AddScoped<Catalog.CreationDefaults>();

    /// <summary>
    /// Registra por convenção, como escopo da requisição, toda classe concreta e pública cujo nome termina
    /// em <c>Handler</c> (ex.: <c>SignUpHandler</c>). Sem MediatR: o endpoint injeta o handler diretamente.
    /// </summary>
    public static IServiceCollection AddHandlersFrom(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        foreach (var type in assembly.GetExportedTypes())
        {
            if (type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
                && type.Name.EndsWith(HandlerSuffix, StringComparison.Ordinal))
            {
                services.AddScoped(type);
            }
        }

        return services;
    }
}
