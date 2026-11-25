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
