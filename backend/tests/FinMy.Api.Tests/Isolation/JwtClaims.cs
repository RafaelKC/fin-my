using System.Text;
using System.Text.Json;

namespace FinMy.Api.Tests.Isolation;

/// <summary>Lê as claims do payload de um JWT (sem validar a assinatura: só para inspecionar o que a API emitiu).</summary>
public static class JwtClaims
{
    public static JsonElement Read(string jwt)
    {
        var payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
        return document.RootElement.Clone();
    }

    public static string? Get(string jwt, string claim) =>
        Read(jwt).TryGetProperty(claim, out var value) ? value.GetString() : null;
}
