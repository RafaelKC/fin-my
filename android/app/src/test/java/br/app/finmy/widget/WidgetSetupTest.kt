package br.app.finmy.widget

import android.content.ComponentName
import android.content.Intent
import android.net.Uri
import androidx.test.core.app.ApplicationProvider
import androidx.work.NetworkType
import androidx.work.WorkInfo
import androidx.work.WorkManager
import androidx.work.testing.TestListenableWorkerBuilder
import androidx.work.testing.WorkManagerTestInitHelper
import br.app.finmy.R
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import org.xmlpull.v1.XmlPullParser
import java.util.concurrent.TimeUnit

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [34])
class WidgetSetupTest {
    private val context = ApplicationProvider.getApplicationContext<android.app.Application>()
    private val pm = context.packageManager

    @Test
    fun `receiver do widget e a configuracao estao declarados`() {
        val update = Intent("android.appwidget.action.APPWIDGET_UPDATE").setPackage(context.packageName)
        assertEquals(
            listOf(FaturaWidgetReceiver::class, MesWidgetReceiver::class, VencimentosWidgetReceiver::class, AcertosWidgetReceiver::class, LancarWidgetReceiver::class)
                .map { it.java.name }.sorted(),
            pm.queryBroadcastReceivers(update, 0).map { it.activityInfo.name }.sorted(),
        )

        val configure = Intent("android.appwidget.action.APPWIDGET_CONFIGURE").setPackage(context.packageName)
        assertEquals(listOf(WidgetConfigActivity::class.java.name), pm.queryIntentActivities(configure, 0).map { it.activityInfo.name })
    }

    @Test
    fun `provider do widget e 2x2 redimensionavel, reconfiguravel e sem update periodico do sistema`() {
        val parser = context.resources.getXml(R.xml.fatura_widget_info)
        while (parser.eventType != XmlPullParser.START_TAG) parser.next()
        fun attr(name: String) = parser.getAttributeValue("http://schemas.android.com/apk/res/android", name)

        assertEquals(WidgetConfigActivity::class.java.name, attr("configure"))
        assertEquals("2", attr("targetCellWidth"))
        assertEquals("2", attr("targetCellHeight"))
        assertEquals("0", attr("updatePeriodMillis")) // a atualização é do WorkManager (30 min)
        val ns = "http://schemas.android.com/apk/res/android"
        assertEquals(3, parser.getAttributeIntValue(ns, "resizeMode", 0)) // horizontal|vertical
        assertEquals(1, parser.getAttributeIntValue(ns, "widgetFeatures", 0)) // reconfigurable
    }

    @Test
    fun `WorkManager atualiza a cada 30 minutos em uma tarefa unica, so com rede`() {
        WorkManagerTestInitHelper.initializeTestWorkManager(context)
        WidgetWork.schedule(context)
        WidgetWork.schedule(context) // idempotente: outro widget não duplica a tarefa

        val infos = WorkManager.getInstance(context).getWorkInfosForUniqueWork(WidgetWork.UNIQUE_NAME).get()
        assertEquals(1, infos.size)
        val info = infos.single()
        assertEquals(TimeUnit.MINUTES.toMillis(30), info.periodicityInfo?.repeatIntervalMillis)
        assertEquals(NetworkType.CONNECTED, info.constraints.requiredNetworkType)

        WidgetWork.cancel(context)
        val after = WorkManager.getInstance(context).getWorkInfosForUniqueWork(WidgetWork.UNIQUE_NAME).get()
        assertEquals(WorkInfo.State.CANCELLED, after.single().state)
    }

    @Test
    fun `worker sem widgets na tela termina com sucesso`() = runBlocking {
        val worker = TestListenableWorkerBuilder<WidgetUpdateWorker>(context).build()
        assertEquals(androidx.work.ListenableWorker.Result.success(), worker.doWork())
    }
}
