using System.Text.Json.Serialization;
using FinMy.Application.OAuth;
using Microsoft.Extensions.Options;

namespace FinMy.Api.Endpoints;

/// <summary>
/// <c>GET /.well-known/assetlinks.json</c> (009 research R2): Digital Asset Links para o Chrome abrir o app Android (TWA) sem a barra
/// de endereço. Vem de <c>Android__PackageName</c> e <c>Android__CertSha256__N</c>; sem configuração, 404.
/// </summary>
public sealed class AssetLinksEndpoints : IEndpointModule
{
    public const string Path = "/.well-known/assetlinks.json";
    private const string Relation = "delegate_permission/common.handle_all_urls";

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet(Path, (IOptions<AndroidOptions> options) =>
        {
            var package = options.Value.PackageName?.Trim();
            var fingerprints = options.Value.CertSha256.Select(f => f.Trim()).Where(f => f.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (string.IsNullOrEmpty(package) || fingerprints.Length == 0)
            {
                return Results.NotFound();
            }

            return Results.Json(new[]
            {
                new AssetLink([Relation], new AssetLinkTarget("android_app", package, fingerprints)),
            });
        }).ExcludeFromDescription();
    }

    private sealed record AssetLink(string[] Relation, AssetLinkTarget Target);

    private sealed record AssetLinkTarget(
        string Namespace,
        [property: JsonPropertyName("package_name")] string PackageName,
        [property: JsonPropertyName("sha256_cert_fingerprints")] string[] Sha256CertFingerprints);
}
