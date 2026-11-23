using FinMy.Api.Tests.Support;
using FinMy.Application.Abstractions;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Infrastructure.Context;
using FinMy.Infrastructure.Email;
using FinMy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FinMy.Api.Tests.Composition;

public class CompositionTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    [Fact]
    public async Task Escopo_compartilha_o_mesmo_CurrentContext_entre_a_interface_e_o_tipo()
    {
        await using var scope = Factory.Services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<ICurrentContext>()
            .ShouldBeSameAs(scope.ServiceProvider.GetRequiredService<CurrentContext>());
    }

    [Fact]
    public async Task DbContext_e_clock_estao_registrados()
    {
        await using var scope = Factory.Services.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<FinMyDbContext>().Database.IsNpgsql().ShouldBeTrue();
        Factory.Services.GetRequiredService<IClock>().ShouldNotBeNull();
    }

    [Fact]
    public async Task Migracoes_reais_criam_as_tabelas_de_identidade()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMyDbContext>();

        (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        (await db.Database.GetAppliedMigrationsAsync()).ShouldNotBeEmpty();
    }
}

public class EmailRegistrationTests
{
    [Fact]
    public void IEmailSender_real_le_a_secao_Email_incluindo_UseTls()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", "Host=localhost;Database=x;Username=x;Password=x");
            builder.UseSetting("Hangfire:Enabled", "false");
            builder.UseSetting("Email:Host", "smtp.teste.local");
            builder.UseSetting("Email:Port", "587");
            builder.UseSetting("Email:UseTls", "true");
            builder.UseSetting("Email:FromAddress", "no-reply@finmy.local");
        });

        factory.Services.GetRequiredService<IEmailSender>().ShouldBeOfType<SmtpEmailSender>();
        var options = factory.Services.GetRequiredService<IOptions<SmtpEmailOptions>>().Value;
        options.Host.ShouldBe("smtp.teste.local");
        options.Port.ShouldBe(587);
        options.UseTls.ShouldBeTrue();
        options.FromAddress.ShouldBe("no-reply@finmy.local");
    }
}
