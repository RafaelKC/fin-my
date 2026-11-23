namespace FinMy.Application.Auth;

/// <summary>Credenciais como chegaram (podem estar nulas ou com espaços).</summary>
/// <param name="DeviceLabel">Rótulo do aparelho (ex.: User-Agent) guardado na sessão; opcional.</param>
public sealed record LoginCommand(string? Email, string? Password, string? DeviceLabel = null);
