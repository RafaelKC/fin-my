using FinMy.Application.Abstractions;
using FinMy.Application.Auth;
using FinMy.Domain.Common;
using FinMy.Domain.Identity;

namespace FinMy.Application.Tests.Auth;

public class RefreshHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(RefreshRotationStatus.Invalid, "session_expired")]
    [InlineData(RefreshRotationStatus.Expired, "session_expired")]
    [InlineData(RefreshRotationStatus.Revoked, "session_revoked")]
    [InlineData(RefreshRotationStatus.ReuseDetected, "refresh_reused")]
    public async Task Cada_resultado_de_rotacao_falho_vira_o_codigo_401_do_contrato(RefreshRotationStatus status, string code)
    {
        var handler = new RefreshHandler(new FakeStore(new RefreshRotation(status, Guid.NewGuid())), new FakeReader(null), null!);

        var ex = await Should.ThrowAsync<DomainException>(() => handler.HandleAsync("token", default));

        ex.Error.Code.ShouldBe(code);
        ex.Error.Kind.ShouldBe(DomainErrorKind.Unauthorized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Sem_token_nao_chega_a_consultar_o_banco(string? token)
    {
        var store = new FakeStore(new RefreshRotation(RefreshRotationStatus.Rotated));
        var handler = new RefreshHandler(store, new FakeReader(null), null!);

        var ex = await Should.ThrowAsync<DomainException>(() => handler.HandleAsync(token, default));

        ex.Error.Code.ShouldBe("session_expired");
        store.Rotations.ShouldBe(0);
    }

    [Fact]
    public async Task Sem_conta_a_sessao_renovada_e_encerrada()
    {
        var issued = new IssuedRefreshToken("novo", Guid.NewGuid(), Now.AddDays(90));
        var store = new FakeStore(new RefreshRotation(RefreshRotationStatus.Rotated, Guid.NewGuid(), issued));
        var handler = new RefreshHandler(store, new FakeReader(null), null!);

        var ex = await Should.ThrowAsync<DomainException>(() => handler.HandleAsync("token", default));

        ex.Error.Code.ShouldBe("session_revoked");
        store.RevokedSessions.ShouldBe([issued.SessionId]);
    }

    [Fact]
    public async Task Logout_sem_token_nao_toca_o_store_e_com_token_revoga_com_motivo_logout()
    {
        var store = new FakeStore(new RefreshRotation(RefreshRotationStatus.Rotated));
        var handler = new LogoutHandler(store);

        await handler.HandleAsync(null, default);
        store.RevokedByToken.ShouldBeEmpty();

        await handler.HandleAsync("abc", default);
        store.RevokedByToken.ShouldBe([("abc", RefreshTokenRevocationReason.Logout)]);
    }

    private sealed class FakeReader(LoginAccount? account) : ILoginAccountReader
    {
        public Task<LoginAccount?> FindAsync(string emailNormalized, CancellationToken cancellationToken) => Task.FromResult(account);

        public Task<LoginAccount?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(account);
    }

    private sealed class FakeStore(RefreshRotation rotation) : IRefreshTokenStore
    {
        public int Rotations { get; private set; }

        public List<Guid> RevokedSessions { get; } = [];

        public List<(string Token, RefreshTokenRevocationReason Reason)> RevokedByToken { get; } = [];

        public Task<IssuedRefreshToken> IssueAsync(Guid userId, Guid? sessionId, string? deviceLabel, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<RefreshRotation> RotateAsync(string token, CancellationToken cancellationToken)
        {
            Rotations++;
            return Task.FromResult(rotation);
        }

        public Task RevokeSessionAsync(Guid sessionId, RefreshTokenRevocationReason reason, CancellationToken cancellationToken)
        {
            RevokedSessions.Add(sessionId);
            return Task.CompletedTask;
        }

        public Task RevokeSessionByTokenAsync(string token, RefreshTokenRevocationReason reason, CancellationToken cancellationToken)
        {
            RevokedByToken.Add((token, reason));
            return Task.CompletedTask;
        }

        public Task RevokeUserSessionsAsync(Guid userId, RefreshTokenRevocationReason reason, CancellationToken cancellationToken, Guid? exceptSessionId = null)
            => throw new NotSupportedException();
    }
}
