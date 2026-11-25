package br.app.finmy

import android.content.ComponentName
import android.content.Intent
import android.content.pm.PackageManager
import androidx.test.core.app.ApplicationProvider
import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import org.xmlpull.v1.XmlPullParser

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [34])
class AppConfigTest {
    private val context = ApplicationProvider.getApplicationContext<android.app.Application>()
    private val pm: PackageManager = context.packageManager
    private val main = ComponentName(context.packageName, MainActivity::class.java.name)

    @Test
    fun `applicationId e fixo (atualiza a instalacao da TWA)`() {
        assertEquals("br.app.finmy", context.packageName)
    }

    @Test
    fun `MainActivity (Capacitor) e a tela inicial do app`() {
        val launcher = Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER).setPackage(context.packageName)
        assertEquals(listOf(main.className), pm.queryIntentActivities(launcher, 0).map { it.activityInfo.name })
    }

    @Test
    fun `capacitor config abre o site de finmy appUrl`() {
        val json = JSONObject(context.assets.open("capacitor.config.json").bufferedReader().readText())
        val server = json.getJSONObject("server")
        assertEquals(BuildConfig.APP_URL, server.getString("url"))
        assertTrue(BuildConfig.APP_URL.startsWith("https://") && !BuildConfig.APP_URL.endsWith("/"))
        assertEquals("br.app.finmy", json.getString("appId"))
        assertEquals("offline.html", server.getString("errorPath"))
        assertTrue(context.assets.list("public")!!.contains("offline.html"))
    }

    @Test
    fun `atalhos abrem o site pela MainActivity`() {
        assertEquals("${BuildConfig.APP_URL}/mes", context.getString(R.string.shortcut_mes_url))
        assertEquals("${BuildConfig.APP_URL}/painel", context.getString(R.string.shortcut_painel_url))
        assertNotEquals(0, pm.getActivityInfo(main, PackageManager.GET_META_DATA).metaData.getInt("android.app.shortcuts"))
    }

    @Test
    fun `build debug confia em CA de usuario (certificado da VPN)`() {
        assertTrue(BuildConfig.DEBUG)
        val id = context.resources.getIdentifier("network_security_config", "xml", context.packageName)
        assertNotEquals(0, id)
        val parser = context.resources.getXml(id)
        val sources = mutableListOf<String>()
        while (parser.next() != XmlPullParser.END_DOCUMENT) {
            if (parser.eventType == XmlPullParser.START_TAG && parser.name == "certificates") {
                sources += parser.getAttributeValue(null, "src")
            }
        }
        assertTrue(sources.containsAll(listOf("system", "user")))
    }
}
