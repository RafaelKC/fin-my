using FinMy.Infrastructure.Time;

namespace FinMy.Application.Tests.Time;

public class SystemClockTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static SystemClock At(string utc) =>
        new(new FixedTimeProvider(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Today_antes_das_03h_UTC_ainda_e_o_dia_anterior_em_BRT()
    {
        At("2026-03-10T02:59:59Z").Today.ShouldBe(new DateOnly(2026, 3, 9));
    }

    [Fact]
    public void Today_vira_o_dia_as_03h_UTC_em_BRT()
    {
        At("2026-03-10T03:00:00Z").Today.ShouldBe(new DateOnly(2026, 3, 10));
    }

    [Fact]
    public void Today_vira_o_ano_em_BRT()
    {
        At("2027-01-01T02:59:59Z").Today.ShouldBe(new DateOnly(2026, 12, 31));
        At("2027-01-01T03:00:00Z").Today.ShouldBe(new DateOnly(2027, 1, 1));
    }

    [Fact]
    public void UtcNow_devolve_o_instante_em_UTC()
    {
        At("2026-03-10T12:00:00Z").UtcNow.ShouldBe(DateTimeOffset.Parse("2026-03-10T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Construtor_padrao_usa_o_relogio_real()
    {
        new SystemClock().UtcNow.ShouldBeInRange(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
    }
}
