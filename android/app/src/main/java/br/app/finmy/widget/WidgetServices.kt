package br.app.finmy.widget

import android.content.Context
import br.app.finmy.BuildConfig
import okhttp3.OkHttpClient
import java.time.Duration

/** Peças do widget ligadas entre si (sem framework de injeção). Os testes trocam tudo por [install]. */
class WidgetComponents(
    val baseUrl: String,
    val session: AuthSession,
    val tokenClient: TokenClient,
    val api: WidgetApi,
    val refresher: InvoiceRefresher,
    val connector: WidgetConnector,
)

object WidgetServices {
    @Volatile
    private var components: WidgetComponents? = null

    fun get(context: Context): WidgetComponents = components ?: synchronized(this) {
        components ?: build(context.applicationContext).also { components = it }
    }

    /** Para testes: usa [override] no lugar da montagem de produção (`null` volta ao padrão). */
    fun install(override: WidgetComponents?) {
        components = override
    }

    fun build(
        context: Context,
        baseUrl: String = BuildConfig.APP_URL,
        http: OkHttpClient = defaultHttp(),
        store: TokenStore = EncryptedTokenStore(context, KeystoreKey.secretBox()),
    ): WidgetComponents {
        val tokenClient = TokenClient(baseUrl, http)
        val session = AuthSession(store, tokenClient)
        val api = WidgetApi(baseUrl, http, session)
        return WidgetComponents(baseUrl, session, tokenClient, api, InvoiceRefresher(api), WidgetConnector(baseUrl, http, tokenClient, session))
    }

    private fun defaultHttp() = OkHttpClient.Builder()
        .callTimeout(Duration.ofSeconds(20))
        .build()
}
