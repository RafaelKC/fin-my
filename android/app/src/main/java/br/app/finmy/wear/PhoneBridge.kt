package br.app.finmy.wear

import br.app.finmy.widget.ApiResult
import br.app.finmy.widget.OverviewResponse
import br.app.finmy.widget.OverviewState
import br.app.finmy.widget.WidgetApi
import br.app.finmy.widget.WidgetJson
import br.app.finmy.widget.WidgetServices
import com.google.android.gms.tasks.Task
import com.google.android.gms.wearable.WearableListenerService
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.async
import kotlinx.coroutines.cancel
import kotlinx.coroutines.tasks.asTask
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put

/**
 * Pedidos do relógio (010) pela Data Layer. O relógio não fala com o servidor: o celular já tem a VPN, o certificado e a
 * conexão do widget.
 */
object WearProtocol {
    const val OVERVIEW = "/finmy/overview"
    const val CREATE_ENTRY = "/finmy/entry"
    const val SETTLE = "/finmy/settle"

    /** Resposta: `{"ok":true,"data":...}` ou `{"ok":false,"error":"needs_login|offline|invalid|not_found|failed"}`. */
    suspend fun handle(api: WidgetApi, path: String, request: ByteArray): ByteArray {
        val body = runCatching { Json.parseToJsonElement(request.decodeToString()).jsonObject }.getOrDefault(JsonObject(emptyMap()))
        fun field(name: String) = body[name]?.jsonPrimitive?.content.orEmpty()
        val result: ApiResult<String> = when (path) {
            OVERVIEW -> when (val r = api.overview()) {
                is ApiResult.Ok -> ApiResult.Ok(WidgetJson.encodeToString(OverviewResponse.serializer(), r.value))
                ApiResult.NeedsLogin -> ApiResult.NeedsLogin
                ApiResult.Offline -> ApiResult.Offline
                ApiResult.CardUnavailable -> ApiResult.Failure("failed")
                is ApiResult.Failure -> r
            }
            CREATE_ENTRY -> api.createEntry(field("kind"), field("description"), field("amount"))
            SETTLE -> api.settleEntry(field("entryId"))
            else -> ApiResult.Failure("unknown_path")
        }
        return encode(result).encodeToByteArray()
    }

    fun encode(result: ApiResult<String>): String = when (result) {
        is ApiResult.Ok -> """{"ok":true,"data":${result.value.ifBlank { "null" }}}"""
        ApiResult.NeedsLogin -> error("needs_login")
        ApiResult.Offline -> error("offline")
        ApiResult.CardUnavailable -> error("not_found")
        is ApiResult.Failure -> error(if (result.message in setOf("invalid", "not_found")) result.message else "failed")
    }

    private fun error(code: String) = buildJsonObject {
        put("ok", false)
        put("error", code)
    }.toString()
}

/** Serviço do Wear OS no celular: atende os pedidos do relógio e, depois de gravar, atualiza os widgets do celular. */
class PhoneBridgeService : WearableListenerService() {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    override fun onRequest(nodeId: String, path: String, request: ByteArray): Task<ByteArray> =
        scope.async {
            val response = WearProtocol.handle(WidgetServices.get(applicationContext).api, path, request)
            if (path != WearProtocol.OVERVIEW) OverviewState.refreshAll(applicationContext)
            response
        }.asTask()

    override fun onDestroy() {
        scope.cancel()
        super.onDestroy()
    }
}
