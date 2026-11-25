using FinMy.Api.Endpoints;
using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Email;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Infrastructure.Persistence;
using FinMy.Infrastructure.Persistence.Interceptors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinMy.Api.Tests.Support;

/// <summary>
/// A API real em memória apontando para um banco PostgreSQL próprio (criado a partir do
/// <see cref="Application.Tests.Infrastructure.PostgresFixture"/>) e com <see cref="FakeEmailSender"/>.
/// Fixture reutilizável pelas fases seguintes; use <see cref="ApiTestBase"/> para o ciclo de vida.
/// </summary>
public sealed class ApiFactory(
    string connectionString,
    IReadOnlyDictionary<string, string?>? settings = null,
    IClock? clock = null,
    params IEndpointModule[] extraEndpoints)
    : WebApplicationFactory<Program>
{
    /// <summary>Chave de assinatura dos tokens nos testes (a API real exige <c>Jwt__SigningKey</c>).</summary>
    public const string TestSigningKey = "chave-de-teste-chave-de-teste-chave-de-teste-0123456789";

    /// <summary>
    /// Troca o contexto do EF pelo de testes (modelo real + sondas <c>ISoftDeletable</c>, research R1) e cria o esquema
    /// por <c>EnsureCreated</c>, para exercitar pela API as rotas que varrem o modelo (ex.: restaurar exclusão).
    /// </summary>
    public bool UseProbeModel { get; init; }

    /// <summary>E-mails "enviados" pela API durante o teste.</summary>
    public FakeEmailSender Emails { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("Hangfire:Enabled", "false");
        builder.UseSetting("Jwt:SigningKey", TestSigningKey);

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);

            if (clock is not null)
            {
                services.RemoveAll<IClock>();
                services.AddSingleton(clock);
            }

            if (UseProbeModel)
            {
                services.RemoveAll<DbContextOptions<FinMyDbContext>>();
                services.RemoveAll<DbContextOptions>();
                services.RemoveAll<FinMyDbContext>();
                services.AddDbContext<TestFinMyDbContext>((sp, options) => options
                    .UseFinMyNpgsql(connectionString)
                    .AddFinMyInterceptors(sp.GetRequiredService<IClock>()));
                services.AddScoped<FinMyDbContext>(sp => sp.GetRequiredService<TestFinMyDbContext>());
            }

            foreach (var module in extraEndpoints)
            {
                services.AddSingleton(module);
            }
        });
    }

    /// <summary>Aplica as migrações reais no banco do teste e semeia (o mesmo caminho do comando <c>migrate</c>).</summary>
    public async Task MigrateAsync()
    {
        if (UseProbeModel)
        {
            await using var scope = Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<FinMyDbContext>().Database.EnsureCreatedAsync();
            return;
        }

        await ApplyMigrationsAsync();
        await SeedAsync();
    }

    /// <summary>Só as migrações (sem semeadura): é o que vai para o banco modelo.</summary>
    public async Task ApplyMigrationsAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<FinMyDbContext>().Database.MigrateAsync();
    }

    /// <summary>
    /// Como o comando `migrate`: o cliente OAuth do app Android (009) é semeado junto. Roda por teste, com as
    /// configurações dele (ex.: <c>Android:RedirectUri</c>), por isso fica fora do banco modelo.
    /// </summary>
    public async Task SeedAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<FinMy.Infrastructure.Auth.OAuth.AndroidClientSeeder>().SeedAsync(CancellationToken.None);
    }
}
