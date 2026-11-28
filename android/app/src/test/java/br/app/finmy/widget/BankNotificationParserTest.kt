package br.app.finmy.widget

import br.app.finmy.notifications.BankNotificationListener
import br.app.finmy.notifications.BankNotificationParser
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertNull
import org.junit.Test

class BankNotificationParserTest {
    @Test
    fun `compra no cartao com estabelecimento e final`() {
        val p = BankNotificationParser.parse("Compra aprovada", "Compra de R$ 45,90 aprovada em PADARIA X com cartão final 1234.")!!
        assertEquals("card_purchase", p.kind)
        assertEquals(4590, p.amountCents)
        assertEquals("Padaria X", p.description)
        assertEquals("1234", p.cardLast4)
    }

    @Test
    fun `compra no credito com valor depois do estabelecimento`() {
        val p = BankNotificationParser.parse("Cartão Sicredi", "Compra no crédito aprovada em MERCADO AZUL LTDA no valor de R$ 1.234,56. Cartão final 9876")!!
        assertEquals("card_purchase", p.kind)
        assertEquals(123456, p.amountCents)
        assertEquals("Mercado Azul Ltda", p.description)
        assertEquals("9876", p.cardLast4)
    }

    @Test
    fun `compra no debito e pix enviado sao saidas`() {
        val debito = BankNotificationParser.parse(null, "Compra no débito de R$ 12,00 em Farmácia Verde")!!
        assertEquals("expense", debito.kind)
        assertEquals("Farmácia Verde", debito.description)
        val pix = BankNotificationParser.parse("Pix enviado", "Você enviou um Pix de R$ 50,00 para Carla Souza")!!
        assertEquals("expense", pix.kind)
        assertEquals(5000, pix.amountCents)
        assertEquals("Carla Souza", pix.description)
    }

    @Test
    fun `pix recebido e entrada`() {
        val p = BankNotificationParser.parse("Pix recebido", "Você recebeu um Pix de R$ 120,00 de Léo Lima")!!
        assertEquals("income", p.kind)
        assertEquals(12000, p.amountCents)
        assertEquals("Léo Lima", p.description)
    }

    @Test
    fun `avisos que nao sao movimentacao sao ignorados`() {
        assertNull(BankNotificationParser.parse("Fatura fechada", "Sua fatura de R$ 1.200,00 fechou."))
        assertNull(BankNotificationParser.parse("Limite", "Seu limite disponível é R$ 3.000,00"))
        assertNull(BankNotificationParser.parse("Oi!", "Temos uma oferta para você"))
        assertNull(BankNotificationParser.parse("Conta", "Seu saldo é R$ 10,00"))
    }

    @Test
    fun `chave de deduplicacao depende do app, texto e minuto`() {
        val a = BankNotificationListener.dedupeKey("com.picpay", "x", 60_000)
        assertEquals(a, BankNotificationListener.dedupeKey("com.picpay", "x", 119_999))
        assertNotEquals(a, BankNotificationListener.dedupeKey("com.picpay", "y", 60_000))
        assertNotEquals(a, BankNotificationListener.dedupeKey("com.picpay", "x", 120_000))
    }
}
