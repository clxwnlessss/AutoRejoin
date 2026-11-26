package com.autorejoin.mobile

import android.Manifest
import android.app.Activity
import android.content.Intent
import android.content.res.ColorStateList
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.PowerManager
import android.provider.Settings
import android.text.InputType
import android.text.SpannableStringBuilder
import android.text.Spanned
import android.text.style.ForegroundColorSpan
import android.view.Gravity
import android.view.View
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.Switch
import android.widget.TextView
import android.widget.Toast
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL

class MainActivity : Activity() {

    // palette: near-black surfaces, one violet accent (as on the AutoRejoin site and bot)
    private val bg = Color.parseColor("#08080B")
    private val surface = Color.parseColor("#121217")
    private val surface2 = Color.parseColor("#0C0C10")
    private val stroke = Color.parseColor("#23232B")
    private val text1 = Color.parseColor("#F4F4F6")
    private val text2 = Color.parseColor("#8E8E9A")
    private val text3 = Color.parseColor("#5C5C68")
    private val accent = Color.parseColor("#8B5CF6")
    private val accent2 = Color.parseColor("#6366F1")
    private val green = Color.parseColor("#30A46C")
    private val amber = Color.parseColor("#F5A524")
    private val red = Color.parseColor("#E5484D")

    private lateinit var root: LinearLayout
    private lateinit var logView: TextView
    private lateinit var statusDot: View
    private lateinit var statusTitle: TextView
    private lateinit var statusSub: TextView
    private lateinit var modePill: TextView
    private lateinit var startBtn: TextView
    private lateinit var chips: LinearLayout
    private lateinit var linkBox: EditText
    private lateinit var hookBox: EditText
    private lateinit var graceBox: EditText
    private lateinit var joinBox: EditText
    private lateinit var freezeBox: EditText
    private lateinit var reportBox: EditText
    private lateinit var shotSwitch: Switch
    private lateinit var execSwitch: Switch
    private lateinit var execStatus: TextView
    private val rows = ArrayList<Triple<Account, EditText, EditText>>()
    private val switches = HashMap<String, Switch>()
    private val ui = Handler(Looper.getMainLooper())
    private val tick = object : Runnable {
        override fun run() { refreshStatus(); ui.postDelayed(this, 3000) }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        window.statusBarColor = bg
        window.navigationBarColor = bg
        val scroll = ScrollView(this).apply {
            isFillViewport = true
            background = GradientDrawable(GradientDrawable.Orientation.TOP_BOTTOM, intArrayOf(Color.parseColor("#17112A"), bg, bg))
        }
        root = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; setPadding(dp(18), dp(20), dp(18), dp(32)) }
        scroll.addView(root)
        setContentView(scroll)
        build()
        if (Build.VERSION.SDK_INT >= 33) requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 1)
    }

    override fun onResume() {
        super.onResume()
        RejoinService.listener = { l -> runOnUiThread { appendLog(l) } }
        logView.text = ""
        RejoinService.history().lines().filter { it.isNotBlank() }.forEach { appendLog(it) }
        ui.post(tick)
    }

    override fun onPause() {
        RejoinService.listener = null
        ui.removeCallbacks(tick)
        super.onPause()
    }

    // ---------------------------------------------------------------- layout
    private fun build() {
        // header: logo, name, version, mode pill
        val header = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL }
        val logo = FrameLayout(this).apply {
            background = GradientDrawable(GradientDrawable.Orientation.TL_BR, intArrayOf(accent, accent2)).apply { cornerRadius = dp(14).toFloat() }
            layoutParams = LinearLayout.LayoutParams(dp(46), dp(46))
            addView(ImageView(this@MainActivity).apply {
                setImageResource(R.drawable.ic_logo)
                layoutParams = FrameLayout.LayoutParams(dp(26), dp(26), Gravity.CENTER)
            })
        }
        header.addView(logo)
        val titles = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(12), 0, 0, 0)
            layoutParams = LinearLayout.LayoutParams(0, -2, 1f)
        }
        titles.addView(text("AutoRejoin", 22f, text1, bold = true))
        titles.addView(text("Mobile · v" + Updater.currentVersion(this), 12.5f, text2))
        header.addView(titles)
        modePill = pill("…", text2, surface)
        header.addView(modePill)
        root.addView(header)

        // hero: status + start/stop
        val hero = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            background = GradientDrawable(GradientDrawable.Orientation.TL_BR, intArrayOf(Color.parseColor("#1B1530"), surface)).apply {
                cornerRadius = dp(20).toFloat(); setStroke(dp(1), Color.parseColor("#2C2547"))
            }
            setPadding(dp(18), dp(18), dp(18), dp(18))
            layoutParams = LinearLayout.LayoutParams(-1, -2).apply { topMargin = dp(18) }
        }
        val statusRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL }
        statusDot = View(this).apply { layoutParams = LinearLayout.LayoutParams(dp(10), dp(10)).apply { rightMargin = dp(10) } }
        statusRow.addView(statusDot)
        statusTitle = text("Stopped", 20f, text1, bold = true)
        statusRow.addView(statusTitle)
        hero.addView(statusRow)
        statusSub = text("", 13f, text2).apply { setPadding(0, dp(4), 0, 0) }
        hero.addView(statusSub)
        startBtn = TextView(this).apply {
            textSize = 16f; typeface = Typeface.DEFAULT_BOLD; gravity = Gravity.CENTER
            layoutParams = LinearLayout.LayoutParams(-1, dp(54)).apply { topMargin = dp(16) }
            setOnClickListener { toggle() }
        }
        hero.addView(startBtn)
        chips = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            layoutParams = LinearLayout.LayoutParams(-1, -2).apply { topMargin = dp(14) }
        }
        hero.addView(chips)
        root.addView(hero)

        // game
        section("GAME")
        val gameCard = card()
        gameCard.addView(text("Game for all accounts", 15f, text1, bold = true))
        gameCard.addView(hint("Place ID, game link or VIP link · Bee Swarm = 1537690962"))
        linkBox = field(Store.get(this, "link", Roblox.BEE_SWARM), "1537690962")
        gameCard.addView(linkBox)
        root.addView(gameCard)

        // accounts
        section("ACCOUNTS")
        val saved = Store.accounts(this).associateBy { it.pkg }
        val apps = Roblox.installedApps(this)
        val accCard = card()
        if (apps.isEmpty()) accCard.addView(hint("No Roblox apps found — install Roblox first.").apply { setTextColor(red) })
        apps.forEachIndexed { i, pkg ->
            val acc = saved[pkg] ?: Account(pkg)
            if (i > 0) accCard.addView(View(this).apply {
                setBackgroundColor(stroke); layoutParams = LinearLayout.LayoutParams(-1, dp(1)).apply { topMargin = dp(14); bottomMargin = dp(14) }
            })
            val head = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL }
            val avatar = avatarView(acc, i)
            head.addView(avatar)
            val names = LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL; setPadding(dp(12), 0, dp(8), 0)
                layoutParams = LinearLayout.LayoutParams(0, -2, 1f)
            }
            names.addView(text(acc.username.ifBlank { if (pkg == "com.roblox.client") "Main" else "Clone ${i}" }, 15f, text1, bold = true))
            names.addView(text(pkg, 11.5f, text3))
            head.addView(names)
            val sw = switch(acc.enabled)
            switches[pkg] = sw
            head.addView(sw)
            accCard.addView(head)
            val user = field(acc.username, "Roblox username (needed without root)")
            val link = field(acc.link, "Own game / VIP link — optional")
            accCard.addView(user)
            accCard.addView(link)
            rows.add(Triple(acc, user, link))
        }
        root.addView(accCard)

        // speed
        section("SPEED")
        val speedCard = card()
        val speedRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        graceBox = field(Store.get(this, "grace", "20"), "20").apply { inputType = InputType.TYPE_CLASS_NUMBER }
        joinBox = field(Store.get(this, "joinTimeout", "60"), "60").apply { inputType = InputType.TYPE_CLASS_NUMBER }
        speedRow.addView(labeled("Rejoin after out of game, s", graceBox, rightGap = true))
        speedRow.addView(labeled("Wait for a launch, s", joinBox, rightGap = false))
        speedCard.addView(speedRow)
        speedCard.addView(hint("Teleports into matches take ~10–20 s, so keep the first one above that."))
        freezeBox = field(Store.get(this, "freeze", "120"), "120").apply { inputType = InputType.TYPE_CLASS_NUMBER }
        speedCard.addView(labeled("Game frozen after no heartbeat, s (root + executor)", freezeBox, rightGap = false).apply {
            layoutParams = LinearLayout.LayoutParams(-1, -2).apply { topMargin = dp(12) }
        })
        root.addView(speedCard)

        // executor
        section("EXECUTOR")
        val execCard = card()
        val execRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL }
        execRow.addView(text("Watcher script in autoexec", 15f, text1, bold = true).apply { layoutParams = LinearLayout.LayoutParams(0, -2, 1f) })
        execSwitch = switch(Store.get(this, "execScript", "1") == "1")
        execRow.addView(execSwitch)
        execCard.addView(execRow)
        execCard.addView(hint("Root: AutoRejoin puts a small script into the autoexec of Delta, Arceus X, Codex and others. It reports a frozen game and the Disconnected prompt the moment they happen."))
        execStatus = hint("Looking for executors…").apply { setTextColor(text3) }
        execCard.addView(execStatus)
        root.addView(execCard)

        // alerts
        section("DISCORD ALERTS")
        val hookCard = card()
        hookCard.addView(hint("Webhook URL — drop and back-in-game messages. Optional."))
        hookBox = field(Store.get(this, "webhook"), "https://discord.com/api/webhooks/…")
        hookCard.addView(hookBox)
        reportBox = field(Store.get(this, "reportMin", "0"), "0").apply { inputType = InputType.TYPE_CLASS_NUMBER }
        hookCard.addView(labeled("Status report every N minutes (0 = off)", reportBox, rightGap = false).apply {
            layoutParams = LinearLayout.LayoutParams(-1, -2).apply { topMargin = dp(12) }
        })
        val shotRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL
            layoutParams = LinearLayout.LayoutParams(-1, -2).apply { topMargin = dp(12) }
        }
        shotRow.addView(text("Screenshot with drop alerts (root)", 14f, text1).apply { layoutParams = LinearLayout.LayoutParams(0, -2, 1f) })
        shotSwitch = switch(Store.get(this, "shots", "1") == "1")
        shotRow.addView(shotSwitch)
        hookCard.addView(shotRow)
        root.addView(hookCard)

        // log
        val logHead = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL
            layoutParams = LinearLayout.LayoutParams(-1, -2).apply { topMargin = dp(22) }
        }
        logHead.addView(text("LOG", 11.5f, text3, bold = true).apply { letterSpacing = 0.12f; layoutParams = LinearLayout.LayoutParams(0, -2, 1f) })
        logHead.addView(text("Clear", 13f, accent).apply { setOnClickListener { logView.text = "" } })
        root.addView(logHead)
        logView = TextView(this).apply {
            setTextColor(Color.parseColor("#C9C9D1")); textSize = 11.5f; typeface = Typeface.MONOSPACE
            setLineSpacing(0f, 1.15f)
            background = rounded(surface2, stroke, 16); setPadding(dp(14), dp(12), dp(14), dp(12)); setTextIsSelectable(true)
            minHeight = dp(120)
            layoutParams = LinearLayout.LayoutParams(-1, -2).apply { topMargin = dp(8) }
        }
        root.addView(logView)

        // footer
        root.addView(text("Check for updates", 13f, accent).apply {
            gravity = Gravity.CENTER; setPadding(0, dp(18), 0, dp(4))
            setOnClickListener {
                toast("Checking for updates…")
                Thread {
                    val started = Updater.checkAndInstall(this@MainActivity) { RejoinService.log(it) }
                    if (!started) runOnUiThread { toast("You have the latest version (" + Updater.currentVersion(this@MainActivity) + ").") }
                }.start()
            }
        })
        root.addView(text("AutoRejoin · free · updates itself", 11.5f, text3).apply { gravity = Gravity.CENTER })
    }

    // ---------------------------------------------------------------- actions
    private fun toggle() {
        if (RejoinService.isRunning) {
            startService(Intent(this, RejoinService::class.java).setAction(RejoinService.ACTION_STOP))
            Store.put(this, "running", "0")
            RejoinService.isRunning = false
            refreshStatus()
            return
        }
        val link = linkBox.text.toString().trim().ifEmpty { Roblox.BEE_SWARM }
        try { Roblox.deepLink(link) } catch (e: Exception) { toast(e.message ?: "Bad link"); return }
        val list = ArrayList<Account>()
        for ((acc, user, own) in rows) {
            val name = user.text.toString().trim()
            val ownLink = own.text.toString().trim()
            if (ownLink.isNotEmpty()) try { Roblox.deepLink(ownLink) } catch (e: Exception) { toast("${acc.pkg}: ${e.message}"); return }
            val id = if (name.equals(acc.username, true)) acc.userId else 0L
            list.add(Account(acc.pkg, name, id, ownLink, switches[acc.pkg]?.isChecked ?: true))
        }
        if (list.none { it.enabled }) { toast("Turn on at least one Roblox app."); return }
        Store.put(this, "link", link)
        Store.put(this, "webhook", hookBox.text.toString().trim())
        Store.put(this, "grace", (graceBox.text.toString().toIntOrNull() ?: 20).coerceIn(5, 600).toString())
        Store.put(this, "joinTimeout", (joinBox.text.toString().toIntOrNull() ?: 60).coerceIn(20, 600).toString())
        Store.put(this, "freeze", (freezeBox.text.toString().toIntOrNull() ?: 120).coerceIn(30, 1800).toString())
        Store.put(this, "reportMin", (reportBox.text.toString().toIntOrNull() ?: 0).coerceIn(0, 1440).toString())
        Store.put(this, "shots", if (shotSwitch.isChecked) "1" else "0")
        Store.put(this, "execScript", if (execSwitch.isChecked) "1" else "0")
        Store.saveAccounts(this, list)
        val intent = Intent(this, RejoinService::class.java)
        if (Build.VERSION.SDK_INT >= 26) startForegroundService(intent) else startService(intent)
        RejoinService.isRunning = true
        Store.put(this, "running", "1")   // Events restarts the service after an update or a reboot
        refreshStatus()
    }

    private fun refreshStatus() {
        val running = RejoinService.isRunning
        statusDot.background = GradientDrawable().apply { shape = GradientDrawable.OVAL; setColor(if (running) green else text3) }
        statusTitle.text = if (running) "Running" else "Stopped"
        statusSub.text = when {
            running && RejoinService.total > 0 -> "${RejoinService.inGame} of ${RejoinService.total} in game · ${RejoinService.rejoinsTotal} rejoins"
            running -> "Starting…"
            else -> "Press Start — AutoRejoin keeps your accounts in game."
        }
        startBtn.text = if (running) "Stop" else "Start"
        startBtn.setTextColor(if (running) red else Color.BLACK)
        startBtn.background = if (running) rounded(Color.parseColor("#1A1214"), Color.parseColor("#4A2326"), 16) else rounded(Color.WHITE, null, 16)
        Thread {
            val r = Root.available()
            val overlay = Build.VERSION.SDK_INT < 23 || Settings.canDrawOverlays(this)
            val battery = (getSystemService(POWER_SERVICE) as PowerManager).isIgnoringBatteryOptimizations(packageName)
            val installs = Build.VERSION.SDK_INT < 26 || packageManager.canRequestPackageInstalls()
            val execs = if (r) Executors.autoexecDirs() else emptyList()
            runOnUiThread {
                execStatus.text = when {
                    !r -> "Needs root."
                    execs.isEmpty() -> "No executor autoexec folder found yet — open your executor once."
                    else -> "Found: " + execs.joinToString(" · ") { Executors.executorName(it) }
                }
                execStatus.setTextColor(if (r && execs.isNotEmpty()) green else text3)
                modePill.text = if (r) "ROOT" else "NO ROOT"
                modePill.setTextColor(if (r) green else amber)
                modePill.background = rounded(if (r) Color.parseColor("#0F2119") else Color.parseColor("#241C0C"), null, 99)
                chips.removeAllViews()
                chips.addView(chip("Root", r, null))
                chips.addView(chip("Overlay", overlay || r, Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION, Uri.parse("package:$packageName"))))
                chips.addView(chip("Battery", battery, Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS, Uri.parse("package:$packageName"))))
                chips.addView(chip("Updates", installs || r, if (Build.VERSION.SDK_INT >= 26) Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:$packageName")) else null))
            }
        }.start()
    }

    private fun appendLog(line: String) {
        val color = when {
            Regex("in game", RegexOption.IGNORE_CASE).containsMatchIn(line) && !line.contains("not in", true) -> green
            Regex("rejoin|opening", RegexOption.IGNORE_CASE).containsMatchIn(line) -> amber
            Regex("error|failed|can't|no root|HTTP", RegexOption.IGNORE_CASE).containsMatchIn(line) -> red
            Regex("update|root mode", RegexOption.IGNORE_CASE).containsMatchIn(line) -> Color.parseColor("#B79CFF")
            else -> Color.parseColor("#C9C9D1")
        }
        val sb = SpannableStringBuilder()
        if (logView.text.isNotEmpty()) sb.append("\n")
        val start = sb.length
        val stamp = line.take(8)
        sb.append(stamp)
        sb.setSpan(ForegroundColorSpan(text3), start, sb.length, Spanned.SPAN_EXCLUSIVE_EXCLUSIVE)
        val rest = sb.length
        sb.append(line.drop(8))
        sb.setSpan(ForegroundColorSpan(color), rest, sb.length, Spanned.SPAN_EXCLUSIVE_EXCLUSIVE)
        logView.append(sb)
    }

    // ---------------------------------------------------------------- avatars
    private fun avatarView(acc: Account, index: Int): View {
        val size = dp(42)
        val frame = FrameLayout(this).apply { layoutParams = LinearLayout.LayoutParams(size, size) }
        val palette = intArrayOf(accent, Color.parseColor("#0EA5E9"), Color.parseColor("#F59E0B"), Color.parseColor("#EC4899"), Color.parseColor("#10B981"))
        val letter = text((acc.username.ifBlank { if (acc.pkg == "com.roblox.client") "M" else "${index}" }).take(1).uppercase(), 16f, Color.WHITE, bold = true).apply {
            gravity = Gravity.CENTER
            background = GradientDrawable().apply { shape = GradientDrawable.OVAL; setColor(palette[index % palette.size]) }
            layoutParams = FrameLayout.LayoutParams(size, size)
        }
        frame.addView(letter)
        if (acc.userId > 0) {
            val img = ImageView(this).apply {
                layoutParams = FrameLayout.LayoutParams(size, size)
                scaleType = ImageView.ScaleType.CENTER_CROP
                clipToOutline = true
                background = GradientDrawable().apply { shape = GradientDrawable.OVAL; setColor(surface2) }
                visibility = View.GONE
            }
            frame.addView(img)
            Thread {
                val bmp = headshot(acc.userId)
                if (bmp != null) runOnUiThread { img.setImageBitmap(bmp); img.visibility = View.VISIBLE }
            }.start()
        }
        return frame
    }

    private fun headshot(userId: Long): Bitmap? = try {
        val api = URL("https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds=$userId&size=150x150&format=Png&isCircular=true")
        val c = api.openConnection() as HttpURLConnection
        c.connectTimeout = 10000; c.readTimeout = 10000; c.setRequestProperty("User-Agent", "AutoRejoin-Mobile")
        val url = JSONObject(c.inputStream.bufferedReader().readText()).getJSONArray("data").getJSONObject(0).getString("imageUrl")
        URL(url).openStream().use { BitmapFactory.decodeStream(it) }
    } catch (e: Exception) { null }

    // ---------------------------------------------------------------- small UI helpers
    private fun dp(v: Int) = (v * resources.displayMetrics.density).toInt()

    private fun rounded(color: Int, strokeColor: Int? = null, radius: Int = 18) = GradientDrawable().apply {
        cornerRadius = dp(radius).toFloat(); setColor(color); if (strokeColor != null) setStroke(dp(1), strokeColor)
    }

    private fun card() = LinearLayout(this).apply {
        orientation = LinearLayout.VERTICAL
        background = rounded(surface, stroke)
        setPadding(dp(16), dp(16), dp(16), dp(16))
        layoutParams = LinearLayout.LayoutParams(-1, -2).apply { topMargin = dp(8) }
    }

    private fun section(title: String) {
        root.addView(text(title, 11.5f, text3, bold = true).apply {
            letterSpacing = 0.12f
            layoutParams = LinearLayout.LayoutParams(-1, -2).apply { topMargin = dp(22); leftMargin = dp(4) }
        })
    }

    private fun text(t: String, size: Float, color: Int, bold: Boolean = false) = TextView(this).apply {
        text = t; textSize = size; setTextColor(color); if (bold) typeface = Typeface.create("sans-serif-medium", Typeface.BOLD)
    }

    private fun hint(t: String) = text(t, 12.5f, text2).apply { setPadding(0, dp(4), 0, 0); setLineSpacing(0f, 1.1f) }

    private fun pill(t: String, color: Int, back: Int) = TextView(this).apply {
        text = t; textSize = 11f; setTextColor(color); typeface = Typeface.DEFAULT_BOLD; letterSpacing = 0.08f
        background = rounded(back, null, 99); setPadding(dp(10), dp(5), dp(10), dp(5))
    }

    private fun chip(label: String, ok: Boolean, fix: Intent?) = TextView(this).apply {
        text = (if (ok) "✓ " else "! ") + label
        textSize = 12f; gravity = Gravity.CENTER; typeface = Typeface.DEFAULT_BOLD
        setTextColor(if (ok) green else amber)
        background = rounded(if (ok) Color.parseColor("#0F2119") else Color.parseColor("#241C0C"), null, 99)
        setPadding(dp(4), dp(7), dp(4), dp(7))
        layoutParams = LinearLayout.LayoutParams(0, -2, 1f).apply { rightMargin = dp(6) }
        if (!ok && fix != null) setOnClickListener { try { startActivity(fix) } catch (e: Exception) { toast(e.message ?: "") } }
    }

    private fun field(value: String, placeholder: String) = EditText(this).apply {
        setText(value); hint = placeholder; setHintTextColor(text3); setTextColor(text1); textSize = 14f
        inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_URI; isSingleLine = true
        background = rounded(surface2, stroke, 12); setPadding(dp(14), dp(12), dp(14), dp(12))
        layoutParams = LinearLayout.LayoutParams(-1, -2).apply { topMargin = dp(10) }
    }

    private fun labeled(label: String, box: EditText, rightGap: Boolean) = LinearLayout(this).apply {
        orientation = LinearLayout.VERTICAL
        layoutParams = LinearLayout.LayoutParams(0, -2, 1f).apply { if (rightGap) rightMargin = dp(10) }
        addView(text(label, 12f, text2))
        addView(box)
    }

    private fun switch(on: Boolean) = Switch(this).apply {
        isChecked = on
        thumbTintList = ColorStateList(arrayOf(intArrayOf(android.R.attr.state_checked), intArrayOf()), intArrayOf(Color.WHITE, Color.parseColor("#9A9AA5")))
        trackTintList = ColorStateList(arrayOf(intArrayOf(android.R.attr.state_checked), intArrayOf()), intArrayOf(accent, Color.parseColor("#33333D")))
    }

    private fun toast(t: String) = Toast.makeText(this, t, Toast.LENGTH_LONG).show()
}
