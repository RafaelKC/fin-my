package br.app.finmy.widget

import androidx.compose.ui.unit.DpSize
import androidx.compose.ui.unit.dp
import androidx.glance.appwidget.testing.unit.hasStartActivityClickAction
import androidx.glance.appwidget.testing.unit.runGlanceAppWidgetUnitTest
import androidx.glance.testing.unit.hasText
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [34])
class ResumoWidgetsTest {
    private fun m(cents: Long) = Money(cents, PtBr.cents(cents))

    private fun overview(
        upcoming: List<OverviewUpcoming> = listOf(
            OverviewUpcoming("expense", "Água", "2026-11-10", m(8000), overdue = true),
            OverviewUpcoming("invoice", "Fatura Verde", "2026-11-25", m(30000), overdue = false),
            OverviewUpcoming("income", "Freela", "2026-11-28", m(50000), overdue = false),
        ),
        settlements: List<OverviewSettlement> = listOf(
            OverviewSettlement("1", "Bruna", "#e91e63", m(17000)),
            OverviewSettlement("2", "Carla", "#3f51b5", m(-2000)),
        ),
    ) = OverviewResponse(
        today = "2026-11-25", month = "2026-11",
        plannedIncome = m(300000), plannedExpense = m(250000), plannedFinal = m(123456),
        actualIncome = m(300000), actualExpense = m(200000), actualFinal = m(173456),
        totalBalance = m(173456), pending = OverviewPending(1, m(0), m(8000)),
        upcoming = upcoming, settlements = settlements, monthPath = "/mes/2026-11", settlementsPath = "/acertos/2026-11",
    )

    private fun ok(data: OverviewResponse = overview()) = OverviewSnapshot(SnapshotState.OK, data, updatedAtMillis = 0L)

    private fun render(size: DpSize = DpSize(300.dp, 200.dp), content: @androidx.compose.runtime.Composable () -> Unit, check: androidx.glance.appwidget.testing.unit.GlanceAppWidgetUnitTest.() -> Unit) = runTest {
        runGlanceAppWidgetUnitTest {
            setAppWidgetSize(size)
            provideComposable { content() }
            check()
        }
    }

    @Test
    fun `mes atual mostra o mes, o saldo previsto, entradas, saidas e quanto falta pagar`() = render(content = { MesContent(ok()) }) {
        onNode(hasText("Novembro")).assertExists()
        onNode(hasText("Saldo previsto no fim do mês")).assertExists()
        onNode(hasText("R$ 1.234,56")).assertExists()
        onNode(hasText("R$ 3.000,00")).assertExists()
        onNode(hasText("R$ 2.500,00")).assertExists()
        onNode(hasText("Falta pagar R$ 500,00")).assertExists()
        onNode(hasStartActivityClickAction(openAppIntent("/mes/2026-11"))).assertExists()
    }

    @Test
    fun `vencimentos listam atrasadas com aviso, faturas e entradas`() = render(content = { VencimentosContent(ok()) }) {
        onNode(hasText("1 em atraso")).assertExists()
        onNode(hasText("Água")).assertExists()
        onNode(hasText("10/11")).assertExists()
        onNode(hasText("Fatura Verde")).assertExists()
        onNode(hasText("+R$ 500,00")).assertExists()
    }

    @Test
    fun `vencimentos vazios dizem que esta tudo em dia`() = render(content = { VencimentosContent(ok(overview(upcoming = emptyList()))) }) {
        onNode(hasText("Tudo em dia por aqui")).assertExists()
    }

    @Test
    fun `acertos mostram quem deve e quem tem credito com o total a receber`() = render(content = { AcertosContent(ok()) }) {
        onNode(hasText("A receber R$ 170,00")).assertExists()
        onNode(hasText("Bruna")).assertExists()
        onNode(hasText("deve para você")).assertExists()
        onAllNodes(hasText("R$ 170,00")).assertCountEquals(2)
        onNode(hasText("tem crédito")).assertExists()
        onNode(hasStartActivityClickAction(openAppIntent("/acertos/2026-11"))).assertExists()
    }

    @Test
    fun `acertos vazios`() = render(content = { AcertosContent(ok(overview(settlements = emptyList()))) }) {
        onNode(hasText("Ninguém deve nada")).assertExists()
    }

    @Test
    fun `sem conexao do widget pede para abrir o app e sem dados mostra carregando`() {
        render(content = { MesContent(OverviewSnapshot(SnapshotState.NEEDS_LOGIN)) }) {
            onNode(hasText("Conecte o widget")).assertExists()
        }
        render(content = { AcertosContent(null) }) { onNode(hasText("Carregando…")).assertExists() }
    }

    @Test
    fun `lancar abre o formulario rapido no app`() = render(DpSize(146.dp, 60.dp), content = { LancarContent() }) {
        onNode(hasText("Lançar")).assertExists()
        onNode(hasStartActivityClickAction(openAppIntent("/mes?lancar=1"))).assertExists()
    }

    @Test
    fun `nova busca sem rede mantem os valores e marca como desatualizado`() {
        val previous = ok()
        val offline = previous.next(ApiResult.Offline, now = 99L)
        assertEquals(previous.data, offline.data)
        assertTrue(offline.stale)
        assertEquals(SnapshotState.NEEDS_LOGIN, previous.next(ApiResult.NeedsLogin, 99L).state)
        val fresh = OverviewSnapshot().next(ApiResult.Ok(overview()), now = 5L)
        assertEquals(SnapshotState.OK, fresh.state)
        assertEquals(5L, fresh.updatedAtMillis)
        assertEquals("Novembro", monthName("2026-11"))
    }
}
