package com.autorejoin.mobile

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.net.Uri
import android.os.Build
import android.os.IBinder
import android.os.PowerManager
import org.json.JSONObject
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * Keeps every Roblox app in game.
 *  - With root: watches the process and the Roblox log (same format as on PC): closed app, kick code, or out of
 *    a server longer than the grace -> force-stop and open the game again.
 *  - Without root: asks Roblox's public presence API (no cookie) whether the account is in game; not in game for
 *    longer than the grace -> opens the game again through a roblox:// link.
 */
class RejoinService : Service() {

    private class State(val acc: Account) {
        var launchedAt = 0L
        var inGame = false
        var everJoined = false
        var outSince = System.currentTimeMillis()
        var kick: Int? = null
        var logPath: String? = null
        var logPos = 0L
        var logDir: String? = null
        var rejoins = 0
        var fails = 0
        var nextLaunch = 0L
        var adopted = false
        var lastType = -1
    }

    private var worker: Thread? = null
    @Volatile private var running = false
    private var wake: PowerManager.WakeLock? = null

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == ACTION_STOP) {
            stopSelf()
            return START_NOT_STICKY
        }
        startForegroundCompat("Starting…")
        if (!running) {
            running = true
            wake = (getSystemService(Context.POWER_SERVICE) as PowerManager).newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "AutoRejoin:loop").apply { acquire() }
            worker = Thread({ loop() }, "rejoin").apply { start() }
        }
        isRunning = true
        return START_STICKY
    }

    override fun onDestroy() {
        running = false
        isRunning = false
        total = 0
        worker?.interrupt()
        try { wake?.release() } catch (_: Exception) {}
        log("Stopped.")
        super.onDestroy()
    }

    private fun loop() {
        val accounts = Store.accounts(this).filter { it.enabled }
        if (accounts.isEmpty()) { log("No accounts — add them on the main screen."); stopSelf(); return }
        Root.recheck()
        val root = Root.available()
        val common = Store.get(this, "link", Roblox.BEE_SWARM)
        val grace = (Store.get(this, "grace", "20").toLongOrNull() ?: 20).coerceIn(5, 600) * 1000
        val joinTimeout = (Store.get(this, "joinTimeout", "60").toLongOrNull() ?: 60).coerceIn(20, 600) * 1000
        val states = accounts.map { State(it) }
        log(if (root) "Root mode: watching ${states.size} app(s) (process + Roblox log)." else "No root: watching ${states.size} account(s) through Roblox presence.")
        // usernames -> user IDs (needed without root)
        for (s in states) {
            if (s.acc.userId == 0L && s.acc.username.isNotBlank()) {
                try { s.acc.userId = Roblox.userId(s.acc.username.trim()); saveIds(states) } catch (e: Exception) { log("${name(s)}: ${e.message}") }
            }
            if (!root && s.acc.userId == 0L) log("${name(s)}: no Roblox username — can't watch it without root.")
            if (root && alive(s.acc.pkg)) { s.adopted = true; s.launchedAt = System.currentTimeMillis(); log("${name(s)}: already open — watching.") }
        }
        var lastLaunch = 0L
        var nextUpdateCheck = System.currentTimeMillis() + 60_000
        while (running) {
            if (System.currentTimeMillis() >= nextUpdateCheck) {
                nextUpdateCheck = System.currentTimeMillis() + 30 * 60_000L
                if (Store.get(this, "autoUpdate", "1") == "1") Thread { Updater.checkAndInstall(this) { log(it) } }.start()
            }
            try {
                val presence = if (root) emptyMap() else try {
                    Roblox.presence(states.map { it.acc.userId }.filter { it > 0 })
                } catch (e: Exception) { log("Presence: ${e.message}"); null }
                for (s in states) {
                    if (!running) break
                    val reason = if (root) rootReason(s, joinTimeout, grace) else presenceReason(s, presence, joinTimeout, grace)
                    if (reason == null) continue
                    val now = System.currentTimeMillis()
                    if (now < s.nextLaunch || now - lastLaunch < 4000) continue
                    launch(s, reason, root, common)
                    lastLaunch = System.currentTimeMillis()
                }
                val inGame = states.count { it.inGame }
                Companion.inGame = inGame
                total = states.size
                rejoinsTotal = states.sumOf { it.rejoins }
                updateNotification("$inGame of ${states.size} in game")
            } catch (e: InterruptedException) {
                break
            } catch (e: Exception) {
                log("Error: ${e.message}")
            }
            try { Thread.sleep(if (root) 3000 else 8000) } catch (e: InterruptedException) { break }
        }
    }

    private fun saveIds(states: List<State>) {
        val all = Store.accounts(this)
        for (a in all) states.firstOrNull { it.acc.pkg == a.pkg }?.let { a.userId = it.acc.userId }
        Store.saveAccounts(this, all)
    }

    private fun name(s: State) = s.acc.username.ifBlank { s.acc.pkg }

    // ---------------- no root: presence ----------------
    private fun presenceReason(s: State, presence: Map<Long, Int>?, joinTimeout: Long, grace: Long): String? {
        if (presence == null || s.acc.userId == 0L) return null
        val type = presence[s.acc.userId] ?: return null
        val now = System.currentTimeMillis()
        if (type != s.lastType) { log("${name(s)}: status ${TYPES[type] ?: type}."); s.lastType = type }
        if (type == 2) {
            if (!s.inGame) { log("${name(s)}: in game."); if (s.rejoins > 0) notify("✅ ${name(s)} is back in game.") }
            s.inGame = true; s.everJoined = true; s.fails = 0
            return null
        }
        if (s.inGame) { s.inGame = false; s.outSince = now }
        if (now - s.outSince < grace || (s.launchedAt > 0 && now - s.launchedAt < joinTimeout)) return null
        return when (type) { 0 -> "offline"; 1 -> "not in a game"; 3 -> "in Studio"; else -> "not in game" }
    }

    // ---------------- root: process + log ----------------
    private fun alive(pkg: String) = Root.run("pidof $pkg").trim().isNotEmpty()

    private fun rootReason(s: State, joinTimeout: Long, grace: Long): String? {
        readLog(s)
        val now = System.currentTimeMillis()
        if (!alive(s.acc.pkg)) return if (s.launchedAt > 0) "the app closed" else "not running"
        if (!s.everJoined) return if (s.launchedAt > 0 && now - s.launchedAt > joinTimeout) "didn't join a server in ${joinTimeout / 1000} s" else null
        if (s.kick != null && !s.inGame) return "kicked / disconnected (code ${s.kick})"
        if (!s.inGame && now - s.outSince > grace) return "out of the game for ${grace / 1000} s"
        return null
    }

    private fun logDir(s: State): String {
        s.logDir?.let { return it }
        val def = "/data/data/${s.acc.pkg}/files/appData/logs"
        val dir = if (Root.run("ls $def 2>/dev/null").isNotBlank()) def else {
            val found = Root.run("find /data/data/${s.acc.pkg} -name '*_Player_*.log' 2>/dev/null | head -1").trim()
            if (found.isNotEmpty()) found.substringBeforeLast('/') else def
        }
        s.logDir = dir
        return dir
    }

    private fun readLog(s: State) {
        val dir = logDir(s)
        val newest = Root.run("ls -t $dir 2>/dev/null").split(Regex("\\s+"))
            .firstOrNull { it.endsWith(".log") && it.contains("_Player_") && !it.contains("CrashHandler") } ?: return
        val path = "$dir/$newest"
        if (path != s.logPath) {
            s.logPath = path
            val mtime = Root.run("stat -c %Y $path").trim().toLongOrNull() ?: 0
            val fresh = mtime * 1000 >= s.launchedAt - 2000
            s.logPos = if (s.adopted || fresh) 0 else Root.run("stat -c %s $path").trim().toLongOrNull() ?: 0
            s.adopted = false
        }
        val data = Root.run("tail -c +${s.logPos + 1} $path")
        s.logPos += data.toByteArray().size
        for (line in data.lineSequence()) {
            val join = JOIN.find(line)
            if (join != null) {
                if (!s.inGame) { log("${name(s)}: in game (place ${join.groupValues[1]})."); if (s.rejoins > 0) notify("✅ ${name(s)} is back in game.") }
                s.inGame = true; s.everJoined = true; s.kick = null; s.fails = 0
                continue
            }
            val kick = KICK.find(line)
            if (kick != null) s.kick = kick.groupValues[1].toIntOrNull()
            if (kick != null || DISCONNECT.containsMatchIn(line)) {
                if (s.inGame) { s.inGame = false; s.outSince = System.currentTimeMillis() }
            }
        }
    }

    // ---------------- launch ----------------
    private fun launch(s: State, reason: String, root: Boolean, common: String) {
        val link = s.acc.link.ifBlank { common }
        val target = try { Roblox.deepLink(link) } catch (e: Exception) { log("${name(s)}: ${e.message}"); s.nextLaunch = System.currentTimeMillis() + 60000; return }
        if (reason != "not running") {
            log("${name(s)}: $reason — rejoining.")
            notify("⚠️ ${name(s)}: $reason — rejoining.")
            s.rejoins++
        }
        log("${name(s)}: opening ${Roblox.hide(link)}…")
        if (root) {
            Root.run("am force-stop ${s.acc.pkg}")
            Thread.sleep(1500)
            Root.run("am start -a android.intent.action.VIEW -d '$target' -p ${s.acc.pkg}")
        } else {
            try {
                startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(target)).setPackage(s.acc.pkg).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
            } catch (e: Exception) {
                log("${name(s)}: can't open Roblox (${e.message}). Allow \"Display over other apps\" for AutoRejoin.")
            }
        }
        val now = System.currentTimeMillis()
        s.launchedAt = now
        s.inGame = false
        s.everJoined = false
        s.kick = null
        s.logPath = null
        s.outSince = now
        s.fails++
        // a launch that didn't get the account into a game: the next one waits a bit longer (15, 30 ... 90 s on top)
        s.nextLaunch = if (s.fails > 1) now + minOf(90_000L, 15_000L * (s.fails - 1)) else 0
    }

    private fun notify(text: String) {
        val hook = Store.get(this, "webhook").trim()
        if (hook.isEmpty()) return
        try { Roblox.post(hook, JSONObject().put("content", text).put("username", "AutoRejoin Mobile").toString()) } catch (e: Exception) { log("Discord: ${e.message}") }
    }

    // ---------------- notification ----------------
    private fun buildNotification(text: String): Notification {
        val nm = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        if (Build.VERSION.SDK_INT >= 26) nm.createNotificationChannel(NotificationChannel(CHANNEL, "AutoRejoin", NotificationManager.IMPORTANCE_LOW))
        val open = PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE)
        val builder = if (Build.VERSION.SDK_INT >= 26) Notification.Builder(this, CHANNEL) else @Suppress("DEPRECATION") Notification.Builder(this)
        return builder.setContentTitle("AutoRejoin").setContentText(text).setSmallIcon(android.R.drawable.ic_popup_sync)
            .setContentIntent(open).setOngoing(true).build()
    }

    private fun startForegroundCompat(text: String) {
        val n = buildNotification(text)
        if (Build.VERSION.SDK_INT >= 29) startForeground(1, n, ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC) else startForeground(1, n)
    }

    private fun updateNotification(text: String) {
        (getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager).notify(1, buildNotification(text))
    }

    companion object {
        const val ACTION_STOP = "stop"
        private const val CHANNEL = "rejoin"
        private val TYPES = mapOf(0 to "offline", 1 to "online (not in a game)", 2 to "in game", 3 to "in Studio")
        private val JOIN = Regex("! Joining game '[^']+' place (\\d+)")
        private val KICK = Regex("setting replicator disconnect reason to (\\d+)")
        private val DISCONNECT = Regex("Disconnected from server|Sending disconnect with reason|Lost connection|Connection lost|ID_CONNECTION_LOST|ID_DISCONNECTION_NOTIFICATION|leaveUGCGameInternal")

        @Volatile var isRunning = false
        @Volatile var inGame = 0
        @Volatile var total = 0
        @Volatile var rejoinsTotal = 0
        private val lines = ArrayDeque<String>()
        @Volatile var listener: ((String) -> Unit)? = null

        fun log(text: String) {
            val line = SimpleDateFormat("HH:mm:ss", Locale.US).format(Date()) + "  " + text
            synchronized(lines) { lines.addLast(line); while (lines.size > 300) lines.removeFirst() }
            listener?.invoke(line)
        }

        fun history(): String = synchronized(lines) { lines.joinToString("\n") }
    }
}
