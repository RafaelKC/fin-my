package br.app.finmy.widget

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.HttpUrl.Companion.toHttpUrl
import okhttp3.OkHttpClient
import okhttp3.Request
import java.io.IOException

/**
 * Conecta o widget à sessão do app (010), sem segundo login nem navegador: o front, já logado, cria um ticket de
 * consentimento para o cliente `finmy-android` com o `code_challenge` de [Pkce]; aqui o ticket vira um código em
 * `/oauth/authorize` (resposta 302 para `br.app.finmy:/oauth/callback`, lida sem seguir o redirect) e o código vira
 * tokens em `/oauth/token`.
 */
class WidgetConnector(
    private val baseUrl: String,
    http: OkHttpClient,
    private val tokenClient: TokenClient,
    private val session: AuthSession,
) {
    private val noRedirects = http.newBuilder().followRedirects(false).followSslRedirects(false).build()

    suspend fun connect(ticket: String, pkce: Pkce): ApiResult<Unit> {
        val code = when (val result = authorize(ticket, pkce)) {
            is ApiResult.Ok -> result.value
            ApiResult.Offline -> return ApiResult.Offline
            ApiResult.NeedsLogin -> return ApiResult.NeedsLogin
            is ApiResult.Failure -> return result
            else -> return ApiResult.Failure("authorize")
        }
        return when (val tokens = tokenClient.exchangeCode(code, pkce.verifier)) {
            is ApiResult.Ok -> {
                session.signIn(tokens.value)
                ApiResult.Ok(Unit)
            }
            ApiResult.Offline -> ApiResult.Offline
            ApiResult.NeedsLogin -> ApiResult.NeedsLogin
            is ApiResult.Failure -> tokens
            else -> ApiResult.Failure("token")
        }
    }

    private suspend fun authorize(ticket: String, pkce: Pkce): ApiResult<String> = withContext(Dispatchers.IO) {
        val url = "$baseUrl${AuthConfig.AUTHORIZE_PATH}".toHttpUrl().newBuilder()
            .addQueryParameter("response_type", "code")
            .addQueryParameter("client_id", AuthConfig.CLIENT_ID)
            .addQueryParameter("redirect_uri", AuthConfig.REDIRECT_URI)
            .addQueryParameter("code_challenge", pkce.challenge)
            .addQueryParameter("code_challenge_method", "S256")
            .addQueryParameter("state", pkce.state)
            .addQueryParameter("scope", "offline_access")
            .addQueryParameter("consent_ticket", ticket)
            .build()
        try {
            noRedirects.newCall(Request.Builder().url(url).get().build()).execute().use { response ->
                val location = response.header("Location")
                if (!response.isRedirect || location == null || !location.startsWith(AuthConfig.REDIRECT_URI)) {
                    return@withContext ApiResult.Failure("authorize HTTP ${response.code}")
                }
                val query = query(location)
                when {
                    query["error"] != null -> ApiResult.Failure("authorize ${query["error"]}")
                    query["state"] != pkce.state -> ApiResult.Failure("state")
                    else -> query["code"]?.let { ApiResult.Ok(it) } ?: ApiResult.Failure("sem code")
                }
            }
        } catch (_: IOException) {
            ApiResult.Offline
        }
    }

    /** Parâmetros da query do redirect (o esquema `br.app.finmy:` não é http; troca-se só para ler a query). */
    private fun query(location: String): Map<String, String> {
        val url = ("https://app.local/" + location.substringAfter('?', "").let { "?$it" }).toHttpUrl()
        return url.queryParameterNames.associateWith { url.queryParameter(it).orEmpty() }
    }
}
