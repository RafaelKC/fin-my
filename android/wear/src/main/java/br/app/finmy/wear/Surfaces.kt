package br.app.finmy.wear

import android.app.PendingIntent
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.graphics.drawable.Icon
import androidx.wear.protolayout.ActionBuilders
import androidx.wear.protolayout.ColorBuilders.argb
import androidx.wear.protolayout.ModifiersBuilders
import androidx.wear.protolayout.ResourceBuilders
import androidx.wear.protolayout.TimelineBuilders
import androidx.wear.protolayout.material.ChipColors
import androidx.wear.protolayout.material.CompactChip
import androidx.wear.protolayout.material.Text
import androidx.wear.protolayout.material.Typography
import androidx.wear.protolayout.material.layouts.PrimaryLayout
import androidx.wear.tiles.RequestBuilders
import androidx.wear.tiles.TileBuilders
import androidx.wear.tiles.TileService
import androidx.wear.watchface.complications.data.ComplicationData
import androidx.wear.watchface.complications.data.ComplicationType
import androidx.wear.watchface.complications.data.MonochromaticImage
import androidx.wear.watchface.complications.data.MonochromaticImageComplicationData
import androidx.wear.watchface.complications.data.PlainComplicationText
import androidx.wear.watchface.complications.data.ShortTextComplicationData
import androidx.wear.watchface.complications.datasource.ComplicationDataSourceUpdateRequester
import androidx.wear.watchface.complications.datasource.ComplicationRequest
import androidx.wear.watchface.complications.datasource.SuspendingComplicationDataSourceService
import com.google.common.util.concurrent.Futures
import com.google.common.util.concurrent.ListenableFuture
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch

private fun lancarIntent(context: Context) =
    Intent(context, MainActivity::class.java).putExtra(MainActivity.EXTRA_LANCAR, true)

private fun pending(context: Context, intent: Intent, code: Int): PendingIntent =
    PendingIntent.getActivity(context, code, intent, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)

/** Pede para o Tile e as complicações se redesenharem (após atualizar o resumo ou lançar). */
object TileAndComplications {
    fun requestUpdate(context: Context) {
        TileService.getUpdater(context).requestUpdate(FinMyTileService::class.java)
        ComplicationDataSourceUpdateRequester.create(context, ComponentName(context, SaldoComplicationService::class.java)).requestUpdateAll()
    }
}

/** Tile (010): mês, saldo previsto e o botão Lançar. Lê o último resumo guardado e pede um novo ao celular. */
class FinMyTileService : TileService() {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    override fun onTileRequest(requestParams: RequestBuilders.TileRequest): ListenableFuture<TileBuilders.Tile> {
        val link = PhoneLink(this)
        val overview = link.cachedOverview()
        // Busca um resumo novo em segundo plano; quando chegar, o Tile é redesenhado.
        scope.launch {
            val before = overview
            val (after, _) = link.overview()
            if (after != null && after != before) TileService.getUpdater(this@FinMyTileService).requestUpdate(FinMyTileService::class.java)
        }

