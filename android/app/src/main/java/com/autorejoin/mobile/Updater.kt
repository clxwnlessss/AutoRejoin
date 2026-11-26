package com.autorejoin.mobile

import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.pm.PackageInstaller
import android.os.Build
import org.json.JSONArray
import java.io.File
import java.net.HttpURLConnection
import java.net.URL

/**
 * Self-update from GitHub releases tagged mobile-vX.Y (they are not "latest" — that one is the PC app).
 * With root the new APK is installed silently (pm install); without root Android shows its "Update?" dialog once.
 * After the update Events restarts the service, so the farm keeps going.
 */
object Updater {
    private const val RELEASES = "https://api.github.com/repos/clxwnlessss/AutoRejoin/releases?per_page=30"
    private const val ASSET = "AutoRejoin-Mobile.apk"
    @Volatile private var busy = false

    fun currentVersion(ctx: Context): String = try {
        ctx.packageManager.getPackageInfo(ctx.packageName, 0).versionName ?: "0"
    } catch (e: Exception) { "0" }

    private fun parts(v: String) = v.trim().removePrefix("v").split('.').map { it.toIntOrNull() ?: 0 }

    fun newer(a: String, b: String): Boolean {
        val x = parts(a); val y = parts(b)
        for (i in 0 until maxOf(x.size, y.size)) {
            val d = x.getOrElse(i) { 0 } - y.getOrElse(i) { 0 }
            if (d != 0) return d > 0
        }
        return false
    }

    /** (version, apk url) of the newest mobile release, or null. */
    fun latest(): Pair<String, String>? {
        val c = URL(RELEASES).openConnection() as HttpURLConnection
        c.connectTimeout = 15000; c.readTimeout = 15000
        c.setRequestProperty("User-Agent", "AutoRejoin-Mobile")
        c.setRequestProperty("Accept", "application/vnd.github+json")
        val arr = JSONArray(c.inputStream.bufferedReader().readText())
        var best: Pair<String, String>? = null
        for (i in 0 until arr.length()) {
            val r = arr.getJSONObject(i)
            val tag = r.optString("tag_name")
            if (!tag.startsWith("mobile-v") || r.optBoolean("draft") || r.optBoolean("prerelease")) continue
            val version = tag.removePrefix("mobile-v")
            val assets = r.optJSONArray("assets") ?: continue
            val url = (0 until assets.length()).map { assets.getJSONObject(it) }.firstOrNull { it.optString("name") == ASSET }
                ?.optString("browser_download_url") ?: continue
            if (best == null || newer(version, best.first)) best = version to url
        }
        return best
    }

    /** Checks and, if there is a newer version, downloads and installs it. Returns a line for the log or null. */
    fun checkAndInstall(ctx: Context, log: (String) -> Unit): Boolean {
        if (busy) return false
        busy = true
        try {
            val current = currentVersion(ctx)
            val (version, url) = latest() ?: return false
            if (!newer(version, current)) return false
            log("Update $current → $version: downloading…")
            val file = File(ctx.cacheDir, "update.apk")
            download(url, file)
            if (Root.available()) {
                log("Installing $version (root, silent)…")
                Root.run("cp '${file.absolutePath}' /data/local/tmp/autorejoin-update.apk && chmod 644 /data/local/tmp/autorejoin-update.apk")
                // pm runs as root: it survives our process being replaced; Events restarts the service afterwards
                Root.run("pm install -r -d /data/local/tmp/autorejoin-update.apk; rm -f /data/local/tmp/autorejoin-update.apk")
            } else {
                log("Installing $version — confirm \"Update\" in the Android dialog.")
                installWithPrompt(ctx, file)
            }
            return true
        } catch (e: Exception) {
            log("Update check: ${e.message}")
            return false
        } finally {
            busy = false
        }
    }

    private fun download(url: String, file: File) {
        var link = url
        repeat(5) {
            val c = URL(link).openConnection() as HttpURLConnection
            c.instanceFollowRedirects = false
            c.connectTimeout = 20000; c.readTimeout = 60000
            c.setRequestProperty("User-Agent", "AutoRejoin-Mobile")
            val code = c.responseCode
            if (code in 300..399) { link = c.getHeaderField("Location") ?: throw RuntimeException("redirect without Location"); return@repeat }
            if (code != 200) throw RuntimeException("download HTTP $code")
            c.inputStream.use { input -> file.outputStream().use { input.copyTo(it) } }
            return
        }
        throw RuntimeException("too many redirects")
    }

    private fun installWithPrompt(ctx: Context, file: File) {
        val installer = ctx.packageManager.packageInstaller
        val params = PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL)
        params.setAppPackageName(ctx.packageName)
        val id = installer.createSession(params)
        installer.openSession(id).use { session ->
            session.openWrite("autorejoin", 0, file.length()).use { out ->
                file.inputStream().use { it.copyTo(out) }
                session.fsync(out)
            }
            val flags = PendingIntent.FLAG_UPDATE_CURRENT or (if (Build.VERSION.SDK_INT >= 31) PendingIntent.FLAG_MUTABLE else 0)
            val pending = PendingIntent.getBroadcast(ctx, id, Intent(ctx, Events::class.java).setAction(Events.INSTALL), flags)
            session.commit(pending.intentSender)
        }
    }
}

/** Restarts the service after an update or a reboot (if it was on) and shows the "Update?" dialog when asked. */
class Events : BroadcastReceiver() {
    override fun onReceive(ctx: Context, intent: Intent) {
        when (intent.action) {
            INSTALL -> {
                when (intent.getIntExtra(PackageInstaller.EXTRA_STATUS, PackageInstaller.STATUS_FAILURE)) {
                    PackageInstaller.STATUS_PENDING_USER_ACTION -> {
                        @Suppress("DEPRECATION")
                        val confirm = intent.getParcelableExtra<Intent>(Intent.EXTRA_INTENT) ?: return
                        try { ctx.startActivity(confirm.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)) } catch (e: Exception) {
                            RejoinService.log("Update: can't show the install dialog (${e.message}). Allow \"Install unknown apps\" for AutoRejoin.")
                        }
                    }
                    PackageInstaller.STATUS_SUCCESS -> {}
                    else -> RejoinService.log("Update failed: ${intent.getStringExtra(PackageInstaller.EXTRA_STATUS_MESSAGE)}")
                }
            }
            Intent.ACTION_MY_PACKAGE_REPLACED, Intent.ACTION_BOOT_COMPLETED -> {
                if (Store.get(ctx, "running") != "1") return
                val start = Intent(ctx, RejoinService::class.java)
                try {
                    if (Build.VERSION.SDK_INT >= 26) ctx.startForegroundService(start) else ctx.startService(start)
                } catch (e: Exception) {
                    RejoinService.log("Restart after update: ${e.message}")
                }
            }
        }
    }

    companion object {
        const val INSTALL = "com.autorejoin.mobile.INSTALL"
    }
}
