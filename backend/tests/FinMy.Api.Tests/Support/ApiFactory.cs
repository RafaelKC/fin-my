using FinMy.Api.Endpoints;
using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Email;
using FinMy.Infrastructure.Persistence;
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

            foreach (var module in extraEndpoints)
            {
                services.AddSingleton(module);
            }
        });
    }

    /// <summary>Aplica as migrações reais no banco do teste (o mesmo caminho do comando <c>migrate</c>).</summary>
    public async Task MigrateAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<FinMyDbContext>().Database.MigrateAsync();
    }
}
