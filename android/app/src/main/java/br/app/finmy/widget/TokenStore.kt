package br.app.finmy.widget

import android.content.Context
import android.util.Base64
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import kotlinx.coroutines.flow.first
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json

/** Onde ficam os tokens do widget. */
interface TokenStore {
    suspend fun load(): Tokens?
    suspend fun save(tokens: Tokens)
    suspend fun clear()
}

private val Context.tokenDataStore by preferencesDataStore(name = "finmy_widget_tokens")

/** Tokens em DataStore, sempre cifrados por [box] (chave do Android Keystore em produção, FR-007). */
class EncryptedTokenStore(private val dataStore: DataStore<Preferences>, private val box: SecretBox) : TokenStore {
    constructor(context: Context, box: SecretBox) : this(context.applicationContext.tokenDataStore, box)

    override suspend fun load(): Tokens? {
        val encoded = dataStore.data.first()[KEY] ?: return null
        val plain = box.decrypt(Base64.decode(encoded, Base64.NO_WRAP)) ?: return null
        return try {
            WidgetJson.decodeFromString<Tokens>(plain.decodeToString())
        } catch (_: kotlinx.serialization.SerializationException) {
            null
        }
    }

    override suspend fun save(tokens: Tokens) {
        val sealed = box.encrypt(WidgetJson.encodeToString(tokens).encodeToByteArray())
        dataStore.edit { it[KEY] = Base64.encodeToString(sealed, Base64.NO_WRAP) }
    }

    override suspend fun clear() {
        dataStore.edit { it.remove(KEY) }
    }

    private companion object {
        val KEY = stringPreferencesKey("tokens")
    }
}

val WidgetJson = Json { ignoreUnknownKeys = true }
