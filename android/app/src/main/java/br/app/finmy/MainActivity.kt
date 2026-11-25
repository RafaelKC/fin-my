package br.app.finmy

import android.content.Intent
import android.os.Bundle
import com.getcapacitor.BridgeActivity

/**
 * App (010): Capacitor carregando o site (`server.url` = `finmy.appUrl`) com o mesmo login por cookie.
 * Links do widget e dos atalhos (VIEW com uma URL do site) abrem essa página.
 */
class MainActivity : BridgeActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        registerPlugin(WidgetBridgePlugin::class.java)
        super.onCreate(savedInstanceState)
        openLink(intent)
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        openLink(intent)
    }

    private fun openLink(intent: Intent?) {
        val url = intent?.dataString ?: return
        if (!url.startsWith(BuildConfig.APP_URL)) return
        val webView = bridge?.webView ?: return
        webView.post { webView.loadUrl(url) }
    }
}