        val device = requestParams.deviceConfiguration
        val lancar = ModifiersBuilders.Clickable.Builder()
            .setId("lancar")
            .setOnClick(
                ActionBuilders.LaunchAction.Builder()
                    .setAndroidActivity(
                        ActionBuilders.AndroidActivity.Builder()
                            .setPackageName(packageName)
                            .setClassName(MainActivity::class.java.name)
                            .addKeyToExtraMapping(MainActivity.EXTRA_LANCAR, ActionBuilders.booleanExtra(true))
                            .build(),
                    )
                    .build(),
            )
            .build()
        val final = overview?.plannedFinal
        val layout = PrimaryLayout.Builder(device)
            .setResponsiveContentInsetEnabled(true)
            .setPrimaryLabelTextContent(
                Text.Builder(this, overview?.let { WearFormat.monthName(it.month) } ?: "Fin-My")
                    .setTypography(Typography.TYPOGRAPHY_CAPTION1)
                    .setColor(argb(0xFFB9B2FF.toInt()))
                    .build(),
            )
            .setContent(
                Text.Builder(this, final?.text ?: "Abra o app")
                    .setTypography(if (final != null) Typography.TYPOGRAPHY_DISPLAY3 else Typography.TYPOGRAPHY_BODY1)
                    .setColor(argb(if ((final?.cents ?: 0) < 0) 0xFFFF8A80.toInt() else 0xFFFFFFFF.toInt()))
                    .setMaxLines(1)
                    .build(),
            )
            .setSecondaryLabelTextContent(
                Text.Builder(this, "Saldo previsto do mês")
                    .setTypography(Typography.TYPOGRAPHY_CAPTION2)
                    .setColor(argb(0xFFA9A9B4.toInt()))
                    .build(),
            )
            .setPrimaryChipContent(
                CompactChip.Builder(this, "+ Lançar", lancar, device)
                    .setChipColors(ChipColors(argb(0xFFB9B2FF.toInt()), argb(0xFF17171C.toInt())))
                    .build(),
            )
            .build()
        return Futures.immediateFuture(
            TileBuilders.Tile.Builder()
                .setResourcesVersion(RESOURCES_VERSION)
                .setFreshnessIntervalMillis(30 * 60 * 1000L)
                .setTileTimeline(TimelineBuilders.Timeline.fromLayoutElement(layout))
                .build(),
        )
    }

    override fun onTileResourcesRequest(requestParams: RequestBuilders.ResourcesRequest): ListenableFuture<ResourceBuilders.Resources> =
        Futures.immediateFuture(ResourceBuilders.Resources.Builder().setVersion(RESOURCES_VERSION).build())

    private companion object {
        const val RESOURCES_VERSION = "1"
    }
}

/** Complicação com o saldo previsto do mês (texto curto). Toque abre o app. */
class SaldoComplicationService : SuspendingComplicationDataSourceService() {
    override fun getPreviewData(type: ComplicationType): ComplicationData? = saldo("1,2 mil", "Saldo", null)

    override suspend fun onComplicationRequest(request: ComplicationRequest): ComplicationData? {
        val link = PhoneLink(this)
        val overview = link.overview().first ?: link.cachedOverview()
        val text = overview?.plannedFinal?.cents?.let(WearFormat::compact) ?: "—"
        val tap = pending(this, Intent(this, MainActivity::class.java), 1)
        return saldo(text, overview?.let { WearFormat.monthName(it.month).take(3) } ?: "Saldo", tap)
    }

    private fun saldo(text: String, title: String, tap: PendingIntent?) =
        ShortTextComplicationData.Builder(PlainComplicationText.Builder(text).build(), PlainComplicationText.Builder("Saldo previsto: $text").build())
            .setTitle(PlainComplicationText.Builder(title).build())
            .setTapAction(tap)
            .build()
}

/** Complicação "Lançar": um toque no mostrador abre direto o lançamento rápido. */
class LancarComplicationService : SuspendingComplicationDataSourceService() {
    override fun getPreviewData(type: ComplicationType): ComplicationData? = build(type, null)

    override suspend fun onComplicationRequest(request: ComplicationRequest): ComplicationData? =
        build(request.complicationType, pending(this, lancarIntent(this), 2))

    private fun build(type: ComplicationType, tap: PendingIntent?): ComplicationData? {
        val icon = MonochromaticImage.Builder(Icon.createWithResource(this, R.drawable.ic_add)).build()
        val description = PlainComplicationText.Builder("Lançar no Fin-My").build()
        return when (type) {
            ComplicationType.MONOCHROMATIC_IMAGE -> MonochromaticImageComplicationData.Builder(icon, description).setTapAction(tap).build()
            ComplicationType.SHORT_TEXT -> ShortTextComplicationData.Builder(PlainComplicationText.Builder("Lançar").build(), description)
                .setMonochromaticImage(icon)
                .setTapAction(tap)
                .build()
            else -> null
        }
    }
}
