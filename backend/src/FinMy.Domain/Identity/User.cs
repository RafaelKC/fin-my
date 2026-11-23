using FinMy.Domain.Common;

namespace FinMy.Domain.Identity;

/// <summary>Conta de usuário (<c>users</c>). Tabela global: não pertence a um Espaço e não tem auditoria de autor.</summary>
public sealed class User : IEntity
{
    // Construtor para o EF Core.
    private User()
    {
    }

    public User(string name, string email, string passwordHash, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        Name = name.Trim();
        Email = email.Trim();
        EmailNormalized = NormalizeEmail(email);
        PasswordHash = passwordHash;
        SecurityStamp = Guid.NewGuid();
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>E-mail como digitado (sem espaços nas pontas).</summary>
    public string Email { get; private set; } = string.Empty;

    /// <summary><c>lower(trim(email))</c>; único no banco.</summary>
    public string EmailNormalized { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    /// <summary>Novo valor a cada troca ou redefinição de senha.</summary>
    public Guid SecurityStamp { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Altera o nome de exibição (já validado pela Application).</summary>
    public void Rename(string name, DateTimeOffset now)
    {
        Name = name.Trim();
        UpdatedAt = now;
    }

    /// <summary>Troca a senha e renova o <see cref="SecurityStamp"/>.</summary>
    public void ChangePassword(string passwordHash, DateTimeOffset now)
    {
        PasswordHash = passwordHash;
        SecurityStamp = Guid.NewGuid();
        UpdatedAt = now;
    }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
