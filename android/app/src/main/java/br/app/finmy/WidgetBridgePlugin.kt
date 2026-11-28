package br.app.finmy

import br.app.finmy.widget.ApiResult
import br.app.finmy.widget.AuthConfig
import br.app.finmy.widget.Pkce
import br.app.finmy.widget.WidgetServices
import br.app.finmy.widget.WidgetState
import br.app.finmy.widget.WidgetWork
import com.getcapacitor.JSObject
import com.getcapacitor.Plugin
import com.getcapacitor.PluginCall
import com.getcapacitor.PluginMethod
import com.getcapacitor.annotation.CapacitorPlugin
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch

/**
 * Ponte do widget para o front (010). O front, logado, chama `begin` (recebe o `code_challenge`), cria o ticket de
 * consentimento com a sessão do app e chama `complete` com o ticket; o nativo troca o ticket por tokens do cliente
 * `finmy-android`. Assim o widget usa a mesma conta sem pedir outro login.
 */
@CapacitorPlugin(name = "WidgetBridge")
class WidgetBridgePlugin : Plugin() {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)
    private var pending: Pkce? = null

    @PluginMethod
    fun status(call: PluginCall) {
        scope.launch {
            call.resolve(JSObject().put("connected", WidgetServices.get(context).session.hasSession()))
        }
    }

    @PluginMethod
    fun begin(call: PluginCall) {
        val pkce = Pkce.create().also { pending = it }
        call.resolve(
            JSObject()
                .put("clientId", AuthConfig.CLIENT_ID)
                .put("redirectUri", AuthConfig.REDIRECT_URI)
                .put("codeChallenge", pkce.challenge),
        )
    }

    @PluginMethod
    fun complete(call: PluginCall) {
        val ticket = call.getString("ticket")
        val pkce = pending
        if (ticket.isNullOrBlank() || pkce == null) {
            call.reject("Chame begin antes de complete, com o ticket.")
            return
        }
        pending = null
        scope.launch {
            when (val result = WidgetServices.get(context).connector.connect(ticket, pkce)) {
                is ApiResult.Ok -> {
                    WidgetWork.schedule(context)
                    WidgetState.refreshAll(context)
                    br.app.finmy.widget.OverviewState.refreshAll(context)
                    call.resolve(JSObject().put("connected", true))
                }
                ApiResult.Offline -> call.reject("Sem conexão com o Fin-My.", "offline")
                else -> call.reject("Não foi possível conectar o widget ($result).", "failed")
            }
        }
    }

    /** Cor de destaque escolhida no app (Perfil › Aparência): widgets e relógio passam a usá-la. */
    @PluginMethod
    fun setAccent(call: PluginCall) {
        val id = call.getString("id")
        scope.launch {
            br.app.finmy.widget.AccentStore.save(context, id.orEmpty())
            call.resolve()
        }
    }

    /** Leitura de notificações de banco (010): acesso liberado e apps de banco instalados (lidos ou não). */
    @PluginMethod
    fun notificationStatus(call: PluginCall) {
        val enabled = androidx.core.app.NotificationManagerCompat.getEnabledListenerPackages(context).contains(context.packageName)
        val apps = com.getcapacitor.JSArray()
        br.app.finmy.notifications.BankApps.installed(context).forEach { app ->
            apps.put(
                JSObject()
                    .put("package", app.packageName)
                    .put("label", app.label)
                    .put("bank", app.bank)
                    .put("enabled", br.app.finmy.notifications.BankApps.isEnabled(context, app.packageName)),
            )
        }
        call.resolve(JSObject().put("listenerEnabled", enabled).put("apps", apps))
    }

    /** Abre a tela do Android "Acesso a notificações" para liberar o Fin-My. */
    @PluginMethod
    fun openNotificationAccess(call: PluginCall) {
        val intent = android.content.Intent(android.provider.Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS)
            .addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK)
        context.startActivity(intent)
        call.resolve()
    }

    @PluginMethod
    fun setBankApp(call: PluginCall) {
        val pkg = call.getString("package")
        if (pkg.isNullOrBlank()) {
            call.reject("package obrigatório")
            return
        }
        br.app.finmy.notifications.BankApps.setEnabled(context, pkg, call.getBoolean("enabled", true) == true)
        call.resolve()
    }

    @PluginMethod
    fun disconnect(call: PluginCall) {
        scope.launch {
            WidgetServices.get(context).session.signOut()
            WidgetState.refreshAll(context)
            br.app.finmy.widget.OverviewState.refreshAll(context)
            call.resolve()
        }
    }
}
