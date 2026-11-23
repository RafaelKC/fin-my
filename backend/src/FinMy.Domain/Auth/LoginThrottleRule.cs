namespace FinMy.Domain.Auth;

/// <summary>Estado do contador de tentativas de um e-mail (<c>failed_count</c>, <c>window_started_at</c>, <c>locked_until</c>).</summary>
public readonly record struct LoginThrottleState(int FailedCount, DateTimeOffset WindowStartedAt, DateTimeOffset? LockedUntil);

/// <summary>
/// Bloqueio por tentativas de login (research R5, FR-010): 5 falhas dentro de uma janela de 15 min bloqueiam o
/// e-mail por 15 min. Regra pura: recebe o estado e o instante e devolve o novo estado, sem I/O.
/// </summary>
public static class LoginThrottleRule
{
    public const int MaxFailures = 5;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    /// <summary>Tempo que falta para o bloqueio acabar, ou <c>null</c> se o e-mail não está bloqueado em <paramref name="now"/>.</summary>
    public static TimeSpan? RemainingLock(LoginThrottleState? state, DateTimeOffset now)
        => state is { LockedUntil: { } until } && until > now ? until - now : null;

    /// <summary>Estado depois de mais uma falha em <paramref name="now"/> (a janela velha ou o bloqueio vencido zeram antes de contar).</summary>
    public static LoginThrottleState RegisterFailure(LoginThrottleState? state, DateTimeOffset now)
    {
        var current = state is { } s && !IsStale(s, now) ? s : new LoginThrottleState(0, now, null);

        var failed = current.FailedCount + 1;
        var lockedUntil = current.LockedUntil ?? (failed >= MaxFailures ? now + LockDuration : null);

        return current with { FailedCount = failed, LockedUntil = lockedUntil };
    }

    private static bool IsStale(LoginThrottleState state, DateTimeOffset now)
        => now - state.WindowStartedAt > Window || (state.LockedUntil is { } until && until <= now);
}
