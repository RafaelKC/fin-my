package br.app.finmy

import android.content.Intent
import android.os.Bundle
import android.webkit.WebView
import com.getcapacitor.BridgeActivity
import com.getcapacitor.WebViewListener

/**
 * App (010): Capacitor carregando o site (`server.url` = `finmy.appUrl`) com o mesmo login por cookie.
 * Links do widget, dos atalhos e das notificações (VIEW com uma URL do site) abrem essa página.
 */
class MainActivity : BridgeActivity() {
    /**
     * Link pedido ao abrir o app "frio": só pode ser carregado depois da página inicial do Capacitor, senão ela o
     * sobrescreve e o app fica na tela inicial (widget Lançar, toque na notificação de sugestão).
     */
    private var pendingLink: String? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        registerPlugin(WidgetBridgePlugin::class.java)
        super.onCreate(savedInstanceState)
        pedirPermissaoDeNotificacao()
        pendingLink = linkOf(intent)
        bridge?.addWebViewListener(
            object : WebViewListener() {
                override fun onPageLoaded(webView: WebView) {
                    val link = pendingLink ?: return
                    pendingLink = null
                    if (webView.url != link) webView.loadUrl(link)
                }
            },
        )
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        // App já aberto: a página inicial já carregou, abre o link direto.
        val link = linkOf(intent) ?: return
        val webView = bridge?.webView ?: return
        webView.post { webView.loadUrl(link) }
    }

    private fun linkOf(intent: Intent?): String? = intent?.dataString?.takeIf { it.startsWith(BuildConfig.APP_URL) }

    /** Android 13+: pede para mostrar notificações (sugestões de lançamento lidas do banco). */
    private fun pedirPermissaoDeNotificacao() {
        if (android.os.Build.VERSION.SDK_INT >= android.os.Build.VERSION_CODES.TIRAMISU &&
            androidx.core.content.ContextCompat.checkSelfPermission(this, android.Manifest.permission.POST_NOTIFICATIONS) !=
            android.content.pm.PackageManager.PERMISSION_GRANTED
        ) {
            androidx.core.app.ActivityCompat.requestPermissions(this, arrayOf(android.Manifest.permission.POST_NOTIFICATIONS), 1)
        }
    }
}
