using FinMy.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace FinMy.Infrastructure.Jobs;

/// <summary>Implementação de <see cref="ITenantJobRunner"/>: um escopo de DI e um escopo de sistema por Espaço.</summary>
public sealed class TenantJobRunner(IServiceScopeFactory scopeFactory) : ITenantJobRunner
{
    public async Task ForEachTenantAsync(Func<IServiceProvider, CancellationToken, Task> work, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Guid> tenantIds;
        using (var listScope = scopeFactory.CreateScope())
        {
            tenantIds = await listScope.ServiceProvider.GetRequiredService<ITenantIdSource>()
                .ListTenantIdsAsync(cancellationToken);
        }

        var failures = new List<Exception>();
        foreach (var tenantId in tenantIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var scope = scopeFactory.CreateScope();
                using var systemScope = scope.ServiceProvider.GetRequiredService<ICurrentContext>().BeginSystemScope(tenantId);
                await work(scope.ServiceProvider, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add(ex);
            }
        }

        if (failures.Count > 0)
        {
            throw new AggregateException("Falha ao executar o job em um ou mais Espaços.", failures);
        }
    }
}
