using FinMy.Application.Abstractions;
using FinMy.Infrastructure.Auth;
using FinMy.Infrastructure.Context;
using FinMy.Infrastructure.Email;
using FinMy.Infrastructure.Jobs;
using FinMy.Infrastructure.Months;
using FinMy.Infrastructure.Persistence;
using FinMy.Infrastructure.Persistence.Interceptors;
using FinMy.Infrastructure.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FinMy.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registra relógio, contexto ambiente, <see cref="FinMyDbContext"/> (com os interceptors na ordem de
    /// docs/arquitetura.md §3.4) e o envio de e-mail. A seção <c>Email</c> é ligada a
    /// <see cref="SmtpEmailOptions"/> por quem chama (a Api), pois a Infrastructure não lê configuração.
    /// </summary>
    /// <param name="services">Coleção de serviços.</param>
    /// <param name="connectionString">Resolve a string de conexão só quando o contexto é criado (a configuração já está completa).</param>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, Func<IServiceProvider, string> connectionString)
    {
        ArgumentNullException.ThrowIfNull(connectionString);

        services.TryAddSingleton<IClock, SystemClock>();

        // Um único CurrentContext por escopo, visível como interface (EF, handlers) e como tipo concreto (middleware).
        services.AddScoped<CurrentContext>();
        services.AddScoped<ICurrentContext>(sp => sp.GetRequiredService<CurrentContext>());

        services.AddDbContext<FinMyDbContext>((sp, options) => options
            .UseFinMyNpgsql(connectionString(sp))
            .AddFinMyInterceptors(sp.GetRequiredService<IClock>()));

        services.TryAddSingleton<IEmailSender, SmtpEmailSender>();

        // Autenticação (001): a seção `Jwt` é ligada a JwtOptions por quem chama (a Api).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>());
        services.TryAddSingleton<ITokenService, JwtTokenService>();
        services.TryAddSingleton<IPasswordHasher, PasswordHasherAdapter>();
        services.AddMemoryCache();
        services.AddScoped<ISessionRegistry, SessionRegistry>();
        services.AddScoped<IRefreshTokenStore, RefreshTokenStore>();
        services.AddScoped<IAccountStore, AccountStore>();
        services.AddScoped<ILoginAccountReader, LoginAccountReader>();
        services.AddScoped<ILoginThrottleStore, LoginThrottleStore>();
        services.AddScoped<IPasswordResetStore, PasswordResetStore>();
        services.AddScoped<IProfileStore, ProfileStore>();
        services.AddScoped<IDeletionStore, DeletionStore>();
        services.AddScoped<IWalletStore, WalletStore>();
        services.AddScoped<ITransferStore, TransferStore>();
        services.AddScoped<ICreditCardStore, CreditCardStore>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<IMonthReader, MonthReader>();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddScoped<IEntryStore, EntryStore>();
        services.AddScoped<ILedgerSearchStore, LedgerSearchStore>();

        // Envio do e-mail de redefinição: na própria requisição (padrão); AddFinMyHangfire troca por job em segundo plano.
        services.TryAddScoped<IPasswordResetDispatcher, InlinePasswordResetDispatcher>();

        return services;
    }
}
