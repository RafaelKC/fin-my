package br.app.finmy.widget

import android.app.Activity
import android.appwidget.AppWidgetManager
import android.content.Intent
import android.view.View
import android.widget.Button
import android.widget.ListView
import android.widget.TextView
import android.net.Uri
import android.webkit.WebResourceRequest
import android.webkit.WebView
import androidx.test.core.app.ApplicationProvider
import androidx.work.testing.WorkManagerTestInitHelper
import br.app.finmy.R
import kotlinx.coroutines.runBlocking
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import okhttp3.mockwebserver.SocketPolicy
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.Robolectric
import org.robolectric.RobolectricTestRunner
import org.robolectric.Shadows.shadowOf
import org.robolectric.annotation.Config
import org.robolectric.shadows.ShadowLooper

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [34])
class WidgetConfigActivityTest {
    private val context = ApplicationProvider.getApplicationContext<android.app.Application>()
    private lateinit var server: MockWebServer
    private lateinit var rig: TestRig
    private var widgetId = 0

    @Before
    fun setUp() {
        server = MockWebServer().also { it.start() }
        rig = TestRig(server, InMemoryTokenStore())
        WidgetServices.install(rig.components())
        WorkManagerTestInitHelper.initializeTestWorkManager(context)
        val shadow = shadowOf(AppWidgetManager.getInstance(context))
        shadow.addInstalledProvider(
            android.appwidget.AppWidgetProviderInfo().apply { provider = android.content.ComponentName(context, FaturaWidgetReceiver::class.java) },
        )
        widgetId = shadow.createWidget(FaturaWidgetReceiver::class.java, androidx.glance.appwidget.R.layout.glance_default_loading_layout)
    }

    @After
    fun tearDown() {
        WidgetServices.install(null)
        server.shutdown()
    }

    private fun launch(widgetId: Int? = this.widgetId): org.robolectric.android.controller.ActivityController<WidgetConfigActivity> {
        val intent = Intent(context, WidgetConfigActivity::class.java)
        widgetId?.let { intent.putExtra(AppWidgetManager.EXTRA_APPWIDGET_ID, it) }
        return Robolectric.buildActivity(WidgetConfigActivity::class.java, intent).create().start().resume()
    }

    /** Espera as chamadas de rede (threads reais) e a fila principal. */
    private fun settle(check: () -> Boolean) {
        repeat(200) {
            ShadowLooper.idleMainLooper()
            if (check()) return
            Thread.sleep(25)
        }
    }

    @Test
    fun `sem id de widget a configuracao fecha`() {
        val controller = launch(widgetId = null)
        assertTrue(controller.get().isFinishing)
    }

    @Test
    fun `sem sessao pede para entrar e mostra o botao Entrar`() {
        val activity = launch().get()
        settle { activity.findViewById<Button>(R.id.action).visibility == View.VISIBLE }

        assertEquals(View.VISIBLE, activity.findViewById<Button>(R.id.action).visibility)
        assertEquals("Entrar", activity.findViewById<Button>(R.id.action).text.toString())
        assertEquals(context.getString(R.string.widget_config_login), activity.findViewById<TextView>(R.id.message).text.toString())
        assertEquals(Activity.RESULT_CANCELED, shadowOf(activity).resultCode)
        assertEquals(0, server.requestCount)
    }

    @Test
    fun `com sessao lista os cartoes ativos e escolher grava o cartao no widget`() {
        rig.store.tokens = TestRig.valid()
        server.enqueue(MockResponse().setBody(Fixtures.cardsJson()))
        server.enqueue(MockResponse().setBody(Fixtures.invoiceJson()))

        val activity = launch().get()
        val list = activity.findViewById<ListView>(R.id.cards)
        settle { list.visibility == View.VISIBLE }

        assertEquals(listOf("Azul", "Verde"), (0 until list.adapter.count).map { list.adapter.getItem(it) })
        assertEquals("/api/v1/widget/credit-cards", server.takeRequest().path)

        list.performItemClick(null, 0, 0)
        settle { activity.isFinishing }

        assertTrue(activity.isFinishing)
        assertEquals(Activity.RESULT_OK, shadowOf(activity).resultCode)
        assertEquals(widgetId, shadowOf(activity).resultIntent.getIntExtra(AppWidgetManager.EXTRA_APPWIDGET_ID, -1))
        // Busca já a fatura do cartão escolhido, para o widget nascer preenchido.
        assertEquals("/api/v1/widget/credit-cards/${Fixtures.CARD_ID}/current-invoice", server.takeRequest().path)
    }

