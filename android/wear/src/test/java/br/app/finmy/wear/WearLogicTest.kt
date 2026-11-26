package br.app.finmy.wear

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class WearLogicTest {
    private fun type(keys: String) = keys.fold("") { acc, k -> AmountInput.press(acc, k) }

    @Test
    fun `teclado monta o valor com virgula e no maximo duas casas`() {
        assertEquals("45,9", type("45,9"))
        assertEquals("45,90", type("45,901"))
        assertEquals("0,5", type(",5"))
        assertEquals("12", type("0012".drop(1)))
        assertEquals("4", type("45<"))
        assertEquals("45,", type("45,,"))
        assertEquals("123456789", type("1234567890"))
    }

    @Test
    fun `valor so vale acima de zero e a previa formata em reais`() {
        assertTrue(AmountInput.isValid("0,5"))
        assertFalse(AmountInput.isValid(""))
        assertFalse(AmountInput.isValid("0,00"))
        assertEquals("R$ 45,90", AmountInput.preview("45,9"))
        assertEquals("R$ 1.234,00", AmountInput.preview("1234"))
        assertEquals("R$ 0,00", AmountInput.preview(""))
    }

    @Test
    fun `valor compacto da complicacao`() {
        assertEquals("988", WearFormat.compact(98_850))
        assertEquals("1,2 mil", WearFormat.compact(123_456))
        assertEquals("2 mil", WearFormat.compact(200_000))
        assertEquals("12 mil", WearFormat.compact(1_234_567))
        assertEquals("-50", WearFormat.compact(-5_000))
        assertEquals("Novembro", WearFormat.monthName("2026-11"))
        assertEquals("25/11", WearFormat.shortDate("2026-11-25"))
    }

    @Test
    fun `sugestoes poem a mais recente na frente sem repetir e juntam os vencimentos do mesmo tipo`() {
        assertEquals(listOf("Mercado", "Padaria"), Suggestions.push(listOf("Padaria", "mercado"), " Mercado "))
        val upcoming = listOf(
            Upcoming("expense", "Luz", "2026-11-25", Money(12000, "R$ 120,00"), false, "1"),
            Upcoming("income", "Freela", "2026-11-28", Money(50000, "R$ 500,00"), false, "2"),
        )
        assertEquals(listOf("Mercado", "Luz"), Suggestions.merge(listOf("Mercado"), upcoming, "expense"))
    }

    @Test
    fun `resposta do celular`() {
        val ok = Reply.parse("""{"ok":true,"data":{"id":"x"}}""".encodeToByteArray())
        assertTrue(ok is Reply.Ok)
        assertEquals(Reply.Error("needs_login"), Reply.parse("""{"ok":false,"error":"needs_login"}""".encodeToByteArray()))
        assertEquals(Reply.Error("failed"), Reply.parse("lixo".encodeToByteArray()))
        assertEquals("Abra o app no celular e entre", Reply.Error("needs_login").message())
        assertEquals("Celular fora de alcance", Reply.NoPhone.message())
    }
}
