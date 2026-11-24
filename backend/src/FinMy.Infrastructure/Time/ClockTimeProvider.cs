using FinMy.Application.Abstractions;

namespace FinMy.Infrastructure.Time;

/// <summary><see cref="TimeProvider"/> sobre o <see cref="IClock"/> do app: bibliotecas que pedem um <c>TimeProvider</c> (OpenIddict) usam o mesmo relógio.</summary>
public sealed class ClockTimeProvider(IClock clock) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => clock.UtcNow;
}
