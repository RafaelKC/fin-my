using FinMy.Application.OAuth;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace FinMy.Infrastructure.Auth.OAuth;

/// <summary>Descritor OpenIddict do cliente <see cref="AndroidApp.ClientId"/> (009 research R3).</summary>
public static class AndroidClient
{
    public static OpenIddictApplicationDescriptor Descriptor(string redirectUri)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = AndroidApp.ClientId,
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Explicit,
            DisplayName = AndroidApp.DisplayName,
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
        };
        descriptor.RedirectUris.Add(new Uri(redirectUri, UriKind.Absolute));
        return descriptor;
    }
}
