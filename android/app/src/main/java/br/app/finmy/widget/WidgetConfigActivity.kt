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
import androidx.glance.appwidget.GlanceAppWidgetManager
import androidx.lifecycle.lifecycleScope
import br.app.finmy.MainActivity
import br.app.finmy.R
import kotlinx.coroutines.launch

/**
 * Configuração do widget: escolhe o cartão. A conexão vem da sessão do app (010, [br.app.finmy.WidgetBridgePlugin]):
 * sem ela, a tela pede para abrir o app (já logado, ele conecta o widget sozinho) e, ao voltar, lista os cartões.
 * Também serve para "Entre de novo" e "Cartão indisponível — toque para escolher outro".
 */
class WidgetConfigActivity : ComponentActivity() {
    private var appWidgetId = AppWidgetManager.INVALID_APPWIDGET_ID
    private var waitingForApp = false
    private lateinit var services: WidgetComponents

    private lateinit var message: TextView
    private lateinit var progress: ProgressBar
    private lateinit var action: Button
    private lateinit var cardList: ListView

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

    override fun onResume() {
        super.onResume()
        // Voltou do app: se ele conectou o widget, segue para a lista de cartões.
        if (waitingForApp) {
            waitingForApp = false
            start()
        }
    }

    private fun start() {
        lifecycleScope.launch {
            if (services.session.hasSession()) loadCards() else showLogin(R.string.widget_config_login)
        }
    }

    private fun showLogin(messageRes: Int) {
        show(messageRes, buttonRes = R.string.widget_config_open_app) { openApp() }
    }

    private fun openApp() {
        waitingForApp = true
        startActivity(Intent(this, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
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
