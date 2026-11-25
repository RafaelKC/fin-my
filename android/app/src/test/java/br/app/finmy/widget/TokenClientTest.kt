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

class TokenClientTest {
    private lateinit var server: MockWebServer
    private lateinit var rig: TestRig

    @Before
    fun setUp() {
        server = MockWebServer().also { it.start() }
        rig = TestRig(server, InMemoryTokenStore())
    }

    @After
    fun tearDown() = server.shutdown()

    @Test
    fun `troca o codigo com PKCE e cliente publico`() = runTest {
        server.enqueue(MockResponse().setBody(Fixtures.tokenJson("acesso", "refresh", 3600)))

        val ok = rig.tokenClient.exchangeCode("codigo-1", "verificador-1") as ApiResult.Ok

        val request = server.takeRequest()
        assertEquals("POST", request.method)
        assertEquals("/oauth/token", request.path)
        val form = request.body.readUtf8().split('&').associate { it.substringBefore('=') to java.net.URLDecoder.decode(it.substringAfter('='), "UTF-8") }
        assertEquals("authorization_code", form["grant_type"])
        assertEquals("codigo-1", form["code"])
        assertEquals("verificador-1", form["code_verifier"])
        assertEquals("finmy-android", form["client_id"])
        assertEquals("br.app.finmy:/oauth/callback", form["redirect_uri"])
        assertNull(form["client_secret"])
        assertEquals(Tokens("acesso", "refresh", Fixtures.NOW + 3_600_000), ok.value)
    }

    @Test
    fun `refresh usa o refresh token e guarda o novo (rotacao)`() = runTest {
        server.enqueue(MockResponse().setBody(Fixtures.tokenJson("acesso-2", "refresh-2")))
        val ok = rig.tokenClient.refresh("refresh-1") as ApiResult.Ok

        val body = server.takeRequest().body.readUtf8()
        assertTrue(body.contains("grant_type=refresh_token"))
        assertTrue(body.contains("refresh_token=refresh-1"))
        assertEquals("refresh-2", ok.value.refreshToken)
    }

    @Test
    fun `sem rotacao na resposta o refresh token anterior continua`() = runTest {
        server.enqueue(MockResponse().setBody(Fixtures.tokenJson("acesso-2", refresh = null)))
        val ok = rig.tokenClient.refresh("refresh-1") as ApiResult.Ok
        assertEquals("refresh-1", ok.value.refreshToken)
    }

    @Test
    fun `invalid_grant pede novo login`() = runTest {
        server.enqueue(MockResponse().setResponseCode(400).setBody("""{"error":"invalid_grant"}"""))
        assertEquals(ApiResult.NeedsLogin, rig.tokenClient.refresh("x"))
    }

    @Test
    fun `sem rede e Offline e erro do servidor e Failure`() = runTest {
        server.enqueue(MockResponse().setSocketPolicy(SocketPolicy.DISCONNECT_AT_START))
        server.enqueue(MockResponse().setResponseCode(500))
        assertEquals(ApiResult.Offline, rig.tokenClient.refresh("x"))
        assertTrue(rig.tokenClient.refresh("x") is ApiResult.Failure)
    }
}
