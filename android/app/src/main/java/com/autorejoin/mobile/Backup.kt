package com.autorejoin.mobile

import android.content.Context
import android.os.Environment
import org.json.JSONObject
import java.io.File

/**
 * Automatic backup of accounts and settings, so a reinstall doesn't mean typing everything again.
 *  - On the phone: Documents/AutoRejoin/backup.json (kept when the app is removed; read with root if Android hides it).
 *  - In the cloud, when signed in (Top AFK & bots): restored right after signing in on a fresh install.
 * Restored only into an app that has nothing set up yet — never over what the user has.
 */
object Backup {
    private const val PREFS = "autorejoin"
    private const val DIR = "Documents/AutoRejoin"
    private const val FILE = "backup.json"
    // per-install or secret: not carried over
    private val SKIP = setOf("cloudToken", "cloudNick", "deviceId", "running", "accountsManaged", "userEdited", "lastCloudBackup")

    private fun file() = File(Environment.getExternalStorageDirectory(), "$DIR/$FILE")

    fun export(ctx: Context): JSONObject {
        val prefs = JSONObject()
        for ((k, v) in ctx.getSharedPreferences(PREFS, 0).all) if (k !in SKIP && v is String) prefs.put(k, v)
        return JSONObject().put("app", "AutoRejoin Mobile").put("version", Updater.currentVersion(ctx))
            .put("savedAt", System.currentTimeMillis()).put("prefs", prefs)
    }

    /** True when the backup has accounts. */
    fun apply(ctx: Context, data: JSONObject): Boolean {
        val prefs = data.optJSONObject("prefs") ?: return false
        val accounts = try { org.json.JSONArray(prefs.optString("accounts", "[]")) } catch (e: Exception) { return false }
        if (accounts.length() == 0) return false
        val e = ctx.getSharedPreferences(PREFS, 0).edit()
        for (k in prefs.keys()) if (k !in SKIP) e.putString(k, prefs.optString(k))
        e.putString("accountsManaged", "1").putString("userEdited", "1").apply()
        return true
    }

    /** The user hasn't set anything up on this install yet (fresh install or never saved). */
    fun untouched(ctx: Context) = Store.get(ctx, "userEdited", "0") != "1"

    fun markEdited(ctx: Context) = Store.put(ctx, "userEdited", "1")

    /** Called whenever accounts or settings are saved: phone file now, cloud copy in the background. */
    fun save(ctx: Context) {
        markEdited(ctx)
        val data = export(ctx)
        Thread {
            writeLocal(ctx, data.toString(2))
            uploadCloud(ctx, data)
        }.start()
    }

    private fun writeLocal(ctx: Context, text: String) {
        try {
            val f = file()
            f.parentFile?.mkdirs()
            f.writeText(text)
            return
        } catch (_: Exception) {}
        if (!Root.available()) return
        try {
            val tmp = File(ctx.cacheDir, FILE).apply { writeText(text) }
            val target = "/sdcard/$DIR"
            Root.run("mkdir -p '$target' && cp '${tmp.absolutePath}' '$target/$FILE' && chmod 666 '$target/$FILE'")
        } catch (_: Exception) {}
    }

    private fun readLocal(): JSONObject? {
        val text = try { file().takeIf { it.canRead() }?.readText() } catch (e: Exception) { null }
            ?: if (Root.available()) Root.run("cat '/sdcard/$DIR/$FILE' 2>/dev/null") else null
        return try { if (text.isNullOrBlank()) null else JSONObject(text) } catch (e: Exception) { null }
    }

    /** First start of a fresh install: take the phone backup, if there is one. */
    fun restoreLocalIfFresh(ctx: Context): Boolean {
        if (!untouched(ctx) || Store.accounts(ctx).isNotEmpty() || Store.get(ctx, "accountsManaged", "0") == "1") return false
        val data = readLocal() ?: return false
        val ok = apply(ctx, data)
        if (ok) RejoinService.log("Accounts and settings restored from the phone backup.")
        return ok
    }

    // ------------------------------------------------------------------ cloud (signed in)
    private fun uploadCloud(ctx: Context, data: JSONObject) {
        if (!Cloud.signedIn(ctx)) return
        val body = data.toString()
        val hash = body.replace(Regex("\"savedAt\":\\d+"), "").hashCode().toString()
        if (hash == Store.get(ctx, "lastCloudBackup")) return
        try {
            Cloud.call("PUT", "/api/mobile/backup", JSONObject().put("data", body), Cloud.token(ctx), 20000)
            Store.put(ctx, "lastCloudBackup", hash)
        } catch (_: Exception) {}
    }

    /** The backup kept in the AutoRejoin account, or null. */
    fun cloudBackup(ctx: Context): JSONObject? = try {
        val text = Cloud.call("GET", "/api/mobile/backup", null, Cloud.token(ctx), 20000).optString("data")
        if (text.isBlank()) null else JSONObject(text)
    } catch (e: Exception) { null }

    fun accountCount(data: JSONObject): Int = try {
        org.json.JSONArray(data.optJSONObject("prefs")?.optString("accounts", "[]") ?: "[]").length()
    } catch (e: Exception) { 0 }

    /** Right after signing in. RESTORED: an untouched install took the cloud backup. ASK: this phone has its own
     *  setup and the account has a different one — the app asks. NONE: nothing to restore (this phone's setup goes up). */
    sealed class AfterSignIn { object Restored : AfterSignIn(); class Ask(val data: JSONObject) : AfterSignIn(); object None : AfterSignIn() }
    fun syncAfterSignIn(ctx: Context): AfterSignIn {
        val cloud = cloudBackup(ctx)
        if (cloud != null && accountCount(cloud) > 0) {
            if (untouched(ctx)) {
                if (apply(ctx, cloud)) { RejoinService.log("Accounts and settings restored from your AutoRejoin account."); return AfterSignIn.Restored }
            } else {
                val mine = export(ctx).optJSONObject("prefs")?.optString("accounts")
                if (mine != cloud.optJSONObject("prefs")?.optString("accounts")) return AfterSignIn.Ask(cloud)
            }
        }
        if (!untouched(ctx)) uploadCloud(ctx, export(ctx))
        return AfterSignIn.None
    }

    fun uploadNow(ctx: Context) = uploadCloud(ctx, export(ctx))
}
