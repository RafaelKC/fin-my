package br.app.finmy.widget

import android.net.Uri
import androidx.core.net.toUri
import net.openid.appauth.AuthorizationRequest
import net.openid.appauth.AuthorizationServiceConfiguration
import net.openid.appauth.ResponseTypeValues

/** Parâmetros do cliente OAuth público do widget (specs/009 research R3). */
object AuthConfig {
    const val CLIENT_ID = "finmy-android"
    const val REDIRECT_URI = "br.app.finmy:/oauth/callback"
    const val AUTHORIZE_PATH = "/oauth/authorize"
    const val TOKEN_PATH = "/oauth/token"

    /** Pedido de autorização com PKCE S256 (o AppAuth gera o verifier e o challenge). */
    fun authorizationRequest(baseUrl: String): AuthorizationRequest {
        val config = AuthorizationServiceConfiguration(
            "$baseUrl$AUTHORIZE_PATH".toUri(),
            "$baseUrl$TOKEN_PATH".toUri(),
        )
        return AuthorizationRequest.Builder(config, CLIENT_ID, ResponseTypeValues.CODE, REDIRECT_URI.toUri())
            .setScope("offline_access")
            .build()
    }

    /** O redirect do login (`br.app.finmy:/oauth/callback?...`). */
    fun isRedirect(url: Uri): Boolean =
        url.scheme == "br.app.finmy" && (url.path ?: url.schemeSpecificPart.substringBefore('?')).endsWith("/oauth/callback")
}
