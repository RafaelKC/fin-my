using FinMy.Application.Abstractions;

namespace FinMy.Application.Tests.Infrastructure;

/// <summary>Relógio controlável pelos testes.</summary>
public sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;

    public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
}
