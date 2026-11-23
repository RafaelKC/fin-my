using FinMy.Domain.Identity;

namespace FinMy.Domain.Tests.Identity;

public class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    private static RefreshToken Novo() => new(Guid.NewGuid(), Guid.NewGuid(), [1, 2, 3], "iPhone", Now, Now.AddDays(90));

    [Fact]
    public void Token_novo_esta_ativo_ate_expirar()
    {
        var token = Novo();

        token.IsActive(Now).ShouldBeTrue();
        token.IsActive(Now.AddDays(90).AddSeconds(-1)).ShouldBeTrue();
        token.IsActive(Now.AddDays(90)).ShouldBeFalse();
    }

    [Fact]
    public void Revogar_registra_instante_e_motivo_e_desativa()
    {
        var token = Novo();

        token.Revoke(RefreshTokenRevocationReason.Logout, Now.AddHours(1));

        token.RevokedAt.ShouldBe(Now.AddHours(1));
        token.RevokedReason.ShouldBe(RefreshTokenRevocationReason.Logout);
        token.IsActive(Now.AddHours(2)).ShouldBeFalse();
    }

    [Fact]
    public void Revogar_de_novo_nao_sobrescreve_o_primeiro_motivo()
    {
        var token = Novo();
        token.Revoke(RefreshTokenRevocationReason.Logout, Now.AddHours(1));

        token.Revoke(RefreshTokenRevocationReason.PasswordReset, Now.AddHours(2));

        token.RevokedAt.ShouldBe(Now.AddHours(1));
        token.RevokedReason.ShouldBe(RefreshTokenRevocationReason.Logout);
    }

    [Fact]
    public void Marcar_como_trocado_guarda_o_substituto()
    {
        var token = Novo();
        var substituto = Guid.NewGuid();

        token.MarkRotated(substituto, Now.AddMinutes(5));

        token.RevokedReason.ShouldBe(RefreshTokenRevocationReason.Rotated);
        token.ReplacedById.ShouldBe(substituto);
        token.IsActive(Now.AddMinutes(6)).ShouldBeFalse();
    }
}
