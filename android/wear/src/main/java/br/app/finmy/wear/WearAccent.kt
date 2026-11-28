package br.app.finmy.wear

/** Cor de destaque do app no relógio (tema escuro): mesmos ids do celular; [main] = `--primary` no escuro. */
enum class WearAccent(val id: String, val main: Long, val deep: Long) {
    INDIGO("indigo", 0xFFB9B2FF, 0xFF4F46E5),
    AZUL("azul", 0xFF8DB8FF, 0xFF1E5FD8),
    TURQUESA("turquesa", 0xFF5FD3DE, 0xFF00818F),
    VERDE("verde", 0xFF6FDB97, 0xFF157F3C),
    AMBAR("ambar", 0xFFF5B95C, 0xFFA15C00),
    LARANJA("laranja", 0xFFFF9F6E, 0xFFC2410C),
    ROSA("rosa", 0xFFFF8FB5, 0xFFC0265E),
    ROXO("roxo", 0xFFDDA2FF, 0xFF8A2BC9);

    companion object {
        fun from(id: String?): WearAccent = entries.firstOrNull { it.id == id } ?: INDIGO
    }
}
