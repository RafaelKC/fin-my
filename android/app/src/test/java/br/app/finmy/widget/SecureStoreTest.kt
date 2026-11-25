package br.app.finmy.widget

import androidx.test.core.app.ApplicationProvider
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertNull
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import javax.crypto.KeyGenerator

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [34])
class SecureStoreTest {
    private val context = ApplicationProvider.getApplicationContext<android.app.Application>()

    private fun softwareKey() = KeyGenerator.getInstance("AES").apply { init(256) }.generateKey()

    @Test
    fun `AES-GCM cifra com IV novo a cada vez e decifra`() {
        val key = softwareKey()
        val box = AesGcmSecretBox { key }
        val plain = "segredo".encodeToByteArray()

        val a = box.encrypt(plain)
        val b = box.encrypt(plain)

        assertNotEquals(a.toList(), b.toList())
        assertFalse(String(a).contains("segredo"))
        assertArrayEquals(plain, box.decrypt(a))
    }

    @Test
    fun `dado adulterado ou chave diferente nao decifra`() {
        val box = AesGcmSecretBox(softwareKey().let { { it } })
        val sealed = box.encrypt("x".repeat(40).encodeToByteArray())
        sealed[sealed.size - 1] = (sealed.last() + 1).toByte()

        assertNull(box.decrypt(sealed))
        assertNull(AesGcmSecretBox(softwareKey().let { { it } }).decrypt(box.encrypt("y".encodeToByteArray())))
        assertNull(box.decrypt(ByteArray(3)))
    }

    private fun dataStore(file: java.io.File) = androidx.datastore.preferences.core.PreferenceDataStoreFactory.create { file }

    @Test
    fun `tokens vao cifrados para o DataStore e voltam iguais`() = runTest {
        val key = softwareKey()
        val file = java.io.File(context.cacheDir, "tokens-${System.nanoTime()}.preferences_pb")
        val store = EncryptedTokenStore(dataStore(file), AesGcmSecretBox { key })
        val tokens = Tokens("acesso-secreto", "refresh-secreto", 123L)

        store.save(tokens)

        assertEquals(tokens, store.load())
        // O que está no arquivo não pode conter os tokens em claro.
        val raw = file.readBytes().decodeToString()
        assertFalse(raw.contains("acesso-secreto"))
        assertFalse(raw.contains("refresh-secreto"))

        store.clear()
        assertNull(store.load())
    }

    @Test
    fun `chave perdida equivale a sem sessao`() = runTest {
        val file = java.io.File(context.cacheDir, "tokens-${System.nanoTime()}.preferences_pb")
        val data = dataStore(file)
        EncryptedTokenStore(data, AesGcmSecretBox(softwareKey().let { { it } })).save(Tokens("a", "b", 1))
        assertNull(EncryptedTokenStore(data, AesGcmSecretBox(softwareKey().let { { it } })).load())
    }
}
