namespace FinMy.Infrastructure.Auth.OAuth;

/// <summary>Configuração do conector do Claude (seção <c>Mcp</c>, 008 research R2).</summary>
public sealed class McpOptions
{
    public const string SectionName = "Mcp";

    /// <summary>Redirects que o Claude usa hoje (web, desktop e celular); a lista de config substitui esta.</summary>
    public static readonly IReadOnlyList<string> DefaultAllowedRedirectUris =
    [
        "https://claude.ai/api/mcp/auth_callback",
        "https://claude.com/api/mcp/auth_callback",
    ];

    /// <summary>Redirects aceitos no registro dinâmico de clientes (<c>Mcp:AllowedRedirectUris</c>); vazio = <see cref="DefaultAllowedRedirectUris"/>.</summary>
    public string[] AllowedRedirectUris { get; set; } = [];

    public IReadOnlyList<string> EffectiveAllowedRedirectUris => AllowedRedirectUris.Length > 0 ? AllowedRedirectUris : DefaultAllowedRedirectUris;
}
