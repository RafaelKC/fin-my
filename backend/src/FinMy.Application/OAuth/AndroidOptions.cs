namespace FinMy.Application.OAuth;

/// <summary>Configuração do app Android (seção <c>Android</c>, 009 research R2 e R3).</summary>
public sealed class AndroidOptions
{
    public const string SectionName = "Android";

    public const string DefaultRedirectUri = "br.app.finmy:/oauth/callback";

    /// <summary>Pacote do app (<c>Android__PackageName</c>); vazio = o app não publica <c>assetlinks.json</c>.</summary>
    public string? PackageName { get; set; }

    /// <summary>Impressões digitais SHA-256 do certificado de assinatura (<c>Android__CertSha256__N</c>).</summary>
    public string[] CertSha256 { get; set; } = [];

    /// <summary>Redirect do login OAuth do widget (<c>Android__RedirectUri</c>).</summary>
    public string? RedirectUri { get; set; }

    public string EffectiveRedirectUri => string.IsNullOrWhiteSpace(RedirectUri) ? DefaultRedirectUri : RedirectUri.Trim();
}

/// <summary>O cliente OAuth público pré-registrado do app Android (009 research R3).</summary>
public static class AndroidApp
{
    public const string ClientId = "finmy-android";
    public const string DisplayName = "App Fin-My (Android)";

    /// <summary>O cliente é o do widget? (decide canal e recurso dos tokens).</summary>
    public static bool IsClient(string? clientId) => string.Equals(clientId, ClientId, StringComparison.Ordinal);
}
