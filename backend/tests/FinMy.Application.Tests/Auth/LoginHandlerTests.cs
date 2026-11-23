using FinMy.Application.Abstractions;
using FinMy.Application.Auth;
using FinMy.Application.Tests.Infrastructure;
using FinMy.Domain.Auth;
using FinMy.Domain.Common;
using FinMy.Domain.Identity;

namespace FinMy.Application.Tests.Auth;

public class LoginHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Email_inexistente_confere_a_senha_contra_um_hash_fantasma_e_conta_a_falha()
    {
        var hasher = new RecordingHasher();
        var throttles = new FakeThrottles();
        var handler = Handler(hasher, throttles, account: null);

        var ex = await Should.ThrowAsync<DomainException>(() => handler.HandleAsync(new LoginCommand("ninguem@exemplo.com", "qualquer"), default));

        ex.Error.Code.ShouldBe("invalid_credentials");
        hasher.VerifyCalls.ShouldBe(1); // mesmo custo de hash de uma conta real
        throttles.Failures.ShouldBe(["ninguem@exemplo.com"]);
    }

    [Fact]
    public async Task Bloqueado_nao_chega_a_conferir_a_senha()
    {
        var hasher = new RecordingHasher();
        var throttles = new FakeThrottles { State = new LoginThrottleState(5, Now.AddMinutes(-1), Now.AddSeconds(90.5)) };
        var handler = Handler(hasher, throttles, account: null);

        var ex = await Should.ThrowAsync<DomainException>(() => handler.HandleAsync(new LoginCommand("a@b.com", "x"), default));

        ex.Error.Code.ShouldBe("login_locked");
        ex.Error.Kind.ShouldBe(DomainErrorKind.TooManyRequests);
        ex.Error.RetryAfterSeconds.ShouldBe(91);
        hasher.VerifyCalls.ShouldBe(0);
        throttles.Failures.ShouldBeEmpty();
    }

    private static LoginHandler Handler(RecordingHasher hasher, FakeThrottles throttles, LoginAccount? account)
        => new(new FakeReader(account), throttles, hasher, null!, null!, new FakeClock(Now));

    private sealed class FakeReader(LoginAccount? account) : ILoginAccountReader
    {
        public Task<LoginAccount?> FindAsync(string emailNormalized, CancellationToken cancellationToken) => Task.FromResult(account);

        public Task<LoginAccount?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(account);
    }

    private sealed class FakeThrottles : ILoginThrottleStore
    {
        public LoginThrottleState? State { get; init; }

        public List<string> Failures { get; } = [];

        public Task<LoginThrottleState?> GetAsync(string emailNormalized, CancellationToken cancellationToken) => Task.FromResult(State);

        public Task<LoginThrottleState> RegisterFailureAsync(string emailNormalized, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Failures.Add(emailNormalized);
            return Task.FromResult(LoginThrottleRule.RegisterFailure(State, now));
        }

        public Task ResetAsync(string emailNormalized, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingHasher : IPasswordHasher
    {
        public int VerifyCalls { get; private set; }

        public string Hash(string password) => "hash:" + password;

        public PasswordCheck Verify(string passwordHash, string password)
        {
            VerifyCalls++;
            return PasswordCheck.Failed;
        }
    }
}
