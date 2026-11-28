package br.app.finmy.notifications

import android.content.Context
import android.content.pm.PackageManager
import androidx.core.content.edit

/** App de banco conhecido: pacote → nome e código COMPE (para achar o cartão/carteira do mesmo banco). */
data class BankApp(val packageName: String, val label: String, val bank: String?)

/** Apps de banco cujas notificações são lidas (010). Por padrão, os conhecidos; a pessoa liga/desliga no Perfil. */
object BankApps {
    val KNOWN = listOf(
        BankApp("br.com.intermedium", "Inter", "077"),
        BankApp("com.picpay", "PicPay", "380"),
        BankApp("br.com.sicredimobi.smart", "Sicredi", "748"),
        BankApp("com.nu.production", "Nubank", "260"),
        BankApp("com.itau", "Itaú", "341"),
        BankApp("com.bradesco", "Bradesco", "237"),
        BankApp("br.com.bb.android", "Banco do Brasil", "001"),
        BankApp("br.com.gabba.Caixa", "Caixa", "104"),
        BankApp("com.santander.app", "Santander", "033"),
        BankApp("com.c6bank.app", "C6 Bank", "336"),
        BankApp("com.mercadopago.wallet", "Mercado Pago", "323"),
        BankApp("br.com.uol.ps.myaccount", "PagBank", "290"),
        BankApp("br.com.sicoobnet", "Sicoob", "756"),
    )

    private const val PREFS = "finmy_bank_apps"
    private const val DISABLED = "disabled"
    private const val EXTRA = "extra"

    fun find(packageName: String): BankApp? = KNOWN.firstOrNull { it.packageName == packageName }

    private fun prefs(context: Context) = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    /** Lê este app? Conhecidos: sim, salvo se desligado. Outros: só se a pessoa ligou. */
    fun isEnabled(context: Context, packageName: String): Boolean {
        val p = prefs(context)
        return if (find(packageName) != null) packageName !in p.getStringSet(DISABLED, emptySet())!!
        else packageName in p.getStringSet(EXTRA, emptySet())!!
    }

    fun setEnabled(context: Context, packageName: String, enabled: Boolean) {
        val p = prefs(context)
        if (find(packageName) != null) {
            val set = p.getStringSet(DISABLED, emptySet())!!.toMutableSet().apply { if (enabled) remove(packageName) else add(packageName) }
            p.edit { putStringSet(DISABLED, set) }
        } else {
            val set = p.getStringSet(EXTRA, emptySet())!!.toMutableSet().apply { if (enabled) add(packageName) else remove(packageName) }
            p.edit { putStringSet(EXTRA, set) }
        }
    }

    /** Apps de banco conhecidos instalados no aparelho. */
    fun installed(context: Context): List<BankApp> = KNOWN.filter { isInstalled(context.packageManager, it.packageName) }

    private fun isInstalled(pm: PackageManager, pkg: String) = try {
        pm.getPackageInfo(pkg, 0)
        true
    } catch (_: PackageManager.NameNotFoundException) {
        false
    }
}
