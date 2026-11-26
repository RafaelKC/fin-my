package br.app.finmy.widget

import br.app.finmy.wear.WearProtocol
import kotlinx.coroutines.test.runTest
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test

class WearProtocolTest {
    private lateinit var server: MockWebServer
    private lateinit var rig: TestRig

    @Before
    fun setUp() {
        server = MockWebServer().also { it.start() }
        rig = TestRig(server, InMemoryTokenStore(TestRig.valid()))
    }

    @After
    fun tearDown() = server.shutdown()

    private suspend fun handle(path: String, body: String = "{}") = WearProtocol.handle(rig.api, path, body.encodeToByteArray()).decodeToString()

    @Test
    fun `lancamento do relogio vira POST no widget e a resposta volta inteira`() = runTest {
        server.enqueue(MockResponse().setResponseCode(201).setBody("""{"id":"e1","kind":"expense","description":"Padaria","amount":{"cents":4590,"text":"R$ 45,90"},"date":"2026-11-26","status":"settled","wallet":"Azul"}"""))

        val reply = handle(WearProtocol.CREATE_ENTRY, """{"kind":"expense","description":"Padaria","amount":"45,90"}""")

        val request = server.takeRequest()
        assertEquals("POST", request.method)
        assertEquals("/api/v1/widget/entries", request.path)
        assertEquals("Bearer acesso-1", request.getHeader("Authorization"))
        val sent = request.body.readUtf8()
        assertTrue(sent.contains("\"description\":\"Padaria\"") && sent.contains("\"amount\":\"45,90\""))
        assertTrue(reply.startsWith("""{"ok":true,"data":{"id":"e1""""))
    }

    @Test
    fun `marcar como pago e erros viram codigos para o relogio`() = runTest {
        server.enqueue(MockResponse().setResponseCode(204))
        assertEquals("""{"ok":true,"data":null}""", handle(WearProtocol.SETTLE, """{"entryId":"e9"}"""))
        assertEquals("/api/v1/widget/entries/e9/settle", server.takeRequest().path)

        server.enqueue(MockResponse().setResponseCode(422).setBody("{}"))
        assertEquals("""{"ok":false,"error":"invalid"}""", handle(WearProtocol.CREATE_ENTRY, """{"kind":"expense","description":"X","amount":"abc"}"""))

        server.enqueue(MockResponse().setResponseCode(404))
        assertEquals("""{"ok":false,"error":"not_found"}""", handle(WearProtocol.SETTLE, """{"entryId":"x"}"""))

        assertEquals("""{"ok":false,"error":"failed"}""", handle("/finmy/outra"))
    }

    @Test
    fun `sem sessao do widget o relogio recebe needs_login`() = runTest {
        rig.store.tokens = null
        assertEquals("""{"ok":false,"error":"needs_login"}""", handle(WearProtocol.OVERVIEW))
    }
}
