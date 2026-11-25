package br.app.finmy.widget

import android.content.Context
import androidx.work.Constraints
import androidx.work.CoroutineWorker
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.NetworkType
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.WorkerParameters
import java.util.concurrent.TimeUnit

/** Atualização periódica: uma tarefa única para todos os widgets (specs/009 research R5). */
class WidgetUpdateWorker(context: Context, params: WorkerParameters) : CoroutineWorker(context, params) {
    override suspend fun doWork(): Result {
        WidgetState.refreshAll(applicationContext)
        // Falhas de rede já viram "atualizado às HH:MM" no widget; não há o que repetir.
        return Result.success()
    }
}

object WidgetWork {
    const val UNIQUE_NAME = "finmy-widget-refresh"
    const val INTERVAL_MINUTES = 30L

    fun schedule(context: Context) {
        val request = PeriodicWorkRequestBuilder<WidgetUpdateWorker>(INTERVAL_MINUTES, TimeUnit.MINUTES)
            // Sem rede não há o que buscar: não acorda o aparelho à toa.
            .setConstraints(Constraints.Builder().setRequiredNetworkType(NetworkType.CONNECTED).build())
            .build()
        WorkManager.getInstance(context).enqueueUniquePeriodicWork(UNIQUE_NAME, ExistingPeriodicWorkPolicy.KEEP, request)
    }

    fun cancel(context: Context) {
        WorkManager.getInstance(context).cancelUniqueWork(UNIQUE_NAME)
    }
}