    @Test
    fun `sem rede mostra aviso e tentar de novo`() {
        rig.store.tokens = TestRig.valid()
        server.enqueue(MockResponse().setSocketPolicy(SocketPolicy.DISCONNECT_AT_START))

        val activity = launch().get()
        settle { activity.findViewById<Button>(R.id.action).visibility == View.VISIBLE }

        assertEquals(context.getString(R.string.widget_config_offline), activity.findViewById<TextView>(R.id.message).text.toString())
        assertEquals("Tentar de novo", activity.findViewById<Button>(R.id.action).text.toString())
        runBlocking { assertTrue(rig.session.hasSession()) }
    }

    @Test
    fun `sessao revogada volta para o login`() {
        rig.store.tokens = TestRig.valid()
        server.enqueue(MockResponse().setResponseCode(401))
        server.enqueue(MockResponse().setResponseCode(400).setBody("""{"error":"invalid_grant"}"""))

        val activity = launch().get()
        settle { activity.findViewById<Button>(R.id.action).visibility == View.VISIBLE }

        assertEquals("Entrar", activity.findViewById<Button>(R.id.action).text.toString())
        runBlocking { assertEquals(false, rig.session.hasSession()) }
    }

    @Test
    fun `sem cartoes ativos avisa`() {
        rig.store.tokens = TestRig.valid()
        server.enqueue(MockResponse().setBody("""{"today":"2026-11-25","cards":[]}"""))

        val activity = launch().get()
        settle { activity.findViewById<Button>(R.id.action).visibility == View.VISIBLE }

        assertEquals(context.getString(R.string.widget_config_empty), activity.findViewById<TextView>(R.id.message).text.toString())
    }

    private fun redirect(url: String) = object : WebResourceRequest {
        override fun getUrl(): Uri = Uri.parse(url)
        override fun isForMainFrame() = true
        override fun isRedirect() = true
        override fun hasGesture() = false
        override fun getMethod() = "GET"
        override fun getRequestHeaders(): Map<String, String> = emptyMap()
    }

    @Test
    fun `Entrar abre o login na WebView da propria tela com PKCE e o redirect do app`() {
        val activity = launch().get()
        settle { activity.findViewById<Button>(R.id.action).visibility == View.VISIBLE }

        activity.findViewById<Button>(R.id.action).performClick()

        val web = activity.findViewById<WebView>(R.id.login)
        assertEquals(View.VISIBLE, web.visibility)
        val url = Uri.parse(shadowOf(web).lastLoadedUrl)
        assertEquals("/oauth/authorize", url.path)
        assertEquals("finmy-android", url.getQueryParameter("client_id"))
        assertEquals("br.app.finmy:/oauth/callback", url.getQueryParameter("redirect_uri"))
        assertEquals("S256", url.getQueryParameter("code_challenge_method"))
    }

    @Test
    fun `o redirect do login troca o codigo pelo token e mostra os cartoes`() {
        server.enqueue(MockResponse().setBody(Fixtures.tokenJson()))
        server.enqueue(MockResponse().setBody(Fixtures.cardsJson()))
        val activity = launch().get()
        settle { activity.findViewById<Button>(R.id.action).visibility == View.VISIBLE }
        activity.findViewById<Button>(R.id.action).performClick()
        val web = activity.findViewById<WebView>(R.id.login)
        val state = Uri.parse(shadowOf(web).lastLoadedUrl).getQueryParameter("state")

        val handled = shadowOf(web).webViewClient.shouldOverrideUrlLoading(web, redirect("br.app.finmy:/oauth/callback?code=abc&state=$state"))

        assertTrue(handled)
        val list = activity.findViewById<ListView>(R.id.cards)
        settle { list.visibility == View.VISIBLE }
        assertEquals(View.GONE, web.visibility)
        val token = server.takeRequest()
        assertEquals("/oauth/token", token.path)
        assertTrue(token.body.readUtf8().contains("code=abc"))
        runBlocking { assertTrue(rig.session.hasSession()) }
    }

    @Test
    fun `redirect com state errado nao troca o codigo`() {
        val activity = launch().get()
        settle { activity.findViewById<Button>(R.id.action).visibility == View.VISIBLE }
        activity.findViewById<Button>(R.id.action).performClick()
        val web = activity.findViewById<WebView>(R.id.login)

        shadowOf(web).webViewClient.shouldOverrideUrlLoading(web, redirect("br.app.finmy:/oauth/callback?code=abc&state=outro"))
        ShadowLooper.idleMainLooper()

        assertEquals(context.getString(R.string.widget_config_login_failed), activity.findViewById<TextView>(R.id.message).text.toString())
        assertEquals(0, server.requestCount)
    }

    @Test
    fun `outras URLs continuam na WebView`() {
        val activity = launch().get()
        settle { activity.findViewById<Button>(R.id.action).visibility == View.VISIBLE }
        activity.findViewById<Button>(R.id.action).performClick()
        val web = activity.findViewById<WebView>(R.id.login)

        assertEquals(false, shadowOf(web).webViewClient.shouldOverrideUrlLoading(web, redirect("https://100.113.21.47:8443/autorizar?x=1")))
    }
}
