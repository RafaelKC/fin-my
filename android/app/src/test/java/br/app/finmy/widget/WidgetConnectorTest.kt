package br.app.finmy.widget

import kotlinx.coroutines.runBlocking
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import okhttp3.mockwebserver.SocketPolicy
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import java.security.MessageDigest
import java.util.Base64

class WidgetConnectorTest {
    private lateinit var server: MockWebServer
    private lateinit var rig: TestRig

    @Before
    fun setUp() {
        server = MockWebServer().also { it.start() }
        rig = TestRig(server, InMemoryTokenStore())
    }

    @After
    fun tearDown() = server.shutdown()

    private fun redirectTo(query: String) =
        MockResponse().setResponseCode(302).setHeader("Location", "br.app.finmy:/oauth/callback?$query")

    @Test
    fun `ticket vira codigo sem seguir o redirect e o codigo vira tokens`() = runBlocking {
        val pkce = Pkce.create()
        server.enqueue(redirectTo("code=abc%2B1&state=${pkce.state}&iss=x"))
        server.enqueue(MockResponse().setBody(Fixtures.tokenJson()))

        val result = rig.connector.connect("ticket-1", pkce)

        assertEquals(ApiResult.Ok(Unit), result)
        val authorize = server.takeRequest().requestUrl!!
        assertEquals("/oauth/authorize", authorize.encodedPath)
        assertEquals("finmy-android", authorize.queryParameter("client_id"))
        assertEquals("br.app.finmy:/oauth/callback", authorize.queryParameter("redirect_uri"))
        assertEquals(pkce.challenge, authorize.queryParameter("code_challenge"))
        assertEquals("S256", authorize.queryParameter("code_challenge_method"))
        assertEquals("ticket-1", authorize.queryParameter("consent_ticket"))
        val token = server.takeRequest().body.readUtf8()
        assertTrue(token.contains("code=abc%2B1"))
        assertTrue(token.contains("code_verifier=${pkce.verifier}"))
        assertTrue(rig.session.hasSession())
    }

    @Test
    fun `state diferente nao troca o codigo`() = runBlocking {
        server.enqueue(redirectTo("code=abc&state=outro"))

        val result = rig.connector.connect("t", Pkce.create())

        assertTrue(result is ApiResult.Failure)
        assertEquals(1, server.requestCount)
        assertEquals(false, rig.session.hasSession())
    }

    @Test
    fun `erro do servidor no redirect falha sem sessao`() = runBlocking {
        val pkce = Pkce.create()
        server.enqueue(redirectTo("error=access_denied&state=${pkce.state}"))

        assertTrue(rig.connector.connect("t", pkce) is ApiResult.Failure)
        assertEquals(false, rig.session.hasSession())
    }

    @Test
    fun `resposta que nao e redirect para o app falha`() = runBlocking {
        server.enqueue(MockResponse().setResponseCode(400).setBody("""{"error":"invalid_request"}"""))

        assertTrue(rig.connector.connect("t", Pkce.create()) is ApiResult.Failure)
    }

    @Test
    fun `sem rede devolve Offline`() = runBlocking {
        server.enqueue(MockResponse().setSocketPolicy(SocketPolicy.DISCONNECT_AT_START))

        assertEquals(ApiResult.Offline, rig.connector.connect("t", Pkce.create()))
    }

    @Test
    fun `pkce S256 confere com o verifier`() {
        val pkce = Pkce.create()
        val expected = Base64.getUrlEncoder().withoutPadding()
            .encodeToString(MessageDigest.getInstance("SHA-256").digest(pkce.verifier.toByteArray()))
        assertEquals(expected, pkce.challenge)
        assertTrue(pkce.verifier.length >= 43)
    }
}
