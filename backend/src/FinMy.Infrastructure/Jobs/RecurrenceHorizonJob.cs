using FinMy.Application.Abstractions;
using FinMy.Application.Recurrences;
using Microsoft.Extensions.DependencyInjection;

namespace FinMy.Infrastructure.Jobs;

/// <summary>
/// Job diário (005 research R2): para cada Espaço, cria as ocorrências que faltam até dezembro do ano seguinte ao de hoje.
/// A virada do ano estende o horizonte sozinha; sem meses faltando, não grava nada (idempotente).
/// </summary>
public sealed class RecurrenceHorizonJob(ITenantJobRunner runner)
{
    public const string JobId = "recurrence-horizon";

    public Task RunAsync(CancellationToken cancellationToken = default)
        => runner.ForEachTenantAsync(
            (services, token) => services.GetRequiredService<RecurrenceGenerator>().GenerateAllAsync(token),
            cancellationToken);

    /// <summary>Informa a recorrência (diária, 03:00 de Brasília) a quem agenda, sem depender do Hangfire.</summary>
    public static void Describe(Action<string, string, TimeZoneInfo?> register)
        => register(JobId, "0 3 * * *", TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));
}
