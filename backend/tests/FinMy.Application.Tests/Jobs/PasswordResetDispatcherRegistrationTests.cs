using FinMy.Application.Abstractions;
using FinMy.Infrastructure;
using FinMy.Infrastructure.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace FinMy.Application.Tests.Jobs;

public class PasswordResetDispatcherRegistrationTests
{
    private static Type? Dispatcher(IServiceCollection services)
        => services.Last(d => d.ServiceType == typeof(IPasswordResetDispatcher)).ImplementationType;

    [Fact]
    public void Sem_Hangfire_o_envio_e_inline()
    {
        var services = new ServiceCollection().AddInfrastructure(_ => "Host=x");

        Dispatcher(services).ShouldBe(typeof(InlinePasswordResetDispatcher));
    }

    [Fact]
    public void Com_Hangfire_o_envio_vira_job_em_segundo_plano()
    {
        var services = new ServiceCollection().AddInfrastructure(_ => "Host=x").AddFinMyHangfire("Host=x");

        Dispatcher(services).ShouldBe(typeof(HangfirePasswordResetDispatcher));
        services.ShouldContain(d => d.ServiceType == typeof(PasswordResetEmailJob));
    }
}
