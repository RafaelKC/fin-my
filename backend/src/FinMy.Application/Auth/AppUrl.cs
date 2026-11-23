namespace FinMy.Application.Auth;

/// <summary>URL pública do app (<c>APP_URL</c>), base dos links enviados por e-mail.</summary>
public sealed record AppUrl(string BaseUrl)
{
    public string Combine(string pathAndQuery) => $"{BaseUrl.TrimEnd('/')}{pathAndQuery}";
}
