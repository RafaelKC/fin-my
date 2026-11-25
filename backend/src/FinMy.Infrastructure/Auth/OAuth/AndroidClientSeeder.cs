using FinMy.Application.OAuth;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;

namespace FinMy.Infrastructure.Auth.OAuth;

/// <summary>
/// Semeia o cliente <c>finmy-android</c> (009 research R3). Idempotente: cria se faltar e, se existir, alinha redirect,
/// permissões e nome ao esperado (por exemplo, depois de mudar <c>Android:RedirectUri</c>). Roda no comando <c>migrate</c>.
/// </summary>
public sealed class AndroidClientSeeder(IOpenIddictApplicationManager applications, IOptions<AndroidOptions> options)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var descriptor = AndroidClient.Descriptor(options.Value.EffectiveRedirectUri);
        var existing = await applications.FindByClientIdAsync(AndroidApp.ClientId, cancellationToken);
        if (existing is null)
        {
            await applications.CreateAsync(descriptor, cancellationToken);
            return;
        }

        await applications.UpdateAsync(existing, descriptor, cancellationToken);
    }
}
