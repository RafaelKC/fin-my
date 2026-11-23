using FinMy.Application.Abstractions;

namespace FinMy.Application.Profile;

/// <summary>Usuário e Espaço do contexto (US2, <c>GET /me</c>); quem tem token de uma conta removida é tratado como sem sessão.</summary>
public sealed class GetMeHandler(IProfileStore profiles, ICurrentContext context)
{
    public async Task<MeResult> HandleAsync(CancellationToken cancellationToken)
    {
        var userId = context.UserId ?? throw MeErrors.Unauthorized();
        return await profiles.GetAsync(userId, cancellationToken) ?? throw MeErrors.Unauthorized();
    }
}
