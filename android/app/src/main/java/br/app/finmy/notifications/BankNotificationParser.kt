package br.app.finmy.notifications

import java.util.Locale

/** O que dá para tirar de uma notificação de banco: tipo, valor, estabelecimento/pessoa e final do cartão. */
data class ParsedNotification(val kind: String, val amountCents: Long, val description: String, val cardLast4: String?)

/**
 * Extrator genérico das notificações de banco (010): valor "R$ 1.234,56", tipo por palavras-chave (compra no cartão,
 * Pix/pagamento enviado, recebimento) e o nome depois de "em"/"para"/"de". Ignora avisos que não são movimentação
 * (fatura, limite, boleto disponível, promoções).
 */
object BankNotificationParser {
    private val AMOUNT = Regex("""R\$\s?(\d{1,3}(?:\.\d{3})*|\d+),(\d{2})""")
    private val LAST4 = Regex("""(?:final|terminado em|\*{2,}\s?)\s*(\d{4})""", RegexOption.IGNORE_CASE)
    private val IGNORE = Regex(
        """fatura|limite|boleto dispon|vence |vencimento|oferta|promo|cashback dispon|investimento|rendeu|token|c[oó]digo de seguran""",
        RegexOption.IGNORE_CASE,
    )
    private val INCOME = Regex("""recebe(u|ido|mos)|caiu na sua conta|dep[oó]sito|estorno|reembolso|cr[eé]dito em conta""", RegexOption.IGNORE_CASE)
    private val CARD = Regex("""compra|cart[aã]o""", RegexOption.IGNORE_CASE)
    private val DEBIT = Regex("""d[eé]bito""", RegexOption.IGNORE_CASE)
    private val EXPENSE = Regex("""pix enviado|enviou|transfer[eê]ncia (enviada|realizada)|pagamento|pagou|saque|d[eé]bito""", RegexOption.IGNORE_CASE)
    private val STOP = Regex("""\s+(com|no|na|nos|às|as|via|usando|aprovad[ao]|realizad[ao]|foi)\s|[.,:;!\n]|\s+cart[aã]o|\s+final|\s+\d{2}/\d{2}""", RegexOption.IGNORE_CASE)

    fun parse(title: String?, text: String?): ParsedNotification? {
        val full = listOfNotNull(title, text).joinToString(". ").replace(' ', ' ')
        if (full.isBlank() || IGNORE.containsMatchIn(full)) return null
        val amount = AMOUNT.find(full) ?: return null
        val cents = amount.groupValues[1].replace(".", "").toLong() * 100 + amount.groupValues[2].toLong()
        if (cents <= 0) return null

        val kind = when {
            INCOME.containsMatchIn(full) -> "income"
            CARD.containsMatchIn(full) && !DEBIT.containsMatchIn(full) -> "card_purchase"
            EXPENSE.containsMatchIn(full) || CARD.containsMatchIn(full) -> "expense"
            else -> return null
        }
        val keyword = when (kind) {
            "income" -> listOf(" de ", " do ", " da ")
            else -> listOf(" em ", " para ", " pra ", " no estabelecimento ")
        }
        val description = after(full.substring(amount.range.last + 1), keyword)
            ?: after(full.substring(0, amount.range.first), keyword)
            ?: title?.trim().orEmpty()
        return ParsedNotification(kind, cents, pretty(description), LAST4.find(full)?.groupValues?.get(1))
    }

    private fun after(text: String, keywords: List<String>): String? {
        val lower = text.lowercase(Locale.ROOT)
        val hit = keywords.mapNotNull { k -> lower.indexOf(k).takeIf { it >= 0 }?.let { it + k.length } }.minOrNull() ?: return null
        val rest = text.substring(hit)
        val end = STOP.find(rest)?.range?.first ?: rest.length
        return rest.substring(0, end).trim().takeIf { it.length >= 2 && !it.startsWith("R$") }
    }

    /** "PADARIA X LTDA" → "Padaria X Ltda"; mantém o que já vem com caixa misturada. */
    private fun pretty(raw: String): String {
        val text = raw.replace(Regex("""\s+"""), " ").trim().take(60)
        if (text.any { it.isLowerCase() }) return text
        return text.lowercase(Locale.ROOT).split(' ').joinToString(" ") { w ->
            if (w.length <= 2 && w in setOf("de", "da", "do", "e")) w else w.replaceFirstChar { it.titlecase(Locale.ROOT) }
        }
    }
}
