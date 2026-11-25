package br.app.finmy.widget

import okhttp3.OkHttpClient
import okhttp3.mockwebserver.MockWebServer

/** Dados fictícios: cartão "Azul" com R$ 300,00 na fatura atual e R$ 800,00 planejados (spec US2). */
object Fixtures {
    const val CARD_ID = "11111111-1111-1111-1111-111111111111"
    const val NOW = 1_764_075_000_000L // 2025-11-25T12:50Z

    fun cardsJson() = """{"today":"2026-11-25","cards":[
        {"id":"$CARD_ID","name":"Azul","color":"#3b82f6","closingDay":10,"dueDay":25},
        {"id":"22222222-2222-2222-2222-222222222222","name":"Verde","color":"#22c55e","closingDay":5,"dueDay":15}]}"""

    fun invoiceJson(status: String = "open", remaining: String = "R$ 300,00", remainingCents: Long = 30000) = """{
        "today":"2026-11-25",
        "card":{"id":"$CARD_ID","name":"Azul","color":"#3b82f6"},
        "invoice":{"dueMonth":"2026-12","closingDate":"2026-12-10","dueDate":"2026-12-25","status":"$status",
          "totalCents":30000,"totalText":"R${'$'} 300,00","plannedCents":80000,"plannedText":"R${'$'} 800,00","estimateCents":80000,
          "paidCents":0,"remainingCents":$remainingCents,"remainingText":"$remaining"},
        "appPath":"/cartoes/$CARD_ID/faturas/2026-12"}"""

    fun tokenJson(access: String = "acesso-2", refresh: String? = "refresh-2", expiresIn: Long = 3600) =
        """{"access_token":"$access",${refresh?.let { "\"refresh_token\":\"$it\"," } ?: ""}"token_type":"Bearer","expires_in":$expiresIn}"""
}

class InMemoryTokenStore(var tokens: Tokens? = null) : TokenStore {
    override suspend fun load() = tokens
    override suspend fun save(tokens: Tokens) { this.tokens = tokens }
    override suspend fun clear() { tokens = null }
}

/** Tudo ligado a um MockWebServer, com relógio fixo. */
class TestRig(
    val server: MockWebServer,
    val store: InMemoryTokenStore,
    val clock: () -> Long = { Fixtures.NOW },
) {
    val baseUrl: String = server.url("/").toString().trimEnd('/')
    val http = OkHttpClient()
    val tokenClient = TokenClient(baseUrl, http, clock)
    val session = AuthSession(store, tokenClient, clock)
    val api = WidgetApi(baseUrl, http, session)
    val refresher = InvoiceRefresher(api, clock)

    fun components() = WidgetComponents(baseUrl, session, tokenClient, api, refresher)

    companion object {
        fun valid() = Tokens("acesso-1", "refresh-1", Fixtures.NOW + 3_600_000)
    }
}

