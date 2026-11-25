package br.app.finmy

import android.content.ComponentName
import android.content.Intent
import android.content.pm.PackageManager
import androidx.test.core.app.ApplicationProvider
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test
import org.xmlpull.v1.XmlPullParser
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [34])
class TwaConfigTest {
    private val context = ApplicationProvider.getApplicationContext<android.app.Application>()
    private val pm: PackageManager = context.packageManager
    private val launcher = ComponentName(
        context.packageName,
        "com.google.androidbrowserhelper.trusted.LauncherActivity",
    )

    private fun activityMeta() =
        pm.getActivityInfo(launcher, PackageManager.GET_META_DATA).metaData

    @Test
    fun `applicationId e fixo`() {
        assertEquals("br.app.finmy", context.packageName)
    }

    @Test
    fun `LauncherActivity e a tela inicial do app`() {
        val main = Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_LAUNCHER).setPackage(context.packageName)
        val found = pm.queryIntentActivities(main, 0).map { it.activityInfo.name }
        assertEquals(listOf(launcher.className), found)
    }

    @Test
    fun `URL padrao da TWA vem de finmy appUrl sem barra final`() {
        val url = activityMeta().getString("android.support.customtabs.trusted.DEFAULT_URL")
        assertEquals(BuildConfig.APP_URL, url)
        assertTrue(url!!.startsWith("https://"))
        assertTrue(!url.endsWith("/"))
    }

    @Test
    fun `splash e provedor de arquivos estao configurados`() {
        val meta = activityMeta()
        assertNotEquals(0, meta.getInt("android.support.customtabs.trusted.SPLASH_IMAGE_DRAWABLE"))
        assertEquals("br.app.finmy.fileprovider", meta.getString("android.support.customtabs.trusted.FILE_PROVIDER_AUTHORITY"))
        assertNotNull(pm.resolveContentProvider("br.app.finmy.fileprovider", 0))
    }

    @Test
    fun `verificacao de ativos aponta para o site`() {
        val statements = context.getString(R.string.asset_statements)
        assertTrue(statements.contains("delegate_permission/common.handle_all_urls"))
        assertTrue(statements.contains(BuildConfig.APP_URL))
    }

    @Test
    fun `atalhos abrem o site pela LauncherActivity`() {
        assertEquals("${BuildConfig.APP_URL}/mes", context.getString(R.string.shortcut_mes_url))
        assertEquals("${BuildConfig.APP_URL}/painel", context.getString(R.string.shortcut_painel_url))
        assertNotEquals(0, pm.getActivityInfo(launcher, PackageManager.GET_META_DATA).metaData.getInt("android.app.shortcuts"))
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
