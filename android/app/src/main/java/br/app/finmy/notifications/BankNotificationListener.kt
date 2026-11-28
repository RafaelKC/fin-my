package br.app.finmy.notifications

import android.Manifest
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.service.notification.NotificationListenerService
import android.service.notification.StatusBarNotification
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat
import androidx.core.net.toUri
import br.app.finmy.BuildConfig
import br.app.finmy.R
import br.app.finmy.widget.ApiResult
import br.app.finmy.widget.WidgetServices
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put
import java.security.MessageDigest
import java.time.Instant

/**
 * Lê as notificações dos apps de banco ligados ([BankApps]) e, quando são uma movimentação ([BankNotificationParser]),
 * envia uma sugestão ao Fin-My e mostra "Lançar R$ 45,90?" — o toque abre o formulário pré-preenchido (010).
 * O texto da notificação só sai do aparelho como sugestão do próprio Espaço.
 */
class BankNotificationListener : NotificationListenerService() {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    override fun onNotificationPosted(sbn: StatusBarNotification) {
        val pkg = sbn.packageName
        if (pkg == packageName || !BankApps.isEnabled(this, pkg)) return
        val extras = sbn.notification?.extras ?: return
        val title = extras.getCharSequence(Notification.EXTRA_TITLE)?.toString()
        val text = (extras.getCharSequence(Notification.EXTRA_BIG_TEXT) ?: extras.getCharSequence(Notification.EXTRA_TEXT))?.toString()
        val parsed = BankNotificationParser.parse(title, text) ?: return
        val app = BankApps.find(pkg)
        val raw = listOfNotNull(title, text).joinToString(" · ")
        val key = dedupeKey(pkg, raw, sbn.postTime)
        scope.launch { submit(applicationContext, app?.label ?: pkg, app?.bank, parsed, raw, key, sbn.postTime) }
    }

    override fun onDestroy() {
        scope.cancel()
        super.onDestroy()
    }

    companion object {
        private const val CHANNEL = "sugestoes"

        /** Mesma notificação (app, texto e minuto) → mesma chave; o servidor não repete a sugestão. */
        fun dedupeKey(pkg: String, raw: String, postTime: Long): String {
            val digest = MessageDigest.getInstance("SHA-256").digest("$pkg|$raw|${postTime / 60_000}".toByteArray())
            return digest.joinToString("") { "%02x".format(it) }.take(40)
        }

        suspend fun submit(context: Context, source: String, bank: String?, parsed: ParsedNotification, raw: String, key: String, postTime: Long) {
            val body = buildJsonObject {
                put("source", source)
                bank?.let { put("bank", it) }
                put("kind", parsed.kind)
                put("description", parsed.description)
                put("amountCents", parsed.amountCents)
                put("occurredAt", Instant.ofEpochMilli(postTime).toString())
                parsed.cardLast4?.let { put("cardLast4", it) }
                put("rawText", raw.take(500))
                put("dedupeKey", key)
            }.toString()
            val result = WidgetServices.get(context).api.submitSuggestion(body)
            if (result !is ApiResult.Ok) return
            val json = runCatching { Json.parseToJsonElement(result.value).jsonObject }.getOrNull() ?: return
            val id = json["id"]?.jsonPrimitive?.content ?: return
            val amount = json["amountText"]?.jsonPrimitive?.content ?: return
            notify(context, id, amount, parsed.description, source)
        }

        private fun notify(context: Context, id: String, amount: String, description: String, source: String) {
            if (ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) return
            val manager = context.getSystemService(NotificationManager::class.java)
            manager.createNotificationChannel(
                NotificationChannel(CHANNEL, context.getString(R.string.channel_sugestoes), NotificationManager.IMPORTANCE_DEFAULT),
            )
            val open = Intent(Intent.ACTION_VIEW, "${BuildConfig.APP_URL}/sugestoes/$id".toUri())
                .setComponent(ComponentName(context, "br.app.finmy.MainActivity"))
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            val tap = PendingIntent.getActivity(context, id.hashCode(), open, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
            val notification = NotificationCompat.Builder(context, CHANNEL)
                .setSmallIcon(R.drawable.ic_notification)
                .setContentTitle("Lançar $amount?")
                .setContentText("$description · $source")
                .setContentIntent(tap)
                .setAutoCancel(true)
                .setCategory(NotificationCompat.CATEGORY_RECOMMENDATION)
                .build()
            NotificationManagerCompat.from(context).notify(id.hashCode(), notification)
        }
    }
}
