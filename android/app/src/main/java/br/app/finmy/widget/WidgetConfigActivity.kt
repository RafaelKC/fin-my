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
import android.annotation.SuppressLint
import android.net.Uri
import android.webkit.CookieManager
import android.webkit.WebResourceRequest
import android.webkit.WebView
import android.webkit.WebViewClient
import androidx.activity.ComponentActivity
import androidx.activity.OnBackPressedCallback
import androidx.core.view.isVisible
import androidx.glance.appwidget.GlanceAppWidgetManager
import androidx.lifecycle.lifecycleScope
import br.app.finmy.R
import kotlinx.coroutines.launch
import net.openid.appauth.AuthorizationRequest

/**
 * Configuração do widget: entra (OAuth + PKCE numa WebView desta tela, se ainda não houver sessão) e escolhe o cartão.
 * A WebView (app próprio falando com o próprio servidor) substitui o Custom Tab: dentro da TWA o Chrome não entrega o
 * redirect `br.app.finmy:/oauth/callback` ao app que a hospeda e o login nunca voltava para esta tela.
 * Também serve para "Entre de novo" e "Cartão indisponível — toque para escolher outro".
 */
class WidgetConfigActivity : ComponentActivity() {
    private var appWidgetId = AppWidgetManager.INVALID_APPWIDGET_ID
    private var pendingRequest: AuthorizationRequest? = null
    private lateinit var services: WidgetComponents

    private lateinit var message: TextView
    private lateinit var progress: ProgressBar
    private lateinit var action: Button
    private lateinit var cardList: ListView
    private lateinit var login: WebView

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
        login = findViewById(R.id.login)
        setupLogin()
        onBackPressedDispatcher.addCallback(
            this,
            object : OnBackPressedCallback(true) {
                override fun handleOnBackPressed() {
                    if (login.isVisible && login.canGoBack()) login.goBack() else finish()
                }
            },
        )
        services = WidgetServices.get(this)

        if (savedInstanceState == null) start()
    }

    override fun onDestroy() {
        login.destroy()
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

    @SuppressLint("SetJavaScriptEnabled")
    private fun setupLogin() {
        CookieManager.getInstance().setAcceptCookie(true)
        login.settings.javaScriptEnabled = true
        login.settings.domStorageEnabled = true
        login.webViewClient = object : WebViewClient() {
            override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest): Boolean {
                val url = request.url
                if (!AuthConfig.isRedirect(url)) return false
                handleRedirect(url)
                return true
            }
        }
    }

    private fun launchLogin() {
        val request = AuthConfig.authorizationRequest(services.baseUrl)
        pendingRequest = request
        progress.visibility = View.GONE
        action.visibility = View.GONE
        cardList.visibility = View.GONE
        message.setText(R.string.widget_config_login)
        login.visibility = View.VISIBLE
        login.loadUrl(request.toUri().toString())
    }

    private fun handleRedirect(url: Uri) {
        login.stopLoading()
        login.visibility = View.GONE
        val request = pendingRequest
        pendingRequest = null
        val code = url.getQueryParameter("code")
        val verifier = request?.codeVerifier
        if (url.getQueryParameter("error") != null || code == null || verifier == null ||
            url.getQueryParameter("state") != request.state
        ) {
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
        login.visibility = View.GONE
        message.setText(R.string.widget_config_loading)
        progress.visibility = View.VISIBLE
        action.visibility = View.GONE
        cardList.visibility = View.GONE
    }

    private fun show(messageRes: Int, buttonRes: Int, onClick: () -> Unit) {
        login.visibility = View.GONE
        message.setText(messageRes)
        progress.visibility = View.GONE
        cardList.visibility = View.GONE
        action.setText(buttonRes)
        action.setOnClickListener { onClick() }
        action.visibility = View.VISIBLE
    }
}
