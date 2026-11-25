package br.app.finmy.widget

import kotlinx.coroutines.test.runTest
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import okhttp3.mockwebserver.SocketPolicy
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class WidgetApiTest {
    private lateinit var server: MockWebServer
    private lateinit var rig: TestRig

    @Before
    fun setUp() {
        server = MockWebServer().also { it.start() }
        rig = TestRig(server, InMemoryTokenStore(TestRig.valid()))
    }

    @After
    fun tearDown() = server.shutdown()

    private fun json(body: String, code: Int = 200) = MockResponse().setResponseCode(code).setHeader("Content-Type", "application/json").setBody(body)

    @Test
    fun `lista os cartoes enviando o Bearer`() = runTest {
        server.enqueue(json(Fixtures.cardsJson()))
        val result = rig.api.cards() as ApiResult.Ok

        assertEquals(listOf("Azul", "Verde"), result.value.cards.map { it.name })
        assertEquals("#3b82f6", result.value.cards[0].color)
        val request = server.takeRequest()
        assertEquals("/api/v1/widget/credit-cards", request.path)
        assertEquals("Bearer acesso-1", request.getHeader("Authorization"))
    }

    @Test
    fun `fatura atual traz Atual, Planejado, vencimento, situacao e restante`() = runTest {
        server.enqueue(json(Fixtures.invoiceJson()))
        val ok = rig.api.currentInvoice(Fixtures.CARD_ID) as ApiResult.Ok
        val invoice = ok.value.invoice

        assertEquals("/api/v1/widget/credit-cards/${Fixtures.CARD_ID}/current-invoice", server.takeRequest().path)
        assertEquals(30000, invoice.totalCents)
        assertEquals("R$ 300,00", invoice.totalText)
        assertEquals("R$ 800,00", invoice.plannedText)
        assertEquals(80000L, invoice.estimateCents)
        assertEquals("2026-12-25", invoice.dueDate)
        assertEquals("open", invoice.status)
        assertEquals("/cartoes/${Fixtures.CARD_ID}/faturas/2026-12", ok.value.appPath)
    }

    @Test
    fun `campos novos e estimativa ausente nao quebram a leitura`() = runTest {
        server.enqueue(json(Fixtures.invoiceJson().replace("\"estimateCents\":80000,", "\"campoNovo\":1,")))
        val ok = rig.api.currentInvoice(Fixtures.CARD_ID) as ApiResult.Ok
        assertNull(ok.value.invoice.estimateCents)
    }

    @Test
    fun `401 renova o token uma vez e repete a chamada`() = runTest {
        server.enqueue(json("", 401))
        server.enqueue(json(Fixtures.tokenJson("acesso-novo", "refresh-novo")))
        server.enqueue(json(Fixtures.cardsJson()))

        assertTrue(rig.api.cards() is ApiResult.Ok)

        assertEquals("Bearer acesso-1", server.takeRequest().getHeader("Authorization"))
        val refresh = server.takeRequest()
        assertEquals("/oauth/token", refresh.path)
        assertTrue(refresh.body.readUtf8().contains("refresh_token=refresh-1"))
        assertEquals("Bearer acesso-novo", server.takeRequest().getHeader("Authorization"))
        assertEquals("refresh-novo", rig.store.tokens?.refreshToken)
    }

    @Test
    fun `401 mesmo apos renovar pede para entrar de novo e esquece a sessao`() = runTest {
        server.enqueue(json("", 401))
        server.enqueue(json(Fixtures.tokenJson()))
        server.enqueue(json("", 401))

        assertEquals(ApiResult.NeedsLogin, rig.api.cards())
        assertNull(rig.store.tokens)
    }

    @Test
    fun `refresh recusado (desconectado no Perfil) pede para entrar de novo`() = runTest {
        server.enqueue(json("", 401))
        server.enqueue(json("""{"error":"invalid_grant"}""", 400))

        assertEquals(ApiResult.NeedsLogin, rig.api.currentInvoice(Fixtures.CARD_ID))
        assertNull(rig.store.tokens)
    }

    @Test
    fun `sem sessao nem chama o servidor`() = runTest {
        rig.store.tokens = null
        assertEquals(ApiResult.NeedsLogin, rig.api.cards())
        assertEquals(0, server.requestCount)
    }

    @Test
    fun `token vencido e renovado antes da chamada`() = runTest {
        rig.store.tokens = Tokens("velho", "refresh-1", Fixtures.NOW - 1)
        server.enqueue(json(Fixtures.tokenJson("acesso-novo")))
        server.enqueue(json(Fixtures.cardsJson()))

        assertTrue(rig.api.cards() is ApiResult.Ok)
        assertEquals("/oauth/token", server.takeRequest().path)
        assertEquals("Bearer acesso-novo", server.takeRequest().getHeader("Authorization"))
    }

    @Test
    fun `404 da fatura e cartao indisponivel`() = runTest {
        server.enqueue(json("""{"code":"credit_card.not_found"}""", 404))
        assertEquals(ApiResult.CardUnavailable, rig.api.currentInvoice(Fixtures.CARD_ID))
    }

    @Test
    fun `sem rede o resultado e Offline e a sessao e mantida`() = runTest {
        server.enqueue(MockResponse().setSocketPolicy(SocketPolicy.DISCONNECT_AT_START))
        assertEquals(ApiResult.Offline, rig.api.currentInvoice(Fixtures.CARD_ID))
        assertEquals("acesso-1", rig.store.tokens?.accessToken)
    }

    @Test
    fun `erro 500 e corpo invalido viram Failure`() = runTest {
        server.enqueue(json("", 503))
        server.enqueue(json("nao e json"))
        assertTrue(rig.api.cards() is ApiResult.Failure)
        assertTrue(rig.api.cards() is ApiResult.Failure)
    }

    @Test
    fun `resumo dos widgets le o contrato do overview`() = runTest {
        server.enqueue(
            json(
                """{"today":"2026-11-25","month":"2026-11",
                "plannedIncome":{"cents":300000,"text":"R$ 3.000,00"},"plannedExpense":{"cents":250000,"text":"R$ 2.500,00"},
                "plannedFinal":{"cents":50000,"text":"R$ 500,00"},"actualIncome":{"cents":0,"text":"R$ 0,00"},
                "actualExpense":{"cents":0,"text":"R$ 0,00"},"actualFinal":{"cents":0,"text":"R$ 0,00"},
                "totalBalance":{"cents":0,"text":"R$ 0,00"},
                "pending":{"count":1,"toReceive":{"cents":0,"text":"R$ 0,00"},"toPay":{"cents":8000,"text":"R$ 80,00"}},
                "upcoming":[{"kind":"invoice","description":"Fatura Verde","date":"2026-11-25","amount":{"cents":30000,"text":"R$ 300,00"},"overdue":false}],
                "settlements":[{"tagId":"t","name":"Bruna","color":"#e91e63","amount":{"cents":17000,"text":"R$ 170,00"}}],
                "monthPath":"/mes/2026-11","settlementsPath":"/acertos/2026-11","novo":1}""",
            ),
        )

        val result = rig.api.overview() as ApiResult.Ok

        assertEquals("/api/v1/widget/overview", server.takeRequest().path)
        assertEquals("R$ 500,00", result.value.plannedFinal.text)
        assertEquals("Fatura Verde", result.value.upcoming.single().description)
        assertEquals(17000L, result.value.settlements.single().amount.cents)
    }
}
