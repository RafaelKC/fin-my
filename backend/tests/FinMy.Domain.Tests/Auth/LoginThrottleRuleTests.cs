using FinMy.Domain.Auth;
using FinMy.Domain.Identity;

namespace FinMy.Domain.Tests.Auth;

public class LoginThrottleRuleTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);

    private static LoginThrottleState? FailTimes(int times, DateTimeOffset start, TimeSpan step)
    {
        LoginThrottleState? state = null;
        for (var i = 0; i < times; i++)
        {
            state = LoginThrottleRule.RegisterFailure(state, start + (step * i));
        }

        return state;
    }

    [Fact]
    public void Primeira_falha_abre_a_janela_e_conta_um_sem_bloquear()
    {
        var state = LoginThrottleRule.RegisterFailure(null, T0);

        state.FailedCount.ShouldBe(1);
        state.WindowStartedAt.ShouldBe(T0);
        state.LockedUntil.ShouldBeNull();
    }

    [Fact]
    public void Quatro_falhas_nao_bloqueiam()
    {
        var state = FailTimes(4, T0, TimeSpan.FromSeconds(10))!.Value;

        state.FailedCount.ShouldBe(4);
        state.LockedUntil.ShouldBeNull();
        LoginThrottleRule.RemainingLock(state, T0.AddMinutes(1)).ShouldBeNull();
    }

    [Fact]
    public void A_quinta_falha_bloqueia_por_15_minutos_a_partir_dela()
    {
        var fifth = T0.AddMinutes(2);
        var state = LoginThrottleRule.RegisterFailure(FailTimes(4, T0, TimeSpan.FromSeconds(10)), fifth);

        state.FailedCount.ShouldBe(5);
        state.LockedUntil.ShouldBe(fifth.AddMinutes(15));
    }

    [Fact]
    public void Durante_o_bloqueio_informa_o_tempo_restante()
    {
        var state = FailTimes(5, T0, TimeSpan.Zero)!.Value;

        LoginThrottleRule.RemainingLock(state, T0).ShouldBe(TimeSpan.FromMinutes(15));
        LoginThrottleRule.RemainingLock(state, T0.AddMinutes(10)).ShouldBe(TimeSpan.FromMinutes(5));
        LoginThrottleRule.RemainingLock(state, T0.AddMinutes(15).AddTicks(-1)).ShouldNotBeNull();
    }

    [Fact]
    public void No_instante_do_fim_do_bloqueio_ele_ja_acabou()
    {
        var state = FailTimes(5, T0, TimeSpan.Zero)!.Value;

        LoginThrottleRule.RemainingLock(state, T0.AddMinutes(15)).ShouldBeNull();
        LoginThrottleRule.RemainingLock(state, T0.AddHours(1)).ShouldBeNull();
    }

    [Fact]
    public void Sem_registro_nao_ha_bloqueio()
        => LoginThrottleRule.RemainingLock(null, T0).ShouldBeNull();

    [Fact]
    public void Falha_depois_do_bloqueio_vencido_recomeca_a_contagem()
    {
        var locked = FailTimes(5, T0, TimeSpan.Zero);

        var state = LoginThrottleRule.RegisterFailure(locked, T0.AddMinutes(16));

        state.FailedCount.ShouldBe(1);
        state.WindowStartedAt.ShouldBe(T0.AddMinutes(16));
        state.LockedUntil.ShouldBeNull();
    }

    [Fact]
    public void Janela_com_mais_de_15_minutos_zera_antes_de_contar()
    {
        var old = FailTimes(4, T0, TimeSpan.FromSeconds(1));

        var state = LoginThrottleRule.RegisterFailure(old, T0.AddMinutes(15).AddSeconds(1));

        state.FailedCount.ShouldBe(1);
        state.WindowStartedAt.ShouldBe(T0.AddMinutes(15).AddSeconds(1));
        state.LockedUntil.ShouldBeNull();
    }

    [Fact]
    public void Janela_de_exatamente_15_minutos_ainda_vale()
    {
        var old = FailTimes(4, T0, TimeSpan.FromSeconds(1));

        var state = LoginThrottleRule.RegisterFailure(old, T0.AddMinutes(15));

        state.FailedCount.ShouldBe(5);
        state.LockedUntil.ShouldBe(T0.AddMinutes(30));
    }

    [Fact]
    public void Cinco_falhas_espalhadas_por_mais_de_15_minutos_nao_bloqueiam()
    {
        var state = FailTimes(5, T0, TimeSpan.FromMinutes(4))!.Value; // 0, 4, 8, 12, 16 min

        state.FailedCount.ShouldBe(1);
        state.LockedUntil.ShouldBeNull();
    }

    [Fact]
    public void Falha_durante_o_bloqueio_nao_o_prolonga()
    {
        var locked = FailTimes(5, T0, TimeSpan.Zero)!.Value;

        var again = LoginThrottleRule.RegisterFailure(locked, T0.AddMinutes(5));

        again.LockedUntil.ShouldBe(locked.LockedUntil);
        again.FailedCount.ShouldBe(6);
    }

    [Fact]
    public void Entidade_guarda_e_devolve_o_estado()
    {
        var throttle = new LoginThrottle("a@b.com", T0);
        throttle.ToState().ShouldBe(new LoginThrottleState(0, T0, null));

        var next = LoginThrottleRule.RegisterFailure(throttle.ToState(), T0);
        throttle.Apply(next);

        throttle.FailedCount.ShouldBe(1);
        throttle.ToState().ShouldBe(next);
    }
}
