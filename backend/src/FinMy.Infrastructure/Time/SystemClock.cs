using FinMy.Application.Abstractions;

namespace FinMy.Infrastructure.Time;

/// <summary>Relógio real; <see cref="Today"/> usa o fuso America/Sao_Paulo (RT-03).</summary>
public sealed class SystemClock(TimeProvider timeProvider) : IClock
{
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public SystemClock()
        : this(TimeProvider.System)
    {
    }

    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow, SaoPaulo).DateTime);
}
