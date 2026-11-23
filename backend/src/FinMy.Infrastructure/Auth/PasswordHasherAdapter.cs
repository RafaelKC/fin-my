using FinMy.Application.Abstractions;
using FinMy.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace FinMy.Infrastructure.Auth;

/// <summary>Adaptador do <see cref="PasswordHasher{TUser}"/> (PBKDF2) do ASP.NET Core Identity; só o hasher, sem o resto do Identity.</summary>
public sealed class PasswordHasherAdapter : Application.Abstractions.IPasswordHasher
{
    // O PasswordHasher<T> não usa o usuário; este valor só satisfaz a assinatura.
    private static readonly User Placeholder = new("-", "-", "-", DateTimeOffset.UnixEpoch);

    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        return _hasher.HashPassword(Placeholder, password);
    }

    public PasswordCheck Verify(string passwordHash, string password)
    {
        ArgumentNullException.ThrowIfNull(passwordHash);
        ArgumentNullException.ThrowIfNull(password);

        try
        {
            return _hasher.VerifyHashedPassword(Placeholder, passwordHash, password) switch
            {
                PasswordVerificationResult.Success => PasswordCheck.Success,
                PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.SuccessRehashNeeded,
                _ => PasswordCheck.Failed,
            };
        }
        catch (FormatException)
        {
            // Hash que não é base64 do Identity: não confere.
            return PasswordCheck.Failed;
        }
    }
}
