package br.app.finmy.widget

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.SerializationException
import okhttp3.OkHttpClient
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
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

    /** Lançamento rápido do relógio (010): `{kind, description, amount}`; resposta crua (JSON) para repassar ao relógio. */
    suspend fun createEntry(kind: String, description: String, amount: String): ApiResult<String> {
        val body = buildJsonObject {
            put("kind", kind)
            put("description", description)
            put("amount", amount)
        }.toString()
        return send("/api/v1/widget/entries", body)
    }

    /** Marca uma prevista como recebida/paga (relógio, 010). */
    suspend fun settleEntry(entryId: String): ApiResult<String> = send("/api/v1/widget/entries/$entryId/settle", "{}")

    private suspend fun send(path: String, json: String): ApiResult<String> =
        request(path, notFound = ApiResult.Failure("not_found"), parse = { it }) {
            post(json.toRequestBody("application/json".toMediaType()))
        }

    suspend fun currentInvoice(cardId: String): ApiResult<CurrentInvoiceResponse> =
        get("/api/v1/widget/credit-cards/$cardId/current-invoice", notFound = ApiResult.CardUnavailable) {
            WidgetJson.decodeFromString<CurrentInvoiceResponse>(it)
        }

    private suspend fun <T> get(path: String, notFound: ApiResult<Nothing>?, parse: (String) -> T): ApiResult<T> =
        request(path, notFound, parse) { get() }

    private suspend fun <T> request(
        path: String,
        notFound: ApiResult<Nothing>?,
        parse: (String) -> T,
        method: Request.Builder.() -> Request.Builder,
    ): ApiResult<T> {
        val token = when (val t = session.accessToken()) {
            is ApiResult.Ok -> t.value
            else -> return t.passThrough()
        }
        when (val first = call(path, token, notFound, parse, method)) {
            is Raw.Done -> return first.result
            is Raw.Unauthorized -> Unit
        }

        // 401: o token pode ter sido revogado ou expirado antes da hora. Renova uma vez e tenta de novo.
        val renewed = when (val t = session.accessToken(rejectedToken = token)) {
            is ApiResult.Ok -> t.value
            else -> return t.passThrough()
        }
        return when (val retry = call(path, renewed, notFound, parse, method)) {
            is Raw.Done -> retry.result
            is Raw.Unauthorized -> {
                session.signOut()
                ApiResult.NeedsLogin
            }
        }
    }

    private suspend fun <T> call(
        path: String,
        token: String,
        notFound: ApiResult<Nothing>?,
        parse: (String) -> T,
        method: Request.Builder.() -> Request.Builder,
    ): Raw<T> =
        withContext(Dispatchers.IO) {
            try {
                val request = Request.Builder().url("$baseUrl$path").header("Authorization", "Bearer $token").method().build()
                http.newCall(request).execute().use { response ->
                    when {
                        response.isSuccessful -> Raw.Done(ApiResult.Ok(parse(response.body?.string().orEmpty())))
                        response.code == 401 -> Raw.Unauthorized
                        response.code == 404 && notFound != null -> Raw.Done(notFound)
                        response.code == 422 -> Raw.Done(ApiResult.Failure("invalid"))
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
