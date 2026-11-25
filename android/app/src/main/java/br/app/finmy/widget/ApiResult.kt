package br.app.finmy.widget

/** Resultado de uma chamada ao widget; cada caso vira um estado do widget (spec US2, cenários 6 a 8). */
sealed interface ApiResult<out T> {
    data class Ok<T>(val value: T) : ApiResult<T>

    /** Sem sessão válida (sem tokens, refresh recusado ou 401 mesmo após renovar): "Entre de novo". */
    data object NeedsLogin : ApiResult<Nothing>

    /** 404 `credit_card.not_found`: cartão arquivado, excluído ou de outro Espaço. */
    data object CardUnavailable : ApiResult<Nothing>

    /** Sem rede (ou o servidor não respondeu): o widget mantém os últimos valores. */
    data object Offline : ApiResult<Nothing>

    /** Qualquer outra falha (5xx, corpo inesperado): tratada como indisponível por agora. */
    data class Failure(val message: String) : ApiResult<Nothing>
}
