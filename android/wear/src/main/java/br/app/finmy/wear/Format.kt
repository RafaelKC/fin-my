package br.app.finmy.wear

/** Valor digitado no teclado do relógio: dígitos e uma vírgula, no máximo 2 casas. */
object AmountInput {
    const val MAX_DIGITS = 9

    fun press(current: String, key: Char): String = when {
        key == '<' -> current.dropLast(1)
        key == ',' -> when {
            current.contains(',') -> current
            current.isEmpty() -> "0,"
            else -> "$current,"
        }
        !key.isDigit() -> current
        current.contains(',') && current.substringAfter(',').length >= 2 -> current
        current.count { it.isDigit() } >= MAX_DIGITS -> current
        current == "0" -> key.toString()
        else -> current + key
    }

    /** Pode enviar: valor maior que zero. */
    fun isValid(text: String): Boolean = text.replace(',', '.').toDoubleOrNull()?.let { it > 0 } ?: false

    /** "45,9" → "R$ 45,90" (prévia). */
    fun preview(text: String): String {
        if (text.isEmpty()) return "R$ 0,00"
        val reais = text.substringBefore(',').ifEmpty { "0" }.trimStart('0').ifEmpty { "0" }
        val cents = text.substringAfter(',', "").padEnd(2, '0').take(2)
        val grouped = reais.reversed().chunked(3).joinToString(".").reversed()
        return "R$ $grouped,$cents"
    }
}

object WearFormat {
    private val MONTHS = listOf("Janeiro", "Fevereiro", "Março", "Abril", "Maio", "Junho", "Julho", "Agosto", "Setembro", "Outubro", "Novembro", "Dezembro")

    fun monthName(yearMonth: String) = yearMonth.substringAfter('-').toIntOrNull()?.let { MONTHS.getOrNull(it - 1) } ?: yearMonth

    /** "2026-11-25" → "25/11". */
    fun shortDate(iso: String): String = iso.split('-').let { if (it.size == 3) "${it[2]}/${it[1]}" else iso }

    /** Valor curto para complicação: 1234567 → "12,3 mil", 98765 → "988", -5000 → "-50". Em reais, sem centavos. */
    fun compact(cents: Long): String {
        val sign = if (cents < 0) "-" else ""
        val reais = kotlin.math.abs(cents) / 100
        return when {
            reais >= 1_000_000 -> sign + oneDecimal(reais / 100_000) + " mi"
            reais >= 10_000 -> sign + (reais / 1000) + " mil"
            reais >= 1_000 -> sign + oneDecimal(reais / 100) + " mil"
            else -> sign + reais
        }
    }

    private fun oneDecimal(tenths: Long) = if (tenths % 10 == 0L) "${tenths / 10}" else "${tenths / 10},${tenths % 10}"
}
