package br.app.finmy.widget

import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock

/**
 * Entrega um access token válido ao cliente HTTP, renovando quando preciso. O mutex serializa as renovações:
 * com rotação de refresh token (008), duas renovações simultâneas derrubariam a sessão.
 */
class AuthSession(
    private val store: TokenStore,
    private val tokenClient: TokenClient,
    private val clock: () -> Long = System::currentTimeMillis,
) {
    private val mutex = Mutex()

    suspend fun hasSession(): Boolean = store.load() != null

    /**
     * @param rejectedToken access token que o servidor acabou de recusar (401): força a renovação, a menos que outra
     * chamada já tenha trocado o token enquanto isso.
     */
    suspend fun accessToken(rejectedToken: String? = null): ApiResult<String> = mutex.withLock {
        val tokens = store.load() ?: return ApiResult.NeedsLogin
        val usable = if (rejectedToken == null) {
            tokens.expiresAtMillis - clock() > EXPIRY_MARGIN_MILLIS
        } else {
            tokens.accessToken != rejectedToken
        }
        if (usable) return ApiResult.Ok(tokens.accessToken)

        val refreshToken = tokens.refreshToken
        if (refreshToken == null) {
            store.clear()
            return ApiResult.NeedsLogin
        }
        when (val renewed = tokenClient.refresh(refreshToken)) {
            is ApiResult.Ok -> {
                store.save(renewed.value)
                ApiResult.Ok(renewed.value.accessToken)
            }
            ApiResult.NeedsLogin -> {
                store.clear()
                ApiResult.NeedsLogin
            }
            ApiResult.Offline -> ApiResult.Offline
            is ApiResult.Failure -> renewed
            ApiResult.CardUnavailable -> ApiResult.Failure("inesperado")
        }
    }

    /** Guarda a sessão recém-criada pelo login. */
    suspend fun signIn(tokens: Tokens) = mutex.withLock { store.save(tokens) }

    /** Esquece a sessão (o servidor recusou mesmo após renovar). */
    suspend fun signOut() = mutex.withLock { store.clear() }

    private companion object {
        const val EXPIRY_MARGIN_MILLIS = 60_000L
    }
}
