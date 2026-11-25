package br.app.finmy.widget

import android.app.Activity
import android.appwidget.AppWidgetManager
import android.content.Intent
import android.os.Bundle
import android.view.View
import android.widget.ArrayAdapter
import android.widget.Button
import android.widget.ListView
import android.widget.ProgressBar
import android.widget.TextView
import androidx.activity.ComponentActivity
import androidx.activity.result.contract.ActivityResultContracts
import androidx.glance.appwidget.GlanceAppWidgetManager
import androidx.lifecycle.lifecycleScope
import br.app.finmy.R
import kotlinx.coroutines.launch
import net.openid.appauth.AuthorizationException
import net.openid.appauth.AuthorizationResponse
import net.openid.appauth.AuthorizationService

/**
 * Configuração do widget: entra (OAuth + PKCE no Custom Tab, se ainda não houver sessão) e escolhe o cartão.
 * Também serve para "Entre de novo" e "Cartão indisponível — toque para escolher outro".
 */
class WidgetConfigActivity : ComponentActivity() {
    private var appWidgetId = AppWidgetManager.INVALID_APPWIDGET_ID
    private var authService: AuthorizationService? = null
    private lateinit var services: WidgetComponents

    private lateinit var message: TextView
    private lateinit var progress: ProgressBar
    private lateinit var action: Button
    private lateinit var cardList: ListView

    private val loginLauncher = registerForActivityResult(ActivityResultContracts.StartActivityForResult()) { result ->
        handleLoginResult(result.data)
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        // Cancelado por padrão: se a pessoa sair, o Android descarta o widget que estava sendo adicionado.
        setResult(Activity.RESULT_CANCELED)
        appWidgetId = intent.getIntExtra(AppWidgetManager.EXTRA_APPWIDGET_ID, AppWidgetManager.INVALID_APPWIDGET_ID)
        if (appWidgetId == AppWidgetManager.INVALID_APPWIDGET_ID) {
            finish()
            return
        }
        setContentView(R.layout.activity_widget_config)
        message = findViewById(R.id.message)
        progress = findViewById(R.id.progress)
        action = findViewById(R.id.action)
        cardList = findViewById(R.id.cards)
        services = WidgetServices.get(this)

        if (savedInstanceState == null) start()
    }

    override fun onDestroy() {
        authService?.dispose()
        super.onDestroy()
    }

    private fun start() {
        lifecycleScope.launch {
            if (services.session.hasSession()) loadCards() else showLogin(R.string.widget_config_login)
        }
    }

    private fun showLogin(messageRes: Int) {
        show(messageRes, buttonRes = R.string.widget_config_login_button) { launchLogin() }
    }

    private fun launchLogin() {
        val service = authService ?: AuthorizationService(this).also { authService = it }
        val request = AuthConfig.authorizationRequest(services.baseUrl)
        loginLauncher.launch(service.getAuthorizationRequestIntent(request))
    }

    private fun handleLoginResult(data: Intent?) {
        val response = data?.let(AuthorizationResponse::fromIntent)
        val error = data?.let(AuthorizationException::fromIntent)
        val code = response?.authorizationCode
        val verifier = response?.request?.codeVerifier
        if (error != null || code == null || verifier == null) {
            showLogin(R.string.widget_config_login_failed)
            return
        }
        lifecycleScope.launch {
            showLoading()
            when (val tokens = services.tokenClient.exchangeCode(code, verifier)) {
                is ApiResult.Ok -> {
                    services.session.signIn(tokens.value)
                    loadCards()
                }
                ApiResult.Offline -> show(R.string.widget_config_offline, R.string.widget_config_retry) { start() }
                else -> showLogin(R.string.widget_config_login_failed)
            }
        }
    }

    private suspend fun loadCards() {
        showLoading()
        when (val result = services.api.cards()) {
            is ApiResult.Ok -> showCards(result.value.cards)
            ApiResult.NeedsLogin -> showLogin(R.string.widget_config_login)
            ApiResult.Offline -> show(R.string.widget_config_offline, R.string.widget_config_retry) { start() }
            else -> show(R.string.widget_config_error, R.string.widget_config_retry) { start() }
        }
    }

    private fun showCards(cards: List<CardItem>) {
        if (cards.isEmpty()) {
            show(R.string.widget_config_empty, R.string.widget_config_retry) { start() }
            return
        }
        progress.visibility = View.GONE
        action.visibility = View.GONE
        message.setText(R.string.widget_config_pick)
        cardList.adapter = ArrayAdapter(this, android.R.layout.simple_list_item_1, cards.map { it.name })
        cardList.setOnItemClickListener { _, _, position, _ -> choose(cards[position]) }
        cardList.visibility = View.VISIBLE
    }

    private fun choose(card: CardItem) {
        cardList.isEnabled = false
        lifecycleScope.launch {
            val manager = GlanceAppWidgetManager(this@WidgetConfigActivity)
            val glanceId = manager.getGlanceIds(FaturaWidget::class.java).first { manager.getAppWidgetId(it) == appWidgetId }
            WidgetState.write(this@WidgetConfigActivity, glanceId, WidgetSnapshot.chosen(card))
            WidgetWork.schedule(this@WidgetConfigActivity)
            // Busca já os valores para o widget nascer preenchido.
            WidgetState.refresh(this@WidgetConfigActivity, glanceId)
            setResult(Activity.RESULT_OK, Intent().putExtra(AppWidgetManager.EXTRA_APPWIDGET_ID, appWidgetId))
            finish()
        }
    }

    private fun showLoading() {
        message.setText(R.string.widget_config_loading)
        progress.visibility = View.VISIBLE
        action.visibility = View.GONE
        cardList.visibility = View.GONE
    }

    private fun show(messageRes: Int, buttonRes: Int, onClick: () -> Unit) {
        message.setText(messageRes)
        progress.visibility = View.GONE
        cardList.visibility = View.GONE
        action.setText(buttonRes)
        action.setOnClickListener { onClick() }
        action.visibility = View.VISIBLE
    }
}
