using System.Security.Cryptography;
using System.Text;

namespace FinMy.Application.Auth;

/// <summary>Token de redefinição: 256 bits aleatórios em base64url; no banco vai só o SHA-256.</summary>
internal static class PasswordResetTokens
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    public static string Generate() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
