package br.app.finmy.widget

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.SerializationException
import okhttp3.FormBody
import okhttp3.OkHttpClient
import okhttp3.Request
import java.io.IOException

/** Cliente do endpoint `/oauth/token` da 008 (cliente público `finmy-android`, sem segredo). */
class TokenClient(
    private val baseUrl: String,
    private val http: OkHttpClient,
    private val clock: () -> Long = System::currentTimeMillis,
    private val clientId: String = AuthConfig.CLIENT_ID,
    private val redirectUri: String = AuthConfig.REDIRECT_URI,
) {
    /** Troca o código de autorização (PKCE) por tokens. */
    suspend fun exchangeCode(code: String, codeVerifier: String): ApiResult<Tokens> = post(
        FormBody.Builder()
            .add("grant_type", "authorization_code")
            .add("code", code)
            .add("code_verifier", codeVerifier)
            .add("client_id", clientId)
            .add("redirect_uri", redirectUri)
            .build(),
        previousRefreshToken = null,
    )

    /** Renova com o refresh token (rotação: a resposta traz um novo refresh token). */
    suspend fun refresh(refreshToken: String): ApiResult<Tokens> = post(
        FormBody.Builder()
            .add("grant_type", "refresh_token")
            .add("refresh_token", refreshToken)
            .add("client_id", clientId)
            .build(),
        previousRefreshToken = refreshToken,
    )

    private suspend fun post(body: FormBody, previousRefreshToken: String?): ApiResult<Tokens> = withContext(Dispatchers.IO) {
        try {
            val request = Request.Builder().url("$baseUrl${AuthConfig.TOKEN_PATH}").post(body).build()
            http.newCall(request).execute().use { response ->
                val text = response.body?.string().orEmpty()
                when {
                    response.isSuccessful -> {
                        val token = WidgetJson.decodeFromString<TokenResponse>(text)
                        ApiResult.Ok(
                            Tokens(
                                accessToken = token.access_token,
                                // Sem rotação na resposta, o refresh token anterior continua valendo.
                                refreshToken = token.refresh_token ?: previousRefreshToken,
                                expiresAtMillis = clock() + token.expires_in * 1000,
                            ),
                        )
                    }
                    // invalid_grant/invalid_client: a sessão foi revogada ou expirou.
                    response.code in 400..401 -> ApiResult.NeedsLogin
                    else -> ApiResult.Failure("HTTP ${response.code}")
                }
            }
        } catch (_: IOException) {
            ApiResult.Offline
        } catch (e: SerializationException) {
            ApiResult.Failure("Resposta de token inválida: ${e.message}")
        }
    }
}
