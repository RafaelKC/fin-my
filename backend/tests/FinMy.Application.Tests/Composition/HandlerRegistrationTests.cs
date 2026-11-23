using FinMy.Application;
using Microsoft.Extensions.DependencyInjection;

namespace FinMy.Application.Tests.Composition;

public class HandlerRegistrationTests
{
    [Fact]
    public void Registra_handlers_concretos_como_escopo_e_ignora_o_resto()
    {
        var services = new ServiceCollection().AddHandlersFrom(typeof(HandlerRegistrationTests).Assembly);

        services.ShouldContain(d => d.ServiceType == typeof(ExemploHandler) && d.Lifetime == ServiceLifetime.Scoped);
        services.ShouldNotContain(d => d.ServiceType == typeof(ExemploAbstratoHandler));
        services.ShouldNotContain(d => d.ServiceType == typeof(NaoEhCasoDeUso));
    }

    [Fact]
    public void AddApplication_nao_falha_sem_handlers()
    {
        Should.NotThrow(() => new ServiceCollection().AddApplication());
    }
}

public sealed class ExemploHandler;

public abstract class ExemploAbstratoHandler;

public sealed class NaoEhCasoDeUso;
