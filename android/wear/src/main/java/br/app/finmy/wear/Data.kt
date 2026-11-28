package br.app.finmy.wear

import android.content.Context
import androidx.core.content.edit
import com.google.android.gms.wearable.CapabilityClient
import com.google.android.gms.wearable.Wearable
import kotlinx.coroutines.TimeoutCancellationException
import kotlinx.coroutines.tasks.await
import kotlinx.coroutines.withTimeout
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put

internal val WearJson = Json { ignoreUnknownKeys = true }

@Serializable
data class Money(val cents: Long, val text: String)

@Serializable
data class Upcoming(
    val kind: String,
    val description: String,
    val date: String,
    val amount: Money,
    val overdue: Boolean,
    val entryId: String? = null,
)

/** O que o relógio usa de `GET /api/v1/widget/overview` (o celular repassa o JSON inteiro). */
@Serializable
data class Overview(
    val today: String,
    val month: String,
    val plannedIncome: Money,
    val plannedExpense: Money,
    val plannedFinal: Money,
    val actualExpense: Money,
    val upcoming: List<Upcoming> = emptyList(),
    /** Cor de destaque escolhida no app do celular (Perfil › Aparência). */
    val accent: String? = null,
)

@Serializable
data class CreatedEntry(val id: String, val kind: String, val description: String, val amount: Money, val status: String, val wallet: String)

/** Resposta do celular ([br.app.finmy.wear.PhoneProtocol] do lado de lá: `{"ok":…,"data"|"error":…}`). */
sealed interface Reply {
    data class Ok(val data: JsonElement) : Reply
    data class Error(val code: String) : Reply
    data object NoPhone : Reply

    companion object {
        fun parse(bytes: ByteArray): Reply = try {
            val obj = WearJson.parseToJsonElement(bytes.decodeToString()).jsonObject
            if (obj["ok"]?.jsonPrimitive?.booleanOrNull == true) Ok(obj["data"] ?: JsonNull)
            else Error(obj["error"]?.jsonPrimitive?.contentOrNull ?: "failed")
        } catch (_: Exception) {
            Error("failed")
        }
    }
}

/** Mensagem para a pessoa conforme o erro. */
fun Reply.message(): String = when (this) {
    is Reply.Ok -> ""
    Reply.NoPhone -> "Celular fora de alcance"
    is Reply.Error -> when (code) {
        "needs_login" -> "Abra o app no celular e entre"
        "offline" -> "Celular sem conexão com o Fin-My"
        "invalid" -> "Valor inválido"
        "not_found" -> "Lançamento não encontrado"
        "timeout" -> "O celular demorou a responder"
        else -> "Não deu certo. Tente de novo"
    }
}

/** Fala com o app do celular pela Data Layer (RPC) e guarda o último resumo para o Tile e as complicações. */
class PhoneLink(private val context: Context) {
    private val prefs = context.getSharedPreferences("finmy_wear", Context.MODE_PRIVATE)

    private suspend fun call(path: String, body: String = "{}"): Reply {
        val nodes = try {
            Wearable.getCapabilityClient(context).getCapability(PHONE_CAPABILITY, CapabilityClient.FILTER_REACHABLE).await().nodes
        } catch (_: Exception) {
            return Reply.NoPhone
        }
        val node = nodes.firstOrNull { it.isNearby } ?: nodes.firstOrNull() ?: return Reply.NoPhone
        return try {
            val bytes = withTimeout(TIMEOUT_MS) {
                Wearable.getMessageClient(context).sendRequest(node.id, path, body.encodeToByteArray()).await()
            }
            Reply.parse(bytes)
        } catch (_: TimeoutCancellationException) {
            Reply.Error("timeout")
        } catch (_: Exception) {
            Reply.NoPhone
        }
    }

    suspend fun overview(): Pair<Overview?, Reply> {
        val reply = call(OVERVIEW)
        if (reply is Reply.Ok) {
            prefs.edit {
                putString(KEY_OVERVIEW, reply.data.toString())
                putLong(KEY_UPDATED, System.currentTimeMillis())
            }
        }
        return cachedOverview() to reply
    }

    fun cachedOverview(): Overview? = prefs.getString(KEY_OVERVIEW, null)?.let {
        runCatching { WearJson.decodeFromString(Overview.serializer(), it) }.getOrNull()
    }

    suspend fun createEntry(kind: String, description: String, amount: String): Reply = call(
        CREATE_ENTRY,
        buildJsonObject {
            put("kind", kind)
            put("description", description)
            put("amount", amount)
        }.toString(),
    ).also { if (it is Reply.Ok) History(context).remember(kind, description) }

    suspend fun settle(entryId: String): Reply = call(SETTLE, buildJsonObject { put("entryId", entryId) }.toString())

    companion object {
        const val PHONE_CAPABILITY = "finmy_phone"
        const val OVERVIEW = "/finmy/overview"
        const val CREATE_ENTRY = "/finmy/entry"
        const val SETTLE = "/finmy/settle"
        private const val TIMEOUT_MS = 15_000L
        private const val KEY_OVERVIEW = "overview"
        private const val KEY_UPDATED = "overview_updated"
    }
}

/** Descrições usadas no relógio, as mais recentes primeiro (sugestões do lançamento). */
class History(context: Context) {
    private val prefs = context.getSharedPreferences("finmy_wear_history", Context.MODE_PRIVATE)

    fun recent(kind: String): List<String> = prefs.getString(kind, "").orEmpty().split('\n').filter { it.isNotBlank() }

    fun remember(kind: String, description: String) {
        prefs.edit { putString(kind, Suggestions.push(recent(kind), description).joinToString("\n")) }
    }
}

object Suggestions {
    const val MAX = 8

    /** Coloca [description] na frente, sem repetir (ignorando caixa) e com no máximo [MAX]. */
    fun push(current: List<String>, description: String): List<String> {
        val clean = description.trim()
        if (clean.isEmpty()) return current
        return (listOf(clean) + current.filterNot { it.equals(clean, ignoreCase = true) }).take(MAX)
    }

    /** Sugestões: histórico do relógio e depois as descrições dos próximos vencimentos do mesmo tipo. */
    fun merge(history: List<String>, upcoming: List<Upcoming>, kind: String): List<String> =
        (history + upcoming.filter { it.kind == kind }.map { it.description })
            .distinctBy { it.lowercase() }
            .take(MAX)
}
