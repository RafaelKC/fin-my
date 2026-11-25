package br.app.finmy.widget

import android.content.Context
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.glance.GlanceId
import androidx.glance.appwidget.GlanceAppWidgetManager
import androidx.glance.appwidget.state.getAppWidgetState
import androidx.glance.appwidget.state.updateAppWidgetState
import androidx.glance.state.PreferencesGlanceStateDefinition
import kotlinx.serialization.SerializationException

/** Estado por widget (por `GlanceId`): o [WidgetSnapshot] em JSON nas preferências do Glance. */
object WidgetState {
    private val KEY = stringPreferencesKey("snapshot")

    suspend fun read(context: Context, id: GlanceId): WidgetSnapshot? {
        val prefs = getAppWidgetState(context, PreferencesGlanceStateDefinition, id)
        return decode(prefs[KEY])
    }

    suspend fun write(context: Context, id: GlanceId, snapshot: WidgetSnapshot) {
        updateAppWidgetState(context, PreferencesGlanceStateDefinition, id) {
            it.toMutablePreferences().apply { this[KEY] = WidgetJson.encodeToString(WidgetSnapshot.serializer(), snapshot) }
        }
    }

    fun decode(json: String?): WidgetSnapshot? {
        if (json == null) return null
        return try {
            WidgetJson.decodeFromString(WidgetSnapshot.serializer(), json)
        } catch (_: SerializationException) {
            null
        }
    }

    internal val snapshotKey get() = KEY

    /** Atualiza os dados de um widget e redesenha. */
    suspend fun refresh(context: Context, id: GlanceId) {
        val previous = read(context, id)
        if (previous != null) {
            write(context, id, WidgetServices.get(context).refresher.refresh(previous))
        }
        FaturaWidget().update(context, id)
    }

    /** Atualiza todos os widgets na tela (usado pelo WorkManager). */
    suspend fun refreshAll(context: Context) {
        GlanceAppWidgetManager(context).getGlanceIds(FaturaWidget::class.java).forEach { refresh(context, it) }
    }
}
