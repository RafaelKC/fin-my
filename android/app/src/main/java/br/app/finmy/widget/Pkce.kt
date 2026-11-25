package br.app.finmy.widget

import java.security.MessageDigest
import java.security.SecureRandom
import java.util.Base64

/** Par PKCE S256 e `state` de um pedido de autorização (RFC 7636). */
class Pkce private constructor(val verifier: String, val challenge: String, val state: String) {
    companion object {
        private val random = SecureRandom()
        private val encoder = Base64.getUrlEncoder().withoutPadding()

        fun create(): Pkce {
            val verifier = token(32)
            val challenge = encoder.encodeToString(MessageDigest.getInstance("SHA-256").digest(verifier.toByteArray(Charsets.US_ASCII)))
            return Pkce(verifier, challenge, token(16))
        }

        private fun token(bytes: Int) = encoder.encodeToString(ByteArray(bytes).also(random::nextBytes))
    }
}
