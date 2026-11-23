using FinMy.Application.Abstractions;
using FinMy.Infrastructure.Auth;

namespace FinMy.Application.Tests.Auth;

public class PasswordHasherAdapterTests
{
    private readonly PasswordHasherAdapter _hasher = new();

    [Fact]
    public void Hash_nao_contem_a_senha_e_muda_a_cada_chamada()
    {
        var a = _hasher.Hash("senha-secreta-1");
        var b = _hasher.Hash("senha-secreta-1");

        a.ShouldNotContain("senha-secreta-1");
        a.ShouldNotBe(b);
    }

    [Fact]
    public void Senha_correta_confere_e_incorreta_falha()
    {
        var hash = _hasher.Hash("senha-secreta-1");

        _hasher.Verify(hash, "senha-secreta-1").ShouldBe(PasswordCheck.Success);
        _hasher.Verify(hash, "senha-secreta-2").ShouldBe(PasswordCheck.Failed);
        _hasher.Verify(hash, string.Empty).ShouldBe(PasswordCheck.Failed);
    }

    [Fact]
    public void Hash_gerado_pela_biblioteca_com_parametros_antigos_pede_novo_hash()
    {
        // Hash no formato v2 do Identity (PBKDF2-HMACSHA1, 1000 iterações): confere, mas deve ser regravado.
        var antigo = new Microsoft.AspNetCore.Identity.PasswordHasher<FinMy.Domain.Identity.User>(
            Microsoft.Extensions.Options.Options.Create(new Microsoft.AspNetCore.Identity.PasswordHasherOptions
            {
                CompatibilityMode = Microsoft.AspNetCore.Identity.PasswordHasherCompatibilityMode.IdentityV2,
            }));
        var hash = antigo.HashPassword(new FinMy.Domain.Identity.User("a", "a@b.com", "x", DateTimeOffset.UnixEpoch), "senha-secreta-1");

        _hasher.Verify(hash, "senha-secreta-1").ShouldBe(PasswordCheck.SuccessRehashNeeded);
    }

    [Fact]
    public void Hash_invalido_nao_confere()
    {
        _hasher.Verify("isto-nao-e-um-hash", "qualquer").ShouldBe(PasswordCheck.Failed);
    }
}
