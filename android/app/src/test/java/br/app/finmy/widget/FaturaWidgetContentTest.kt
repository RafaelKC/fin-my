package br.app.finmy.widget

import android.content.Context
import android.content.Intent
import androidx.compose.ui.unit.DpSize
import androidx.compose.ui.unit.dp
import androidx.glance.appwidget.testing.unit.hasRunCallbackClickAction
import androidx.glance.appwidget.testing.unit.hasStartActivityClickAction
import androidx.glance.appwidget.testing.unit.runGlanceAppWidgetUnitTest
import androidx.glance.testing.unit.hasText
import androidx.test.core.app.ApplicationProvider
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [34])
class FaturaWidgetContentTest {
    private val context = ApplicationProvider.getApplicationContext<Context>()
    private val open = FaturaWidget.openInvoiceIntent("https://fin-my.exemplo.com.br", "/cartoes/${Fixtures.CARD_ID}/faturas/2026-12")
    private val config = FaturaWidget.configIntent(context, 7)

    private fun content(status: String = "open", stale: Boolean = false, remaining: String = "R$ 120,00") = WidgetUi.Content(
        cardName = "Azul",
        cardColor = PtBr.parseColor("#3b82f6"),
        dueLabel = "Vence 25/12",
        statusLabel = PtBr.status(status),
        paid = status == "paid",
        totalText = "R$ 300,00",
        plannedText = "R$ 800,00",
        remainingText = remaining,
        updatedLabel = "atualizado às 14:05",
        offline = stale,
        appPath = "/cartoes/${Fixtures.CARD_ID}/faturas/2026-12",
    )

    private fun render(ui: WidgetUi, check: androidx.glance.appwidget.testing.unit.GlanceAppWidgetUnitTest.() -> Unit) = runTest {
        runGlanceAppWidgetUnitTest {
            setAppWidgetSize(DpSize(146.dp, 146.dp))
            provideComposable { FaturaWidgetContent(ui, open, config) }
            check()
        }
    }

    @Test
    fun `mostra cartao, Atual, Planejado, vencimento, situacao e restante`() = render(content()) {
        onNode(hasText("Azul")).assertExists()
        onNode(hasText("Atual")).assertExists()
        onNode(hasText("R$ 300,00")).assertExists()
        onNode(hasText("Planejado")).assertExists()
        onNode(hasText("R$ 800,00")).assertExists()
        onNode(hasText("Vence 25/12 · Aberta")).assertExists()
        onNode(hasText("Falta R$ 120,00")).assertExists()
        onNode(hasText("atualizado às 14:05")).assertExists()
    }

    @Test
    fun `fatura paga mostra Paga e restante zerado`() = render(content("paid", remaining = "R$ 0,00")) {
        onNode(hasText("Vence 25/12 · Paga")).assertExists()
        onNode(hasText("Restante R$ 0,00")).assertExists()
    }

    @Test
    fun `sem rede mantem os valores e avisa com a hora`() = render(content(stale = true)) {
        onNode(hasText("R$ 800,00")).assertExists()
        onNode(hasText("Sem conexão · atualizado às 14:05")).assertExists()
    }

    @Test
    fun `toque no corpo abre a fatura na TWA e o icone atualiza`() = render(content()) {
        onNode(hasStartActivityClickAction(open)).assertExists()
        onNode(hasRunCallbackClickAction<RefreshAction>()).assertExists()
    }

    @Test
    fun `Entre de novo e Cartao indisponivel abrem a configuracao`() {
        render(WidgetUi.NeedsLogin) {
            onNode(hasText("Entre de novo")).assertExists()
            onNode(hasStartActivityClickAction(config)).assertExists()
        }
        render(WidgetUi.CardUnavailable) {
            onNode(hasText("Cartão indisponível")).assertExists()
            onNode(hasText("Toque para escolher outro")).assertExists()
            onNode(hasStartActivityClickAction(config)).assertExists()
        }
    }

    @Test
    fun `widget novo e carregando`() {
        render(WidgetUi.NotConfigured) { onNode(hasText("Toque para escolher o cartão")).assertExists() }
        render(WidgetUi.Loading("Azul", false)) { onNode(hasText("Carregando…")).assertExists() }
        render(WidgetUi.Loading("Azul", true)) { onNode(hasText("Sem conexão. Toque para tentar de novo")).assertExists() }
    }

    @Test
    fun `intent da fatura vai para a TWA com a URL do site`() {
        assertEquals("https://fin-my.exemplo.com.br/cartoes/${Fixtures.CARD_ID}/faturas/2026-12", open.dataString)
        assertEquals("com.google.androidbrowserhelper.trusted.LauncherActivity", open.component?.className)
        assertEquals(Intent.ACTION_VIEW, open.action)
    }

    @Test
    @Config(sdk = [34], qualifiers = "night")
    fun `cores acompanham o tema escuro`() {
        val dark = Surface.getColor(context)
        val darkText = OnSurface.getColor(context)
        assertNotEquals(dark, darkText)
        assertEquals(androidx.compose.ui.graphics.Color(0xFF1E1E24), dark)
    }

    @Test
    fun `cores no tema claro`() {
        assertEquals(androidx.compose.ui.graphics.Color(0xFFFFFFFF), Surface.getColor(context))
    }
}
