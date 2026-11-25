package br.app.finmy.widget

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.SerializationException
import okhttp3.OkHttpClient
import okhttp3.Request
import java.io.IOException

/** Cliente das rotas somente leitura `/api/v1/widget` (specs/009 contracts/api.md). */
class WidgetApi(
    private val baseUrl: String,
    private val http: OkHttpClient,
    private val session: AuthSession,
) {
    suspend fun cards(): ApiResult<CardsResponse> =
        get("/api/v1/widget/credit-cards", notFound = null) { WidgetJson.decodeFromString<CardsResponse>(it) }

    /** Resumo dos widgets "Mês atual", "Próximos vencimentos" e "Acertos" (010). */
    suspend fun overview(): ApiResult<OverviewResponse> =
        get("/api/v1/widget/overview", notFound = null) { WidgetJson.decodeFromString(OverviewResponse.serializer(), it) }

    suspend fun currentInvoice(cardId: String): ApiResult<CurrentInvoiceResponse> =
        get("/api/v1/widget/credit-cards/$cardId/current-invoice", notFound = ApiResult.CardUnavailable) {
            WidgetJson.decodeFromString<CurrentInvoiceResponse>(it)
        }

    private suspend fun <T> get(path: String, notFound: ApiResult<Nothing>?, parse: (String) -> T): ApiResult<T> {
        val token = when (val t = session.accessToken()) {
            is ApiResult.Ok -> t.value
            else -> return t.passThrough()
        }
        when (val first = call(path, token, notFound, parse)) {
            is Raw.Done -> return first.result
            is Raw.Unauthorized -> Unit
        }

        // 401: o token pode ter sido revogado ou expirado antes da hora. Renova uma vez e tenta de novo.
        val renewed = when (val t = session.accessToken(rejectedToken = token)) {
            is ApiResult.Ok -> t.value
            else -> return t.passThrough()
        }
        return when (val retry = call(path, renewed, notFound, parse)) {
            is Raw.Done -> retry.result
            is Raw.Unauthorized -> {
                session.signOut()
                ApiResult.NeedsLogin
            }
        }
    }

    private suspend fun <T> call(path: String, token: String, notFound: ApiResult<Nothing>?, parse: (String) -> T): Raw<T> =
        withContext(Dispatchers.IO) {
            try {
                val request = Request.Builder().url("$baseUrl$path").header("Authorization", "Bearer $token").get().build()
                http.newCall(request).execute().use { response ->
                    when {
                        response.isSuccessful -> Raw.Done(ApiResult.Ok(parse(response.body?.string().orEmpty())))
                        response.code == 401 -> Raw.Unauthorized
                        response.code == 404 && notFound != null -> Raw.Done(notFound)
                        else -> Raw.Done(ApiResult.Failure("HTTP ${response.code}"))
                    }
                }
            } catch (_: IOException) {
                Raw.Done(ApiResult.Offline)
            } catch (e: SerializationException) {
                Raw.Done(ApiResult.Failure("Resposta inesperada: ${e.message}"))
            }
        }

    private sealed interface Raw<out T> {
        data class Done<T>(val result: ApiResult<T>) : Raw<T>
        data object Unauthorized : Raw<Nothing>
    }

    /** Repassa uma falha de sessão (não-Ok) com outro tipo de valor. */
    private fun ApiResult<String>.passThrough(): ApiResult<Nothing> = when (this) {
        is ApiResult.Ok -> ApiResult.Failure("inesperado")
        ApiResult.NeedsLogin -> ApiResult.NeedsLogin
        ApiResult.Offline -> ApiResult.Offline
        ApiResult.CardUnavailable -> ApiResult.CardUnavailable
        is ApiResult.Failure -> this
    }
}
