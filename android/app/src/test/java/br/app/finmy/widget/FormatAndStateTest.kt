package br.app.finmy.widget

import kotlinx.coroutines.test.runTest
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import okhttp3.mockwebserver.SocketPolicy
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import java.time.Instant
import java.time.ZoneId

class PtBrTest {
    @Test
    fun `datas em pt-BR`() {
        assertEquals("25/12", PtBr.shortDate("2026-12-25"))
        assertEquals("05/01", PtBr.shortDate("2027-01-05"))
    }

    @Test
    fun `hora em 24 horas no fuso informado`() {
        val instant = Instant.parse("2026-11-25T17:05:00Z")
        assertEquals("14:05", PtBr.time(instant, ZoneId.of("America/Sao_Paulo")))
        assertEquals("09:00", PtBr.time(Instant.parse("2026-11-25T12:00:00Z"), ZoneId.of("America/Sao_Paulo")))
    }

    @Test
    fun `situacao da fatura`() {
        assertEquals("Aberta", PtBr.status("open"))
        assertEquals("Fechada", PtBr.status("closed"))
        assertEquals("Paga", PtBr.status("paid"))
        assertEquals("Atrasada", PtBr.status("overdue"))
    }

    @Test
    fun `cor do cartao com fallback`() {
        assertEquals(0xFF3B82F6.toInt(), PtBr.parseColor("#3b82f6"))
        assertEquals(PtBr.FALLBACK_COLOR, PtBr.parseColor("azul"))
    }
}

class WidgetUiTest {
    private val zone = ZoneId.of("America/Sao_Paulo")
    private fun invoice(status: String = "open", remaining: String = "R$ 300,00") =
        WidgetJson.decodeFromString<CurrentInvoiceResponse>(Fixtures.invoiceJson(status, remaining))

    private fun snapshot(state: SnapshotState = SnapshotState.OK, stale: Boolean = false, data: CurrentInvoiceResponse? = invoice()) =
        WidgetSnapshot(Fixtures.CARD_ID, "Azul", "#3b82f6", state, data, Instant.parse("2026-11-25T17:05:00Z").toEpochMilli(), stale)

    @Test
    fun `sem cartao escolhido`() = assertEquals(WidgetUi.NotConfigured, WidgetUi.from(null))

    @Test
    fun `fatura aberta mostra Atual, Planejado, vencimento, situacao e restante`() {
        val ui = WidgetUi.from(snapshot(), zone) as WidgetUi.Content
        assertEquals("Azul", ui.cardName)
        assertEquals("R$ 300,00", ui.totalText)
        assertEquals("R$ 800,00", ui.plannedText)
        assertEquals("Vence 25/12", ui.dueLabel)
        assertEquals("Aberta", ui.statusLabel)
        assertEquals("R$ 300,00", ui.remainingText)
        assertEquals("atualizado às 14:05", ui.updatedLabel)
        assertEquals(false, ui.offline)
        assertEquals("/cartoes/${Fixtures.CARD_ID}/faturas/2026-12", ui.appPath)
    }

    @Test
    fun `fatura paga mostra Paga e restante zero`() {
        val ui = WidgetUi.from(snapshot(data = invoice("paid", "R$ 0,00")), zone) as WidgetUi.Content
        assertEquals("Paga", ui.statusLabel)
        assertEquals("R$ 0,00", ui.remainingText)
        assertTrue(ui.paid)
    }

    @Test
    fun `sem rede mantem os valores e marca offline`() {
        val ui = WidgetUi.from(snapshot(stale = true), zone) as WidgetUi.Content
        assertTrue(ui.offline)
        assertEquals("R$ 300,00", ui.totalText)
        assertEquals("atualizado às 14:05", ui.updatedLabel)
    }

    @Test
    fun `estados de login, cartao indisponivel e carregando`() {
        assertEquals(WidgetUi.NeedsLogin, WidgetUi.from(snapshot(SnapshotState.NEEDS_LOGIN), zone))
        assertEquals(WidgetUi.CardUnavailable, WidgetUi.from(snapshot(SnapshotState.CARD_UNAVAILABLE), zone))
        assertEquals(WidgetUi.Loading("Azul", false), WidgetUi.from(snapshot(SnapshotState.LOADING, data = null), zone))
        assertEquals(WidgetUi.Loading("Azul", true), WidgetUi.from(snapshot(SnapshotState.OK, stale = true, data = null), zone))
    }
}

class InvoiceRefresherTest {
    private val server = MockWebServer().also { it.start() }
    private val rig = TestRig(server, InMemoryTokenStore(TestRig.valid()))
    private val previous = WidgetSnapshot.chosen(CardItem(Fixtures.CARD_ID, "Azul", "#3b82f6"))

    @org.junit.After
    fun tearDown() = server.shutdown()

    private fun ok(body: String) = MockResponse().setBody(body)

    @Test
    fun `sucesso guarda o payload e a hora`() = runTest {
        server.enqueue(ok(Fixtures.invoiceJson()))
        val result = rig.refresher.refresh(previous)
        assertEquals(SnapshotState.OK, result.state)
        assertEquals(Fixtures.NOW, result.updatedAtMillis)
        assertEquals(30000L, result.data?.invoice?.totalCents)
        assertEquals(false, result.stale)
    }

    @Test
    fun `sem rede mantem os ultimos valores e a hora antiga`() = runTest {
        server.enqueue(ok(Fixtures.invoiceJson()))
        val good = rig.refresher.refresh(previous)
        val later = TestRig(server, rig.store) { Fixtures.NOW + 1_800_000 }
        server.enqueue(MockResponse().setSocketPolicy(SocketPolicy.DISCONNECT_AT_START))

        val result = later.refresher.refresh(good)

        assertEquals(SnapshotState.OK, result.state)
        assertTrue(result.stale)
        assertEquals(good.data, result.data)
        assertEquals(Fixtures.NOW, result.updatedAtMillis)
    }

    @Test
    fun `desconectado vira Entre de novo e 404 vira Cartao indisponivel, sem perder os valores`() = runTest {
        server.enqueue(ok(Fixtures.invoiceJson()))
        val good = rig.refresher.refresh(previous)

        server.enqueue(MockResponse().setResponseCode(404))
        val gone = rig.refresher.refresh(good)
        assertEquals(SnapshotState.CARD_UNAVAILABLE, gone.state)
        assertEquals(good.data, gone.data)

        rig.store.tokens = null
        assertEquals(SnapshotState.NEEDS_LOGIN, rig.refresher.refresh(good).state)
    }

    @Test
    fun `novo sucesso depois de erro limpa o estado de erro`() = runTest {
        server.enqueue(MockResponse().setResponseCode(404))
        val gone = rig.refresher.refresh(previous)
        server.enqueue(ok(Fixtures.invoiceJson()))
        val back = rig.refresher.refresh(gone)
        assertEquals(SnapshotState.OK, back.state)
    }

    @Test
    fun `progresso e mes curto e reais do grafico`() {
        assertEquals(0.375f, WidgetUi.progress(30000, 80000), 0.0001f)
        assertEquals(1f, WidgetUi.progress(90000, 80000), 0.0001f)
        assertEquals(0f, WidgetUi.progress(0, 0), 0.0001f)
        assertEquals(1f, WidgetUi.progress(100, 0), 0.0001f)
        assertEquals("dez", PtBr.monthShort("2026-12"))
        assertEquals("jan", PtBr.monthShort("2027-01"))
        assertEquals("R$ 1.234,56", PtBr.cents(123456))
        assertEquals("R$ 0,05", PtBr.cents(5))
    }
}
