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
import java.net.HttpURLConnection
import java.net.URL
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

/**
 * Keeps every Roblox app in game.
 *  - With root: the Roblox log file and, in real time, logcat (joins, disconnects, kick codes, the account's user ID);
 *    the heartbeat of our script in the executor's autoexec (frozen game, "Disconnected" prompt); the process itself.
 *    Closed / kicked / frozen / out of a server -> force-stop, open Roblox and the game again.
 *  - Without root: Roblox's public presence API by username (no cookie) -> opens the game again by link.
 * After 10 failed launches in a row an account rests for 10 minutes. Optional Discord alerts with a screenshot and
 * periodic status reports.
 */
class RejoinService : Service() {

    private class State(val acc: Account) {
        var launchedAt = 0L
        var inGame = false
        var everJoined = false
        var outSince = System.currentTimeMillis()
        var joinedAt = 0L
        var kick: Int? = null
        var logPath: String? = null
        var logPos = 0L
        var logDir: String? = null
        var rejoins = 0
        var fails = 0
        var nextLaunch = 0L
        var adopted = false
        var lastType = -1
        var pid = 0
        var place: String? = null
        @Volatile var force: String? = null   // rejoin asked from the app (button, schedule)
    }

    private var worker: Thread? = null
    private var logcat: Thread? = null
    @Volatile private var logcatProc: Process? = null
    @Volatile private var running = false
    private var wake: PowerManager.WakeLock? = null
    private var states: List<State> = emptyList()
    private val startedAt = System.currentTimeMillis()

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == ACTION_STOP) {
            stopSelf()
            return START_NOT_STICKY
        }
        if (intent?.action == ACTION_REJOIN) {
            // rejoin one account (pkg) or all of them, from the app's buttons
            val pkg = intent.getStringExtra("pkg")
            val targets = states.filter { pkg == null || it.acc.pkg == pkg }
            targets.forEach { it.force = "rejoin button"; it.nextLaunch = 0 }
            log(if (pkg == null) "Rejoin all (button): ${targets.size} account(s)." else "${targets.firstOrNull()?.let { name(it) } ?: pkg}: rejoin (button).")
            if (running) return START_STICKY
        }
        startForegroundCompat("Starting…")
        if (!running) {
            running = true
            paused = false
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
        try { logcatProc?.destroy() } catch (_: Exception) {}
        logcat?.interrupt()
        try { wake?.release() } catch (_: Exception) {}
        log("Stopped.")
        super.onDestroy()
    }

    // ================================================================ main loop
    private fun loop() {
        val accounts = Store.accounts(this).filter { it.enabled }
        if (accounts.isEmpty()) { log("No accounts — add them on the main screen."); stopSelf(); return }
        Root.recheck()
        val root = Root.available()
        val common = Store.get(this, "link", Roblox.BEE_SWARM)
        val grace = (Store.get(this, "grace", "20").toLongOrNull() ?: 20).coerceIn(5, 600) * 1000
        val joinTimeout = (Store.get(this, "joinTimeout", "60").toLongOrNull() ?: 60).coerceIn(20, 600) * 1000
        val freeze = (Store.get(this, "freeze", "120").toLongOrNull() ?: 120).coerceIn(30, 1800) * 1000
        val reportEvery = (Store.get(this, "reportMin", "0").toLongOrNull() ?: 0).coerceIn(0, 1440) * 60_000
        states = accounts.map { State(it) }
        log(if (root) "Root mode: watching ${states.size} app(s) — Roblox log + logcat in real time." else "No root: watching ${states.size} account(s) through Roblox presence.")
        startCloud(root, joinTimeout)

        var workspaces = emptyList<String>()
        if (root) {
            for (s in states) detectAccount(s)
            if (Store.get(this, "execScript", "1") == "1") {
                val dirs = Executors.install(this)
                workspaces = dirs.map { Executors.workspaceOf(it) }.distinct()
                if (dirs.isEmpty()) log("No executor autoexec folder found — frozen-game check is off (open your executor once).")
                else log("Executor script in: " + dirs.joinToString { Executors.executorName(it) } + " — frozen game and Disconnected prompt are watched.")
            }
            startLogcat()
        }
        for (s in states) {
            if (s.acc.userId == 0L && s.acc.username.isNotBlank()) {
                try { s.acc.userId = Roblox.userId(s.acc.username.trim()); saveAccount(s) } catch (e: Exception) { log("${name(s)}: ${e.message}") }
            }
            if (!root && s.acc.userId == 0L) log("${name(s)}: no Roblox username — can't watch it without root.")
            if (root && alive(s.acc.pkg)) { s.adopted = true; s.launchedAt = System.currentTimeMillis(); log("${name(s)}: already open — watching.") }
        }

        var lastLaunch = 0L
        var nextUpdateCheck = System.currentTimeMillis() + 60_000
        var nextReport = if (reportEvery > 0) System.currentTimeMillis() + reportEvery else Long.MAX_VALUE
        var nextExecScan = System.currentTimeMillis() + 5 * 60_000
        while (running) {
            val now0 = System.currentTimeMillis()
            if (now0 >= nextUpdateCheck) {
                nextUpdateCheck = now0 + 30 * 60_000L
                if (Store.get(this, "autoUpdate", "1") == "1") Thread { Updater.checkAndInstall(this) { log(it) } }.start()
            }
            if (now0 >= nextReport) { nextReport = now0 + reportEvery; Thread { report() }.start() }
            checkSchedule()
            if (root && now0 >= nextExecScan && Store.get(this, "execScript", "1") == "1") {
                // an executor installed later, or its folder appeared after the first launch
                nextExecScan = now0 + 5 * 60_000
                workspaces = Executors.install(this).map { Executors.workspaceOf(it) }.distinct()
            }
            try {
                val presence = if (root) emptyMap() else try {
                    Roblox.presence(states.map { it.acc.userId }.filter { it > 0 })
                } catch (e: Exception) { log("Presence: ${e.message}"); null }
                for (s in states) {
                    if (!running) break
                    val auto = synchronized(s) {
                        if (root) rootReason(s, joinTimeout, grace, freeze, workspaces) else presenceReason(s, presence, joinTimeout, grace)
                    }
                    val forced = s.force
                    val reason = forced ?: auto ?: continue
                    if (paused && forced == null) continue   // stopped from a bot: watch, don't rejoin
                    val now = System.currentTimeMillis()
                    if ((forced == null && now < s.nextLaunch) || now - lastLaunch < 4000) continue
                    s.force = null
                    launch(s, reason, root, common)
                    lastLaunch = System.currentTimeMillis()
                }
                val inGame = states.count { it.inGame }
                Companion.inGame = inGame
                total = states.size
                rejoinsTotal = states.sumOf { it.rejoins }
                updateNotification("$inGame of ${states.size} in game")
                snapshot = states.map { st ->
                    val now = System.currentTimeMillis()
                    val status = when {
                        st.inGame -> "In game"
                        st.nextLaunch > now + 60_000 -> "Paused ${(st.nextLaunch - now) / 60_000 + 1} min"
                        st.launchedAt > 0 && now - st.launchedAt < joinTimeout -> "Launching…"
                        st.everJoined || st.launchedAt > 0 -> "Out of game"
                        else -> "Waiting"
                    }
                    AccStatus(st.acc.pkg, name(st), st.acc.userId, st.inGame, status, st.rejoins, st.place)
                }
            } catch (e: InterruptedException) {
                break
            } catch (e: Exception) {
                log("Error: ${e.message}")
            }
            try { Thread.sleep(if (root) 2000 else 8000) } catch (e: InterruptedException) { break }
        }
    }

    private fun name(s: State) = s.acc.username.ifBlank { s.acc.pkg }

    private fun saveAccount(s: State) {
        val all = Store.accounts(this)
        all.firstOrNull { it.pkg == s.acc.pkg }?.let { it.userId = s.acc.userId; it.username = s.acc.username }
        Store.saveAccounts(this, all)
    }

    // ================================================================ schedule
    private var scheduleDoneDay = -1

    /** Daily rejoin of every account at the time set in the app (HH:mm, phone time). */
    private fun checkSchedule() {
        if (Store.get(this, "scheduleOn", "0") != "1") return
        val parts = Store.get(this, "scheduleAt", "06:00").split(":")
        val h = parts.getOrNull(0)?.toIntOrNull() ?: return
        val m = parts.getOrNull(1)?.toIntOrNull() ?: 0
        val cal = java.util.Calendar.getInstance()
        val day = cal.get(java.util.Calendar.DAY_OF_YEAR)
        if (day == scheduleDoneDay) return
        if (cal.get(java.util.Calendar.HOUR_OF_DAY) == h && cal.get(java.util.Calendar.MINUTE) == m) {
            scheduleDoneDay = day
            states.forEach { it.force = "scheduled rejoin"; it.nextLaunch = 0 }
            log("Scheduled rejoin ($h:${"%02d".format(m)}): all accounts.")
        }
    }

    // ================================================================ no root: presence
    private fun presenceReason(s: State, presence: Map<Long, Int>?, joinTimeout: Long, grace: Long): String? {
        if (presence == null || s.acc.userId == 0L) return null
        val type = presence[s.acc.userId] ?: return null
        val now = System.currentTimeMillis()
        if (type != s.lastType) { log("${name(s)}: status ${TYPES[type] ?: type}."); s.lastType = type }
        if (type == 2) {
            if (!s.inGame) joined(s, null)
            return null
        }
        if (s.inGame) { s.inGame = false; s.outSince = now }
        if (now - s.outSince < grace || (s.launchedAt > 0 && now - s.launchedAt < joinTimeout)) return null
        return when (type) { 0 -> "offline"; 1 -> "not in a game"; 3 -> "in Studio"; else -> "not in game" }
    }

    // ================================================================ root: process, log, logcat, heartbeat
    private fun alive(pkg: String) = Root.run("pidof $pkg").trim().isNotEmpty()

    private fun rootReason(s: State, joinTimeout: Long, grace: Long, freeze: Long, workspaces: List<String>): String? {
        readLog(s)
        val now = System.currentTimeMillis()
        val pid = Root.run("pidof ${s.acc.pkg}").trim().split(" ").firstOrNull()?.toIntOrNull() ?: 0
        s.pid = pid
        if (pid == 0) return if (s.launchedAt > 0) "the app closed" else "not running"
        if (!s.everJoined) return if (s.launchedAt > 0 && now - s.launchedAt > joinTimeout) "didn't join a server in ${joinTimeout / 1000} s" else null
        if (s.kick != null && !s.inGame) return "kicked / disconnected (code ${s.kick})"
        if (s.acc.userId > 0 && workspaces.isNotEmpty()) {
            val dc = Executors.disconnect(workspaces, s.acc.userId)
            if (dc != null && dc.first > s.joinedAt && dc.first > s.launchedAt) return "Roblox showed Disconnected (code ${dc.second})"
            if (s.inGame) {
                val hb = Executors.heartbeat(workspaces, s.acc.userId)
                val since = maxOf(s.joinedAt, s.launchedAt)
                if (hb > 0 && hb >= since - 60_000 && now - hb > freeze && now - since > freeze) return "the game froze (no heartbeat for ${(now - hb) / 1000} s)"
            }
        }
        if (!s.inGame && now - s.outSince > grace) return "out of the game for ${grace / 1000} s"
        return null
    }

    private fun joined(s: State, place: String?) {
        val back = s.rejoins > 0
        log("${name(s)}: in game" + (if (place != null) " (place $place)." else "."))
        s.inGame = true; s.everJoined = true; s.kick = null; s.fails = 0
        s.joinedAt = System.currentTimeMillis()
        if (place != null) s.place = place
        if (back) {
            notify("✅ **${name(s)}** is back in game.", shot = false)
            cloudEvent("back", "🟢 **${name(s)}** is back in game.", s)
        }
    }

    /** One line of the Roblox log (from the file or from logcat). */
    private fun handleLine(s: State, line: String) {
        val join = JOIN.find(line)
        if (join != null) {
            if (!s.inGame) joined(s, join.groupValues[1])
            return
        }
        val uid = USERID.find(line)
        if (uid != null && s.acc.userId == 0L) {
            s.acc.userId = uid.groupValues[1].toLongOrNull() ?: 0
            Thread { lookupName(s) }.start()
        }
        val kick = KICK.find(line)
        if (kick != null) s.kick = kick.groupValues[1].toIntOrNull()
        if (kick != null || DISCONNECT.containsMatchIn(line)) {
            if (s.inGame) { s.inGame = false; s.outSince = System.currentTimeMillis() }
        }
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
        for (line in data.lineSequence()) handleLine(s, line)
    }

    /** Real-time logcat: a disconnect is seen the moment Roblox writes it, not on the next check. */
    private fun startLogcat() {
        logcat = Thread({
            while (running) {
                try {
                    val p = ProcessBuilder("su", "-c", "logcat -v threadtime -T 1").redirectErrorStream(true).start()
                    logcatProc = p
                    p.inputStream.bufferedReader().useLines { seq ->
                        for (line in seq) {
                            if (!running) break
                            if (!QUICK.containsMatchIn(line)) continue
                            val pid = PID.find(line)?.groupValues?.get(1)?.toIntOrNull() ?: continue
                            val s = states.firstOrNull { it.pid == pid } ?: continue
                            synchronized(s) { handleLine(s, line) }
                        }
                    }
                } catch (e: Exception) {
                    if (running) log("logcat: ${e.message}")
                }
                try { Thread.sleep(3000) } catch (e: InterruptedException) { break }
            }
        }, "logcat").apply { isDaemon = true; start() }
    }

    // ================================================================ account detection (root)
    /** User ID from the Roblox app's own data (shared_prefs), then the name from Roblox. */
    private fun detectAccount(s: State) {
        if (s.acc.userId > 0 && s.acc.username.isNotBlank()) return
        if (s.acc.userId == 0L) {
            val prefs = Root.run("cat /data/data/${s.acc.pkg}/shared_prefs/*.xml 2>/dev/null")
            val id = PREF_USERID.findAll(prefs).map { it.groupValues[1].ifEmpty { it.groupValues[2] } }
                .mapNotNull { it.toLongOrNull() }.firstOrNull { it in 1_000..99_999_999_999 }
            if (id != null) s.acc.userId = id
        }
        if (s.acc.userId > 0 && s.acc.username.isBlank()) lookupName(s)
    }

    private fun lookupName(s: State) {
        try {
            val c = URL("https://users.roblox.com/v1/users/${s.acc.userId}").openConnection() as HttpURLConnection
            c.connectTimeout = 10000; c.readTimeout = 10000; c.setRequestProperty("User-Agent", "AutoRejoin-Mobile")
            val name = JSONObject(c.inputStream.bufferedReader().readText()).optString("name")
            if (name.isNotBlank()) {
                s.acc.username = name
                log("${s.acc.pkg}: account $name (id ${s.acc.userId}).")
            }
        } catch (e: Exception) { /* name stays empty, the ID still works */ }
        saveAccount(s)
    }

    // ================================================================ launch
    private fun launch(s: State, reason: String, root: Boolean, common: String) {
        val link = s.acc.link.ifBlank { common }
        val target = try { Roblox.deepLink(link) } catch (e: Exception) { log("${name(s)}: ${e.message}"); s.nextLaunch = System.currentTimeMillis() + 60000; return }
        if (reason != "not running") {
            log("${name(s)}: $reason — rejoining.")
            notify("⚠️ **${name(s)}**: $reason — rejoining.", shot = root && Store.get(this, "shots", "1") == "1")
            cloudEvent("drop", "🔴 **${name(s)}** dropped: $reason", s)
            s.rejoins++
        }
        log("${name(s)}: opening ${Roblox.hide(link)}…")
        if (root) {
            Root.run("am force-stop ${s.acc.pkg}")
            Thread.sleep(1200)
            // start Roblox through its launcher first: a cold app then takes the game link reliably (clones too)
            Root.run("am start --user 0 -n ${s.acc.pkg}/com.roblox.client.startup.LauncherAliasMain -a android.intent.action.MAIN -c android.intent.category.LAUNCHER")
            Thread.sleep(3500)
            Root.run("am start --user 0 -a android.intent.action.VIEW -d '$target' -p ${s.acc.pkg}")
        } else {
            try {
                if (!s.inGame && s.lastType == 0) {
                    packageManager.getLaunchIntentForPackage(s.acc.pkg)?.let { startActivity(it.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)); Thread.sleep(3500) }
                }
                startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(target)).setPackage(s.acc.pkg).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
            } catch (e: Exception) {
                log("${name(s)}: can't open Roblox (${e.message}). Allow \"Display over other apps\" for AutoRejoin.")
            }
        }
        val now = System.currentTimeMillis()
        synchronized(s) {
            s.launchedAt = now
            s.inGame = false
            s.everJoined = false
            s.kick = null
            s.logPath = null
            s.outSince = now
            s.fails++
            when {
                s.fails >= 10 -> {
                    // something is really wrong (Roblox down, banned, no internet): rest instead of hammering Roblox
                    s.nextLaunch = now + 10 * 60_000L
                    s.fails = 0
                    log("${name(s)}: 10 launches in a row didn't get into a game — pausing this account for 10 minutes.")
                    notify("⏸️ **${name(s)}**: 10 failed rejoins in a row — pausing for 10 minutes.", shot = false)
                    cloudEvent("stuck", "🟠 **${name(s)}**: 10 failed rejoins in a row — pausing for 10 minutes.", s)
                }
                s.fails > 1 -> s.nextLaunch = now + minOf(90_000L, 15_000L * (s.fails - 1))
                else -> s.nextLaunch = 0
            }
        }
    }

    // ================================================================ bots (Telegram / Discord) through the AutoRejoin server
    private fun cloudEvent(kind: String, text: String, s: State) {
        if (!Cloud.signedIn(this)) return
        Thread { Cloud.event(this, kind, text, name(s), Cloud.accountId(this, s.acc.pkg)) }.start()
    }

    private fun uptime(ms: Long): String {
        val m = ms / 60_000
        return if (m >= 60) "${m / 60}h ${m % 60}m" else "${m}m"
    }

    /** Every 10 s: this phone's accounts to the server, the bots' commands back. Needs sign-in and a linked bot. */
    private fun startCloud(root: Boolean, joinTimeout: Long) {
        Thread({
            var failing = false
            var linkedNoted = false
            while (running) {
                try {
                    if (Cloud.signedIn(this)) {
                        val now = System.currentTimeMillis()
                        val snap = snapshot.associateBy { it.pkg }
                        val accs = states.mapIndexed { i, s ->
                            val launching = s.launchedAt > 0 && now - s.launchedAt < joinTimeout
                            val state = when { s.inGame -> "ingame"; paused -> "stopped"; launching -> "launching"; else -> "out" }
                            val text = when { s.inGame -> "in game"; paused -> "stopped from a bot"; else -> (snap[s.acc.pkg]?.status ?: "waiting").lowercase() }
                            Cloud.Acc(Cloud.accountId(this, s.acc.pkg), name(s).take(40), i == 0, state, text, if (s.inGame) uptime(now - s.joinedAt) else "")
                        }
                        val cmds = Cloud.sync(this, !paused, accs)
                        if (failing) log("Bots: connected again.")
                        failing = false
                        if (cmds != null && !linkedNoted) { linkedNoted = true; log("Bots: this phone is online in the Telegram / Discord bots.") }
                        cmds?.forEach { handleCommand(it, root) }
                    }
                } catch (e: InterruptedException) {
                    break
                } catch (e: Exception) {
                    if (!failing) log("Bots: can't reach the server (${e.message}) — retrying.")
                    failing = true
                }
                try { Thread.sleep(10_000) } catch (e: InterruptedException) { break }
            }
        }, "cloud").apply { isDaemon = true; start() }
    }

    private fun handleCommand(c: Cloud.Command, root: Boolean) {
        when (c.cmd) {
            "start" -> if (paused) {
                paused = false
                states.forEach { it.nextLaunch = 0 }
                log("Bot: start — rejoining again.")
            }
            "stop" -> if (!paused) {
                paused = true
                log("Bot: stop — not rejoining until Start (in a bot or here).")
            }
            "rejoin-all" -> { states.forEach { it.force = "rejoin from a bot"; it.nextLaunch = 0 }; log("Bot: rejoin all.") }
            "rejoin" -> states.firstOrNull { Cloud.accountId(this, it.acc.pkg) == c.arg }?.let {
                it.force = "rejoin from a bot"; it.nextLaunch = 0; log("${name(it)}: rejoin (bot).")
            }
            "screen" -> {
                val reqId = c.arg.substringBefore(':')
                Thread {
                    if (!root) Cloud.sendScreen(this, reqId, null, "screenshots on a phone need root")
                    else {
                        val png = Discord.screenshot()
                        Cloud.sendScreen(this, reqId, png, if (png == null) "could not capture the screen" else null)
                    }
                }.start()
            }
        }
    }

    // ================================================================ Discord
    private fun notify(text: String, shot: Boolean) {
        val hook = Store.get(this, "webhook").trim()
        if (hook.isEmpty()) return
        Thread {
            try { Discord.send(hook, text, if (shot) Discord.screenshot() else null) } catch (e: Exception) { log("Discord: ${e.message}") }
        }.start()
    }

    private fun report() {
        val hook = Store.get(this, "webhook").trim()
        if (hook.isEmpty() || states.isEmpty()) return
        val up = (System.currentTimeMillis() - startedAt) / 60_000
        val lines = states.joinToString("\n") { s -> (if (s.inGame) "🟢 " else "🔴 ") + "**${name(s)}**" + (if (s.inGame) " — in game" else " — out of game") + (if (s.rejoins > 0) " · ${s.rejoins} rejoins" else "") }
        val head = "📊 **AutoRejoin Mobile** — ${states.count { it.inGame }} of ${states.size} in game · up ${up / 60}h ${up % 60}m"
        try { Discord.send(hook, "$head\n$lines") } catch (e: Exception) { log("Discord report: ${e.message}") }
    }

    // ================================================================ notification
    private fun buildNotification(text: String): Notification {
        val nm = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        if (Build.VERSION.SDK_INT >= 26) nm.createNotificationChannel(NotificationChannel(CHANNEL, "AutoRejoin", NotificationManager.IMPORTANCE_LOW))
        val open = PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE)
        val builder = if (Build.VERSION.SDK_INT >= 26) Notification.Builder(this, CHANNEL) else @Suppress("DEPRECATION") Notification.Builder(this)
        return builder.setContentTitle("AutoRejoin").setContentText(text).setSmallIcon(R.drawable.ic_logo)
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
        const val ACTION_REJOIN = "rejoin"
        private const val CHANNEL = "rejoin"
        private val TYPES = mapOf(0 to "offline", 1 to "online (not in a game)", 2 to "in game", 3 to "in Studio")
        private val JOIN = Regex("! Joining game '[^']+' place (\\d+)")
        private val KICK = Regex("setting replicator disconnect reason to (\\d+)")
        private val USERID = Regex("game_join_loadtime:.*\\buserid:(\\d+)")
        private val DISCONNECT = Regex("Disconnected from server|Sending disconnect with reason|Lost connection|Connection lost|ID_CONNECTION_LOST|ID_DISCONNECTION_NOTIFICATION|leaveUGCGameInternal|Client:Disconnect")
        private val QUICK = Regex("Joining game|isconnect|Lost connection|Connection lost|ID_CONNECTION|ID_DISCONNECTION|leaveUGCGameInternal|replicator disconnect reason|userid:")
        private val PID = Regex("^\\S+\\s+\\S+\\s+(\\d+)\\s+\\d+\\s+[VDIWEF]\\s")
        private val PREF_USERID = Regex("name=\"[^\"]*[Uu]ser_?[Ii][Dd][^\"]*\"(?:\\s+value=\"(\\d{3,})\"|\\s*>(\\d{3,})<)")

        @Volatile var isRunning = false
        @Volatile var paused = false      // stopped from a Telegram / Discord bot: the service stays up and keeps reporting
        @Volatile var inGame = 0
        @Volatile var total = 0
        @Volatile var rejoinsTotal = 0
        @Volatile var snapshot: List<AccStatus> = emptyList()
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

/** What the app shows on an account card. */
data class AccStatus(val pkg: String, val name: String, val userId: Long, val inGame: Boolean, val status: String, val rejoins: Int, val place: String?)
