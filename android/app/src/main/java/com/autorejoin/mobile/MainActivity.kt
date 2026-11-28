package com.autorejoin.mobile

import android.Manifest
import android.app.Activity
import android.app.AlertDialog
import android.content.ClipData
import android.content.ClipboardManager
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
import android.widget.HorizontalScrollView
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.Switch
import android.widget.TextView
import android.widget.Toast
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL

/**
 * Same structure as AutoRejoin on PC: Accounts (cards with status, rejoin button, switch), Settings (sections),
 * Log and Help, a Discord button and Rejoin all next to Start / Stop.
 */
class MainActivity : Activity() {

    // palette: near-black surfaces, one violet accent (as on the AutoRejoin site and the PC app)
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
    private val discord = Color.parseColor("#5865F2")

    private val DISCORD_URL = "https://discord.gg/bZFB8dwBdw"
    private val SITE_URL = "https://clxwnlessss.github.io/AutoRejoin/"
    private val GAMES = listOf(
        "Bee Swarm" to "1537690962", "Blox Fruits" to "2753915549", "Anime Adventures" to "8304191830",
        "Anime Vanguards" to "16146832113", "Pet Sim 99" to "8737899170", "Grow a Garden" to "126884695634066",
        "Steal a Brainrot" to "109983668079237", "99 Nights" to "79546208627805", "Fisch" to "16732694052",
        "King Legacy" to "4520749081", "Rivals" to "17625359962", "Sol's RNG" to "15532962292",
    )

    private lateinit var pages: List<ScrollView>
    private val navItems = ArrayList<TextView>()
    private lateinit var logView: TextView
    private lateinit var statusDot: View
    private lateinit var statusTitle: TextView
    private lateinit var statusSub: TextView
    private lateinit var modePill: TextView
    private lateinit var startBtn: TextView
    private lateinit var rejoinAllBtn: TextView
    private lateinit var chips: LinearLayout
    private lateinit var linkBox: EditText
    private lateinit var hookBox: EditText
    private lateinit var graceBox: EditText
    private lateinit var joinBox: EditText
    private lateinit var freezeBox: EditText
    private lateinit var reportBox: EditText
    private lateinit var scheduleBox: EditText
    private lateinit var scheduleSwitch: Switch
    private lateinit var shotSwitch: Switch
    private lateinit var execSwitch: Switch
    private lateinit var execStatus: TextView
    private lateinit var farmSwitch: Switch
    private lateinit var farmBox: EditText
    private lateinit var updateSwitch: Switch

    private class Row(val acc: Account, val user: EditText, val link: EditText, val sw: Switch,
                      val status: TextView, val dot: View, val name: TextView)
    private val rows = ArrayList<Row>()
    private lateinit var accountsBox: LinearLayout
    private lateinit var cloudBox: LinearLayout

    private val ui = Handler(Looper.getMainLooper())
    private val tick = object : Runnable {
        override fun run() { refreshStatus(); ui.postDelayed(this, 3000) }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        window.statusBarColor = bg
        window.navigationBarColor = Color.parseColor("#0E0E13")
        val screen = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            background = GradientDrawable(GradientDrawable.Orientation.TOP_BOTTOM, intArrayOf(Color.parseColor("#17112A"), bg, bg))
        }
        screen.addView(header())
        val restored = Backup.restoreLocalIfFresh(this)   // fresh install: accounts and settings from the phone backup
        val content = FrameLayout(this).apply { layoutParams = LinearLayout.LayoutParams(-1, 0, 1f) }
        pages = listOf(page { accountsPage(it) }, page { settingsPage(it) }, page { logPage(it) }, page { helpPage(it) })
        pages.forEach { content.addView(it) }
        screen.addView(content)
        screen.addView(navBar())
        setContentView(screen)
        showTab(0)
        if (Build.VERSION.SDK_INT >= 33) requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 1)
        else if (Build.VERSION.SDK_INT in 23..29) requestPermissions(arrayOf(Manifest.permission.WRITE_EXTERNAL_STORAGE, Manifest.permission.READ_EXTERNAL_STORAGE), 2)
        if (restored) toast("Accounts and settings restored from the backup.")
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

    // ================================================================ frame: header, pages, nav
    private fun header() = LinearLayout(this).apply {
        orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL
        setPadding(dp(18), dp(16), dp(18), dp(8))
        addView(FrameLayout(this@MainActivity).apply {
            background = GradientDrawable(GradientDrawable.Orientation.TL_BR, intArrayOf(accent, accent2)).apply { cornerRadius = dp(13).toFloat() }
            layoutParams = LinearLayout.LayoutParams(dp(42), dp(42))
            addView(ImageView(this@MainActivity).apply {
                setImageResource(R.drawable.ic_logo)
                layoutParams = FrameLayout.LayoutParams(dp(24), dp(24), Gravity.CENTER)
            })
        })
        addView(LinearLayout(this@MainActivity).apply {
            orientation = LinearLayout.VERTICAL; setPadding(dp(12), 0, 0, 0)
            layoutParams = LinearLayout.LayoutParams(0, -2, 1f)
            addView(text("AutoRejoin", 20f, text1, bold = true))
            addView(text("Mobile · v" + Updater.currentVersion(this@MainActivity), 12f, text2))
        })
        modePill = pill("…", text2, surface)
        addView(modePill)
        addView(pill("Discord", Color.WHITE, discord).apply {
            layoutParams = LinearLayout.LayoutParams(-2, -2).apply { leftMargin = dp(8) }
            setOnClickListener { open(DISCORD_URL) }
        })
    }

    private fun page(build: (LinearLayout) -> Unit) = ScrollView(this).apply {
        isFillViewport = true
        val col = LinearLayout(this@MainActivity).apply { orientation = LinearLayout.VERTICAL; setPadding(dp(18), dp(4), dp(18), dp(28)) }
        build(col)
        addView(col)
    }

    private fun navBar() = LinearLayout(this).apply {
        orientation = LinearLayout.HORIZONTAL
        setBackgroundColor(Color.parseColor("#0E0E13"))
        setPadding(dp(6), dp(6), dp(6), dp(8))
        listOf("👥" to "Accounts", "⚙️" to "Settings", "📜" to "Log", "❔" to "Help").forEachIndexed { i, (ico, label) ->
            val item = TextView(this@MainActivity).apply {
                text = "$ico\n$label"; gravity = Gravity.CENTER; textSize = 11.5f
                setPadding(0, dp(6), 0, dp(6))
                layoutParams = LinearLayout.LayoutParams(0, -2, 1f).apply { leftMargin = dp(3); rightMargin = dp(3) }
                setOnClickListener { showTab(i) }
            }
            navItems.add(item)
            addView(item)
        }
    }

    private fun showTab(i: Int) {
        pages.forEachIndexed { j, p -> p.visibility = if (i == j) View.VISIBLE else View.GONE }
        navItems.forEachIndexed { j, t ->
            t.setTextColor(if (i == j) text1 else text3)
            t.background = if (i == j) rounded(Color.parseColor("#1D1830"), null, 14) else null
            t.typeface = if (i == j) Typeface.DEFAULT_BOLD else Typeface.DEFAULT
        }
    }

    // ================================================================ Accounts
    private fun accountsPage(col: LinearLayout) {
        val hero = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            background = GradientDrawable(GradientDrawable.Orientation.TL_BR, intArrayOf(Color.parseColor("#1B1530"), surface)).apply {
                cornerRadius = dp(20).toFloat(); setStroke(dp(1), Color.parseColor("#2C2547"))
            }
            setPadding(dp(18), dp(18), dp(18), dp(18))
            layoutParams = LinearLayout.LayoutParams(-1, -2).apply { topMargin = dp(8) }
        }
        val statusRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL }
        statusDot = View(this).apply { layoutParams = LinearLayout.LayoutParams(dp(10), dp(10)).apply { rightMargin = dp(10) } }
        statusRow.addView(statusDot)
        statusTitle = text("Stopped", 20f, text1, bold = true)
        statusRow.addView(statusTitle)
        hero.addView(statusRow)
        statusSub = text("", 13f, text2).apply { setPadding(0, dp(4), 0, 0) }
        hero.addView(statusSub)
        val buttons = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            layoutParams = LinearLayout.LayoutParams(-1, -2).apply { topMargin = dp(16) }
        }
        startBtn = bigButton().apply {
            layoutParams = LinearLayout.LayoutParams(0, dp(52), 1.4f).apply { rightMargin = dp(10) }
            setOnClickListener { toggle() }
        }
        rejoinAllBtn = bigButton().apply {
            text = "↻  Rejoin all"
            layoutParams = LinearLayout.LayoutParams(0, dp(52), 1f)
            setOnClickListener { rejoin(null) }
        }
        buttons.addView(startBtn)
        buttons.addView(rejoinAllBtn)
        hero.addView(buttons)
        chips = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; layoutParams = LinearLayout.LayoutParams(-1, -2).apply { topMargin = dp(14) } }
        hero.addView(chips)
        col.addView(hero)

        // section title with the + Add account button on the right
        val titleRow = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL
            setPadding(dp(4), dp(22), 0, dp(8))
        }
        titleRow.addView(text("ACCOUNTS", 12f, text3, bold = true).apply { letterSpacing = 0.12f; layoutParams = LinearLayout.LayoutParams(0, -2, 1f) })
        titleRow.addView(pill("＋  Add account", Color.WHITE, accent).apply { setOnClickListener { pickApp() } })
        col.addView(titleRow)
        accountsBox = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        col.addView(accountsBox)
        col.addView(hint("One account = one Roblox app or clone, already logged in (no cookies). Tap ＋ Add account and pick the app — AutoRejoin opens the game in exactly that app. With root, names are found automatically.").apply { setPadding(dp(4), dp(10), dp(4), 0) })
        seedAccounts()
        buildAccounts(Store.accounts(this))
    }

    /** First start of 1.6: accounts used to be every installed Roblox app — keep them so nothing changes for old users. */
    private fun seedAccounts() {
        if (Store.get(this, "accountsManaged", "0") == "1") return
        if (Store.accounts(this).isEmpty()) Store.saveAccounts(this, Roblox.installedApps(this).map { Account(it) })
        Store.put(this, "accountsManaged", "1")
    }

    private fun buildAccounts(list: List<Account>, expand: String? = null) {
        rows.clear()
        accountsBox.removeAllViews()
        if (list.isEmpty()) {
            accountsBox.addView(card().apply {
                gravity = Gravity.CENTER_HORIZONTAL
                addView(text("No accounts yet", 15f, text1, bold = true).apply { gravity = Gravity.CENTER })
                addView(hint(if (Roblox.installedApps(this@MainActivity).isEmpty()) "No Roblox apps found — install Roblox (or a clone) first."
                    else "Tap ＋ Add account and pick a Roblox app or clone.").apply { gravity = Gravity.CENTER })
                addView(pill("＋  Add account", Color.WHITE, accent).apply {
                    layoutParams = LinearLayout.LayoutParams(-2, -2).apply { topMargin = dp(12) }
                    setOnClickListener { pickApp() }
                })
            })
        }
        list.forEachIndexed { i, acc -> accountsBox.addView(accountCard(acc, i, acc.pkg == expand)) }
    }

    /** Accounts as they are on screen now (unsaved edits included). */
    private fun currentAccounts(): MutableList<Account> = rows.map { r ->
        val name = r.user.text.toString().trim()
        Account(r.acc.pkg, name, if (name.equals(r.acc.username, true)) r.acc.userId else 0L, r.link.text.toString().trim(), r.sw.isChecked)
    }.toMutableList()

    private fun appLabel(pkg: String): String = try {
        packageManager.getApplicationLabel(packageManager.getApplicationInfo(pkg, 0)).toString()
    } catch (e: Exception) { pkg }

    private fun installed(pkg: String) = try { packageManager.getApplicationInfo(pkg, 0); true } catch (e: Exception) { false }

    /** + Add account: list of Roblox apps and clones on the phone; the picked one becomes an account. */
    private fun pickApp() {
        val current = currentAccounts()
        val added = current.map { it.pkg }.toSet()
        val apps = Roblox.installedApps(this)
        if (apps.isEmpty()) { toast("No Roblox apps found — install Roblox or a Roblox clone first."); return }
        val list = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; setPadding(dp(14), dp(6), dp(14), dp(6)) }
        var dialog: AlertDialog? = null
        for (pkg in apps) {
            val taken = pkg in added
            val row = LinearLayout(this).apply {
                orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL
                background = rounded(surface, stroke, 14)
                setPadding(dp(12), dp(10), dp(12), dp(10))
                layoutParams = LinearLayout.LayoutParams(-1, -2).apply { topMargin = dp(8) }
                alpha = if (taken) 0.45f else 1f
            }
            row.addView(ImageView(this).apply {
                try { setImageDrawable(packageManager.getApplicationIcon(pkg)) } catch (e: Exception) {}
                layoutParams = LinearLayout.LayoutParams(dp(38), dp(38)).apply { rightMargin = dp(12) }
            })
            row.addView(LinearLayout(this).apply {
                orientation = LinearLayout.VERTICAL
                layoutParams = LinearLayout.LayoutParams(0, -2, 1f)
                addView(text(appLabel(pkg), 15f, text1, bold = true))
                addView(text(pkg, 11.5f, text3))
            })
            row.addView(text(if (taken) "Added" else "Add", 13f, if (taken) text3 else accent, bold = true))
            if (!taken) row.setOnClickListener {
                dialog?.dismiss()
                current.add(Account(pkg))
                Store.saveAccounts(this, current)
                Backup.save(this)
                buildAccounts(current, expand = pkg)
                toast("${appLabel(pkg)} added. Type the username if you have no root.")
                if (RejoinService.isRunning) restartService()
            }
            list.addView(row)
        }
        dialog = AlertDialog.Builder(this, AlertDialog.THEME_DEVICE_DEFAULT_DARK)
            .setTitle("Pick the Roblox app for this account")
            .setView(ScrollView(this).apply { addView(list) })
            .setNegativeButton("Cancel", null)
            .show()
    }

    private fun removeAccount(pkg: String) {
        AlertDialog.Builder(this, AlertDialog.THEME_DEVICE_DEFAULT_DARK)
            .setTitle("Remove ${appLabel(pkg)}?")
            .setMessage("AutoRejoin stops opening the game in this app. The app itself is not touched — you can add it again any time.")
            .setPositiveButton("Remove") { _, _ ->
                val list = currentAccounts().filter { it.pkg != pkg }
                Store.saveAccounts(this, list)
                Backup.save(this)
                buildAccounts(list)
                if (RejoinService.isRunning) restartService()
            }
            .setNegativeButton("Cancel", null)
            .show()
    }

    private fun accountCard(acc: Account, i: Int, expanded: Boolean = false): View {
        val c = card()
        val head = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL }
        head.addView(avatarView(acc, i))
        val names = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL; setPadding(dp(12), 0, dp(8), 0)
            layoutParams = LinearLayout.LayoutParams(0, -2, 1f)
        }
        val name = text(acc.username.ifBlank { appLabel(acc.pkg) }, 15f, text1, bold = true)
        names.addView(name)
        val ok = installed(acc.pkg)
        names.addView(text(if (ok) "${appLabel(acc.pkg)} · ${acc.pkg}" else "App not installed · ${acc.pkg}", 11f, if (ok) text3 else red).apply {
            maxLines = 1; ellipsize = android.text.TextUtils.TruncateAt.END
        })
        val statusLine = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL; setPadding(0, dp(3), 0, 0) }
        val dot = View(this).apply {
            layoutParams = LinearLayout.LayoutParams(dp(7), dp(7)).apply { rightMargin = dp(6) }
            background = GradientDrawable().apply { shape = GradientDrawable.OVAL; setColor(text3) }
        }
        statusLine.addView(dot)
        val status = text("Stopped", 12f, text2)
        statusLine.addView(status)
        names.addView(statusLine)
        head.addView(names)
        head.addView(TextView(this).apply {
            text = "↻"; textSize = 18f; setTextColor(text1); gravity = Gravity.CENTER
            background = rounded(Color.parseColor("#1F1F27"), stroke, 12)
            layoutParams = LinearLayout.LayoutParams(dp(40), dp(40)).apply { rightMargin = dp(8) }
            setOnClickListener { rejoin(acc.pkg) }
        })
        val sw = switch(acc.enabled)
        head.addView(sw)
        c.addView(head)

        val edit = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; visibility = if (expanded) View.VISIBLE else View.GONE }
        val user = field(acc.username, "Roblox username (needed without root)")
        val link = field(acc.link, "Own game / VIP link — optional")
        edit.addView(user)
        edit.addView(link)
        edit.addView(text("Remove account", 13f, red, bold = true).apply {
            setPadding(dp(2), dp(12), 0, dp(2))
            setOnClickListener { removeAccount(acc.pkg) }
        })
        c.addView(text(if (expanded) "Hide ▴" else "Edit ▾", 12.5f, accent).apply {
            setPadding(0, dp(10), 0, 0)
            setOnClickListener {
                val show = edit.visibility != View.VISIBLE
                edit.visibility = if (show) View.VISIBLE else View.GONE
                text = if (show) "Hide ▴" else "Edit ▾"
            }
        })
        c.addView(edit)
        rows.add(Row(acc, user, link, sw, status, dot, name))
        return c
    }

    // ================================================================ Settings
    private fun settingsPage(col: LinearLayout) {
        section(col, "TOP AFK & BOTS")
        cloudBox = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        col.addView(cloudBox)
        renderCloud()

        section(col, "GAME")
        val game = card()
        game.addView(text("Game for all accounts", 15f, text1, bold = true))
        game.addView(hint("Tap a popular game or paste a place ID, game link or VIP link. Each account can have its own in Accounts → Edit."))
        linkBox = field(Store.get(this, "link", Roblox.BEE_SWARM), "1537690962")
        val gameChips = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        GAMES.forEach { (label, place) ->
            gameChips.addView(TextView(this).apply {
                text = label; textSize = 12.5f; setTextColor(text1)
                background = rounded(Color.parseColor("#1A1A22"), stroke, 99)
                setPadding(dp(12), dp(7), dp(12), dp(7))
                layoutParams = LinearLayout.LayoutParams(-2, -2).apply { rightMargin = dp(6) }
                setOnClickListener { linkBox.setText(place); toast("$label selected") }
            })
        }
        game.addView(HorizontalScrollView(this).apply {
            isHorizontalScrollBarEnabled = false
            layoutParams = LinearLayout.LayoutParams(-1, -2).apply { topMargin = dp(10) }
            addView(gameChips)
        })
        game.addView(linkBox)
        col.addView(game)

        section(col, "SPEED")
        val speed = card()
        val speedRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        graceBox = number(Store.get(this, "grace", "20"))
        joinBox = number(Store.get(this, "joinTimeout", "60"))
        speedRow.addView(labeled("Rejoin after out of game, s", graceBox, rightGap = true))
        speedRow.addView(labeled("Wait for a launch, s", joinBox, rightGap = false))
        speed.addView(speedRow)
        freezeBox = number(Store.get(this, "freeze", "120"))
        speed.addView(labeled("Game frozen after no heartbeat, s (root + executor)", freezeBox, rightGap = false).apply {
            layoutParams = LinearLayout.LayoutParams(-1, -2).apply { topMargin = dp(12) }
        })
        speed.addView(hint("Teleports into matches take ~10–20 s, so keep the first one above that."))
        col.addView(speed)

        section(col, "SCHEDULE")
        val sched = card()
        scheduleSwitch = switch(Store.get(this, "scheduleOn", "0") == "1")
        sched.addView(switchRow("Rejoin all accounts every day", scheduleSwitch))
        scheduleBox = field(Store.get(this, "scheduleAt", "06:00"), "06:00").apply { inputType = InputType.TYPE_CLASS_DATETIME or InputType.TYPE_DATETIME_VARIATION_TIME }
        sched.addView(labeled("At (phone time, HH:MM)", scheduleBox, rightGap = false).apply { layoutParams = LinearLayout.LayoutParams(-1, -2).apply { topMargin = dp(10) } })
        sched.addView(hint("A fresh rejoin once a day clears memory leaks and stuck servers — like the schedule in the PC app."))
        col.addView(sched)

        section(col, "EXECUTOR")
        val exec = card()
        execSwitch = switch(Store.get(this, "execScript", "1") == "1")
        exec.addView(switchRow("Watcher script in autoexec", execSwitch))
        exec.addView(hint("Root: a small script in the autoexec of Delta, Arceus X, Codex and others reports a frozen game and the Disconnected prompt instantly."))
        execStatus = hint("Looking for executors…").apply { setTextColor(text3) }
        exec.addView(execStatus)
        farmSwitch = switch(Store.get(this, "farmOn", "1") == "1")
        exec.addView(switchRow("Bee Swarm farm script", farmSwitch).apply { layoutParams = LinearLayout.LayoutParams(-1, -2).apply { topMargin = dp(16) } })
        exec.addView(hint("Put into autoexec, runs only in Bee Swarm. Atlas by default — paste your own loadstring to change it."))
        farmBox = field(Store.get(this, "farmScript", Executors.ATLAS), Executors.ATLAS).apply { typeface = Typeface.MONOSPACE; textSize = 12f }
        exec.addView(farmBox)
        col.addView(exec)

        section(col, "DISCORD ALERTS")
        val hook = card()
        hook.addView(hint("Webhook URL — drop and back-in-game messages."))
        hookBox = field(Store.get(this, "webhook"), "https://discord.com/api/webhooks/…")
        hook.addView(hookBox)
        reportBox = number(Store.get(this, "reportMin", "0"))
        hook.addView(labeled("Status report every N minutes (0 = off)", reportBox, rightGap = false).apply { layoutParams = LinearLayout.LayoutParams(-1, -2).apply { topMargin = dp(12) } })
        shotSwitch = switch(Store.get(this, "shots", "1") == "1")
        hook.addView(switchRow("Screenshot with drop alerts (root)", shotSwitch).apply { layoutParams = LinearLayout.LayoutParams(-1, -2).apply { topMargin = dp(12) } })
        col.addView(hook)

        section(col, "APP")
        val app = card()
        updateSwitch = switch(Store.get(this, "autoUpdate", "1") == "1")
        app.addView(switchRow("Update automatically", updateSwitch))
        app.addView(smallButton("Check for updates now") {
            toast("Checking for updates…")
            Thread {
                val started = Updater.checkAndInstall(this) { RejoinService.log(it) }
                if (!started) runOnUiThread { toast("You have the latest version (" + Updater.currentVersion(this) + ").") }
            }.start()
        })
        app.addView(smallButton("Display over other apps") { open(Settings.ACTION_MANAGE_OVERLAY_PERMISSION) })
        app.addView(smallButton("Don't optimize battery") { open(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS) })
        if (Build.VERSION.SDK_INT >= 26) app.addView(smallButton("Install updates (unknown apps)") { open(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES) })
        col.addView(app)

        col.addView(bigButton().apply {
            text = "Save settings"; setTextColor(Color.BLACK); background = rounded(Color.WHITE, null, 16)
            layoutParams = LinearLayout.LayoutParams(-1, dp(52)).apply { topMargin = dp(18) }
            setOnClickListener {
                if (!saveAll()) return@setOnClickListener
                if (RejoinService.isRunning) { restartService(); toast("Saved — AutoRejoin restarted with the new settings.") } else toast("Saved.")
            }
        })
    }

    // ================================================================ Log, Help
    private fun logPage(col: LinearLayout) {
        val head = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL; setPadding(dp(4), dp(8), dp(4), 0) }
        head.addView(text("LOG", 11.5f, text3, bold = true).apply { letterSpacing = 0.12f; layoutParams = LinearLayout.LayoutParams(0, -2, 1f) })
        head.addView(text("Copy", 13f, accent).apply {
            setPadding(dp(12), 0, dp(12), 0)
            setOnClickListener {
                (getSystemService(CLIPBOARD_SERVICE) as ClipboardManager).setPrimaryClip(ClipData.newPlainText("AutoRejoin log", RejoinService.history()))
                toast("Log copied.")
            }
        })
        head.addView(text("Clear", 13f, accent).apply { setOnClickListener { logView.text = "" } })
        col.addView(head)
        logView = TextView(this).apply {
            setTextColor(Color.parseColor("#C9C9D1")); textSize = 11.5f; typeface = Typeface.MONOSPACE
            setLineSpacing(0f, 1.15f)
            background = rounded(surface2, stroke, 16); setPadding(dp(14), dp(12), dp(14), dp(12)); setTextIsSelectable(true)
            minHeight = dp(300)
            layoutParams = LinearLayout.LayoutParams(-1, -2).apply { topMargin = dp(8) }
        }
        col.addView(logView)
    }

    private fun helpPage(col: LinearLayout) {
        section(col, "HOW IT WORKS")
        val how = card()
        listOf(
            "1. Pick the game" to "Settings → Game: a popular game, a place ID or your VIP link.",
            "2. Turn on your Roblox apps" to "Accounts: every Roblox app and clone is one account. Without root, add each account's username.",
            "3. Fix yellow checks" to "Tap Overlay, Battery and Updates on the Accounts page. Root is optional but makes rejoins 3–5 s.",
            "4. Start" to "AutoRejoin keeps accounts in game: drops, kicks, error 256, frozen game, closed app. ↻ rejoins one account, Rejoin all — everyone.",
        ).forEach { (t, d) ->
            how.addView(text(t, 14.5f, text1, bold = true).apply { setPadding(0, dp(8), 0, 0) })
            how.addView(hint(d))
        }
        col.addView(how)
        section(col, "COMMUNITY")
        val links = card()
        links.addView(hint("Questions, bugs, ideas and updates — in our Discord."))
        links.addView(bigButton().apply {
            text = "Join our Discord"; setTextColor(Color.WHITE); background = rounded(discord, null, 16)
            layoutParams = LinearLayout.LayoutParams(-1, dp(50)).apply { topMargin = dp(12) }
            setOnClickListener { open(DISCORD_URL) }
        })
        links.addView(smallButton("Website & PC version") { open(SITE_URL) })
        col.addView(links)
        col.addView(text("AutoRejoin · free · updates itself", 11.5f, text3).apply { gravity = Gravity.CENTER; setPadding(0, dp(18), 0, 0) })
    }

    // ================================================================ Top AFK account & Telegram / Discord bots
    private fun renderCloud() {
        cloudBox.removeAllViews()
        val c = card()
        if (!Cloud.signedIn(this)) {
            c.addView(text("Control this phone from Telegram & Discord", 15f, text1, bold = true))
            c.addView(hint("Sign in with your AutoRejoin account — the same as on PC and the site. Then the bots show this phone's accounts and can start, stop, rejoin and take screenshots; drops come to Telegram."))
            val row = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; layoutParams = LinearLayout.LayoutParams(-1, -2).apply { topMargin = dp(14) } }
            row.addView(bigButton().apply {
                text = "Sign in"; setTextColor(Color.BLACK); background = rounded(Color.WHITE, null, 14)
                layoutParams = LinearLayout.LayoutParams(0, dp(46), 1f).apply { rightMargin = dp(10) }
                setOnClickListener { signInDialog() }
            })
            row.addView(bigButton().apply {
                text = "Create account"; setTextColor(text1); background = rounded(Color.parseColor("#1F1F27"), stroke, 14)
                layoutParams = LinearLayout.LayoutParams(0, dp(46), 1f)
                setOnClickListener { registerDialog() }
            })
            c.addView(row)
            c.addView(text("Forgot password?", 12.5f, accent).apply { setPadding(0, dp(12), 0, 0); setOnClickListener { open(Cloud.SITE + "forgot") } })
        } else {
            c.addView(text("Signed in as " + Cloud.nickname(this).ifBlank { "?" }, 15f, text1, bold = true))
            val bots = hint("Checking the bots…")
            c.addView(bots)
            c.addView(hint("☁️ Accounts and settings are saved to your account automatically — after a reinstall just sign in."))
            c.addView(hint("In the bots this phone is \"${Cloud.deviceName(this)}\" — press 📱 Phones there to see only phones. Control works while AutoRejoin is started here."))
            val row = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; layoutParams = LinearLayout.LayoutParams(-1, -2).apply { topMargin = dp(14) } }
            row.addView(bigButton().apply {
                text = "🔗  Link bot"; setTextColor(Color.WHITE); background = rounded(accent, null, 14)
                layoutParams = LinearLayout.LayoutParams(0, dp(46), 1.3f).apply { rightMargin = dp(10) }
                setOnClickListener { linkBotDialog() }
            })
            row.addView(bigButton().apply {
                text = "Sign out"; setTextColor(text1); background = rounded(Color.parseColor("#1F1F27"), stroke, 14)
                layoutParams = LinearLayout.LayoutParams(0, dp(46), 1f)
                setOnClickListener {
                    AlertDialog.Builder(this@MainActivity, AlertDialog.THEME_DEVICE_DEFAULT_DARK)
                        .setTitle("Sign out?").setMessage("The bots stop seeing this phone.")
                        .setPositiveButton("Sign out") { _, _ -> Cloud.logout(this@MainActivity); renderCloud() }
                        .setNegativeButton("Cancel", null).show()
                }
            })
            c.addView(row)
            Thread {
                val text = try {
                    val s = Cloud.linkState(this)
                    "Telegram: " + (if (s.telegram) "✅ linked" + (s.telegramName?.let { " ($it)" } ?: "") else "not linked") +
                        "   ·   Discord: " + (if (s.discord) "✅ linked" + (s.discordName?.let { " ($it)" } ?: "") else "not linked")
                } catch (e: Cloud.ApiError) {
                    if (e.code == 401) { Store.put(this, "cloudToken", ""); runOnUiThread { toast("Session expired — sign in again."); renderCloud() }; return@Thread }
                    "Can't check the bots: ${e.message}"
                } catch (e: Exception) { "Can't reach the server — check the internet." }
                runOnUiThread { bots.text = text }
            }.start()
        }
        cloudBox.addView(c)
    }

    /** A dialog with fields; OK runs [work] off the UI thread and closes the dialog only when it worked. */
    private fun formDialog(title: String, message: String?, fields: List<EditText>, ok: String, work: () -> Unit, done: () -> Unit) {
        val box = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; setPadding(dp(20), dp(4), dp(20), 0) }
        if (message != null) box.addView(text(message, 13f, text2).apply { setPadding(dp(2), 0, 0, dp(4)) })
        fields.forEach { box.addView(it) }
        val d = AlertDialog.Builder(this, AlertDialog.THEME_DEVICE_DEFAULT_DARK)
            .setTitle(title).setView(box).setPositiveButton(ok, null).setNegativeButton("Cancel", null).show()
        val button = d.getButton(AlertDialog.BUTTON_POSITIVE)
        button.setOnClickListener {
            button.isEnabled = false
            Thread {
                try {
                    work()
                    runOnUiThread { d.dismiss(); done() }
                } catch (e: Exception) {
                    runOnUiThread { button.isEnabled = true; toast(if (e is Cloud.ApiError) e.message ?: "Error" else "Can't reach the server — check the internet.") }
                }
            }.start()
        }
    }

    private fun password(hintText: String) = field("", hintText).apply { inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD }

    private fun signInDialog() {
        val nick = field("", "Nickname or email")
        val pass = password("Password")
        formDialog("Sign in to AutoRejoin", "The same account as on PC and the site.", listOf(nick, pass), "Sign in",
            { Cloud.login(this, nick.text.toString().trim(), pass.text.toString()) },
            { toast("Signed in as ${Cloud.nickname(this)}."); renderCloud(); afterSignIn() })
    }

    private fun registerDialog() {
        val nick = field("", "Nickname (3–20: a-z, 0-9, _)")
        val email = field("", "Email").apply { inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_EMAIL_ADDRESS }
        val pass = password("Password (6+ characters)")
        var sentTo = ""
        formDialog("Create an account", "A 6-digit code will come to your email.", listOf(nick, email, pass), "Get code",
            { sentTo = Cloud.registerStart(nick.text.toString().trim(), email.text.toString().trim(), pass.text.toString()) },
            { codeDialog(sentTo) })
    }

    private fun codeDialog(email: String) {
        val code = field("", "6-digit code").apply { inputType = InputType.TYPE_CLASS_NUMBER }
        formDialog("Confirm your email", "We sent a code to $email (check spam too). It works for 15 minutes.", listOf(code), "Create account",
            { Cloud.registerConfirm(this, email, code.text.toString().trim()) },
            { toast("Account ${Cloud.nickname(this)} created."); renderCloud(); Thread { Backup.uploadNow(this) }.start(); linkBotDialog() })
    }

    /** Signed in: take the backup kept in the account (fresh install) or offer it, else save this phone's setup there. */
    private fun afterSignIn() {
        Thread {
            when (val r = Backup.syncAfterSignIn(this)) {
                is Backup.AfterSignIn.Restored -> runOnUiThread { toast("Accounts and settings restored from your account."); recreate() }
                is Backup.AfterSignIn.Ask -> runOnUiThread {
                    val n = Backup.accountCount(r.data)
                    val at = java.text.SimpleDateFormat("d MMM, HH:mm", java.util.Locale.US).format(java.util.Date(r.data.optLong("savedAt")))
                    AlertDialog.Builder(this, AlertDialog.THEME_DEVICE_DEFAULT_DARK)
                        .setTitle("Restore from your account?")
                        .setMessage("Your account has a backup from $at with $n account(s). Restore it? It replaces the accounts and settings on this phone.")
                        .setPositiveButton("Restore") { _, _ -> if (Backup.apply(this, r.data)) { toast("Restored."); recreate() } }
                        .setNegativeButton("Keep this phone's") { _, _ -> Thread { Backup.uploadNow(this) }.start() }
                        .show()
                }
                else -> {}
            }
        }.start()
    }

    private fun linkBotDialog() {
        Thread {
            try {
                val lc = Cloud.linkCode(this)
                runOnUiThread { showLinkCode(lc) }
            } catch (e: Exception) {
                runOnUiThread { toast(if (e is Cloud.ApiError) e.message ?: "Error" else "Can't reach the server — check the internet.") }
            }
        }.start()
    }

    private fun showLinkCode(lc: Cloud.LinkCode) {
        val box = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; setPadding(dp(22), dp(6), dp(22), 0) }
        box.addView(text(lc.code.chunked(3).joinToString(" "), 34f, text1, bold = true).apply { gravity = Gravity.CENTER; letterSpacing = 0.1f; setPadding(0, dp(6), 0, dp(10)) })
        box.addView(text("Telegram: tap Telegram below and press Start — the code goes by itself (or send the 6 digits to the bot).\n\n" +
            "Discord: on our server press «Link account» on the AutoRejoin panel (or type /panel) and enter the code.\n\n" +
            "The code works once, for 10 minutes. One code fits both bots — get a new one for the second.", 13f, text2))
        val b = AlertDialog.Builder(this, AlertDialog.THEME_DEVICE_DEFAULT_DARK).setTitle("Link a bot").setView(box)
        if (lc.telegram != null) b.setPositiveButton("Telegram") { _, _ -> open(lc.telegram + "?start=" + lc.code) }
        b.setNeutralButton("Discord") { _, _ -> open(DISCORD_URL) }
        b.setNegativeButton("Copy code") { _, _ ->
            (getSystemService(CLIPBOARD_SERVICE) as ClipboardManager).setPrimaryClip(ClipData.newPlainText("code", lc.code))
            toast("Code copied.")
        }
        b.setOnDismissListener { renderCloud() }
        b.show()
    }

    // ================================================================ actions
    private fun saveAll(): Boolean {
        val link = linkBox.text.toString().trim().ifEmpty { Roblox.BEE_SWARM }
        try { Roblox.deepLink(link) } catch (e: Exception) { toast(e.message ?: "Bad link"); showTab(1); return false }
        val list = currentAccounts()
        for (a in list) {
            if (a.link.isNotEmpty()) try { Roblox.deepLink(a.link) } catch (e: Exception) { toast("${appLabel(a.pkg)}: ${e.message}"); showTab(0); return false }
        }
        val at = scheduleBox.text.toString().trim()
        if (scheduleSwitch.isChecked && !Regex("([01]?\\d|2[0-3]):[0-5]\\d").matches(at)) { toast("Schedule time: use HH:MM, e.g. 06:00"); showTab(1); return false }
        Store.put(this, "link", link)
        Store.put(this, "webhook", hookBox.text.toString().trim())
        Store.put(this, "grace", (graceBox.text.toString().toIntOrNull() ?: 20).coerceIn(5, 600).toString())
        Store.put(this, "joinTimeout", (joinBox.text.toString().toIntOrNull() ?: 60).coerceIn(20, 600).toString())
        Store.put(this, "freeze", (freezeBox.text.toString().toIntOrNull() ?: 120).coerceIn(30, 1800).toString())
        Store.put(this, "reportMin", (reportBox.text.toString().toIntOrNull() ?: 0).coerceIn(0, 1440).toString())
        Store.put(this, "shots", if (shotSwitch.isChecked) "1" else "0")
        Store.put(this, "execScript", if (execSwitch.isChecked) "1" else "0")
        Store.put(this, "farmOn", if (farmSwitch.isChecked) "1" else "0")
        Store.put(this, "farmScript", farmBox.text.toString().trim().ifEmpty { Executors.ATLAS })
        Store.put(this, "scheduleOn", if (scheduleSwitch.isChecked) "1" else "0")
        Store.put(this, "scheduleAt", at.ifEmpty { "06:00" })
        Store.put(this, "autoUpdate", if (updateSwitch.isChecked) "1" else "0")
        Store.saveAccounts(this, list)
        Backup.save(this)   // phone file + account copy: a reinstall gets everything back
        return true
    }

    private fun toggle() {
        if (RejoinService.isRunning) {
            startService(Intent(this, RejoinService::class.java).setAction(RejoinService.ACTION_STOP))
            Store.put(this, "running", "0")
            RejoinService.isRunning = false
            refreshStatus()
            return
        }
        if (!saveAll()) return
        if (Store.accounts(this).none { it.enabled }) { toast("Add an account (＋ Add account) and turn it on."); return }
        startRejoin()
    }

    private fun startRejoin() {
        val intent = Intent(this, RejoinService::class.java)
        if (Build.VERSION.SDK_INT >= 26) startForegroundService(intent) else startService(intent)
        RejoinService.isRunning = true
        Store.put(this, "running", "1")   // Events restarts the service after an update or a reboot
        refreshStatus()
    }

    private fun restartService() {
        startService(Intent(this, RejoinService::class.java).setAction(RejoinService.ACTION_STOP))
        ui.postDelayed({ startRejoin() }, 1500)
    }

    private fun rejoin(pkg: String?) {
        if (!RejoinService.isRunning) { toast("Press Start first."); return }
        startService(Intent(this, RejoinService::class.java).setAction(RejoinService.ACTION_REJOIN).apply { if (pkg != null) putExtra("pkg", pkg) })
        toast(if (pkg == null) "Rejoining all accounts…" else "Rejoining…")
    }

    private fun refreshStatus() {
        val running = RejoinService.isRunning
        statusDot.background = GradientDrawable().apply { shape = GradientDrawable.OVAL; setColor(if (running) green else text3) }
        statusTitle.text = if (running) "Running" else "Stopped"
        statusSub.text = when {
            running && RejoinService.paused -> "Stopped from a bot — press Start there to rejoin again."
            running && RejoinService.total > 0 -> "${RejoinService.inGame} of ${RejoinService.total} in game · ${RejoinService.rejoinsTotal} rejoins"
            running -> "Starting…"
            else -> "Press Start — AutoRejoin keeps your accounts in game."
        }
        startBtn.text = if (running) "■  Stop" else "▶  Start"
        startBtn.setTextColor(if (running) red else Color.BLACK)
        startBtn.background = if (running) rounded(Color.parseColor("#1A1214"), Color.parseColor("#4A2326"), 16) else rounded(Color.WHITE, null, 16)
        rejoinAllBtn.setTextColor(if (running) text1 else text3)
        rejoinAllBtn.background = rounded(Color.parseColor("#1F1F27"), stroke, 16)
        // account cards: live status from the service
        val snap = RejoinService.snapshot.associateBy { it.pkg }
        for (r in rows) {
            val st = snap[r.acc.pkg]
            val (label, color) = when {
                !running -> (if (r.sw.isChecked) "Stopped" else "Off") to text3
                !r.sw.isChecked -> "Off" to text3
                st == null -> "Waiting" to text3
                st.inGame -> "In game" + (if (st.rejoins > 0) " · ${st.rejoins} rejoins" else "") to green
                st.status.startsWith("Paused") -> st.status to red
                else -> st.status + (if (st.rejoins > 0) " · ${st.rejoins} rejoins" else "") to amber
            }
            r.status.text = label
            r.status.setTextColor(if (color == text3) text2 else color)
            r.dot.background = GradientDrawable().apply { shape = GradientDrawable.OVAL; setColor(color) }
            if (st != null && st.name.isNotBlank() && !st.name.contains('.')) r.name.text = st.name
        }
        Thread {
            val root = Root.available()
            val overlay = Build.VERSION.SDK_INT < 23 || Settings.canDrawOverlays(this)
            val battery = (getSystemService(POWER_SERVICE) as PowerManager).isIgnoringBatteryOptimizations(packageName)
            val installs = Build.VERSION.SDK_INT < 26 || packageManager.canRequestPackageInstalls()
            val execs = if (root) Executors.autoexecDirs() else emptyList()
            runOnUiThread {
                execStatus.text = when {
                    !root -> "Needs root."
                    execs.isEmpty() -> "No executor autoexec folder found yet — open your executor once."
                    else -> "Found: " + execs.joinToString(" · ") { Executors.executorName(it) }
                }
                execStatus.setTextColor(if (root && execs.isNotEmpty()) green else text3)
                modePill.text = if (root) "ROOT" else "NO ROOT"
                modePill.setTextColor(if (root) green else amber)
                modePill.background = rounded(if (root) Color.parseColor("#0F2119") else Color.parseColor("#241C0C"), null, 99)
                chips.removeAllViews()
                chips.addView(chip("Root", root, null))
                chips.addView(chip("Overlay", overlay || root, Settings.ACTION_MANAGE_OVERLAY_PERMISSION))
                chips.addView(chip("Battery", battery, Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS))
                chips.addView(chip("Updates", installs || root, if (Build.VERSION.SDK_INT >= 26) Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES else null))
            }
        }.start()
    }

    private fun appendLog(line: String) {
        val color = when {
            Regex("in game", RegexOption.IGNORE_CASE).containsMatchIn(line) && !line.contains("not in", true) -> green
            Regex("rejoin|opening", RegexOption.IGNORE_CASE).containsMatchIn(line) -> amber
            Regex("error|failed|can't|no root|HTTP|froze", RegexOption.IGNORE_CASE).containsMatchIn(line) -> red
            Regex("update|root mode|executor", RegexOption.IGNORE_CASE).containsMatchIn(line) -> Color.parseColor("#B79CFF")
            else -> Color.parseColor("#C9C9D1")
        }
        val sb = SpannableStringBuilder()
        if (logView.text.isNotEmpty()) sb.append("\n")
        val start = sb.length
        sb.append(line.take(8))
        sb.setSpan(ForegroundColorSpan(text3), start, sb.length, Spanned.SPAN_EXCLUSIVE_EXCLUSIVE)
        val rest = sb.length
        sb.append(line.drop(8))
        sb.setSpan(ForegroundColorSpan(color), rest, sb.length, Spanned.SPAN_EXCLUSIVE_EXCLUSIVE)
        logView.append(sb)
    }

    private fun open(target: String) {
        try {
            val intent = if (target.startsWith("http")) Intent(Intent.ACTION_VIEW, Uri.parse(target))
                else Intent(target, Uri.parse("package:$packageName"))
            startActivity(intent)
        } catch (e: Exception) { toast(e.message ?: "Can't open") }
    }

    // ================================================================ avatars
    private fun avatarView(acc: Account, index: Int): View {
        val size = dp(44)
        val frame = FrameLayout(this).apply { layoutParams = LinearLayout.LayoutParams(size, size) }
        val palette = intArrayOf(accent, Color.parseColor("#0EA5E9"), Color.parseColor("#F59E0B"), Color.parseColor("#EC4899"), Color.parseColor("#10B981"))
        frame.addView(text((acc.username.ifBlank { if (acc.pkg == "com.roblox.client") "M" else "$index" }).take(1).uppercase(), 16f, Color.WHITE, bold = true).apply {
            gravity = Gravity.CENTER
            background = GradientDrawable().apply { shape = GradientDrawable.OVAL; setColor(palette[index % palette.size]) }
            layoutParams = FrameLayout.LayoutParams(size, size)
        })
        if (acc.userId > 0) {
            val img = ImageView(this).apply {
                layoutParams = FrameLayout.LayoutParams(size, size)
                scaleType = ImageView.ScaleType.CENTER_CROP
                clipToOutline = true
                background = GradientDrawable().apply { shape = GradientDrawable.OVAL; setColor(surface2) }
                visibility = View.GONE
            }
            frame.addView(img)
            Thread { headshot(acc.userId)?.let { bmp -> runOnUiThread { img.setImageBitmap(bmp); img.visibility = View.VISIBLE } } }.start()
        }
        return frame
    }

    private fun headshot(userId: Long): Bitmap? = try {
        val c = URL("https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds=$userId&size=150x150&format=Png&isCircular=true").openConnection() as HttpURLConnection
        c.connectTimeout = 10000; c.readTimeout = 10000; c.setRequestProperty("User-Agent", "AutoRejoin-Mobile")
        val url = JSONObject(c.inputStream.bufferedReader().readText()).getJSONArray("data").getJSONObject(0).getString("imageUrl")
        URL(url).openStream().use { BitmapFactory.decodeStream(it) }
    } catch (e: Exception) { null }

    // ================================================================ small UI helpers
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

    private fun section(col: LinearLayout, title: String) {
        col.addView(text(title, 11.5f, text3, bold = true).apply {
            letterSpacing = 0.12f
            layoutParams = LinearLayout.LayoutParams(-1, -2).apply { topMargin = dp(20); leftMargin = dp(4) }
        })
    }

    private fun text(t: String, size: Float, color: Int, bold: Boolean = false) = TextView(this).apply {
        text = t; textSize = size; setTextColor(color); if (bold) typeface = Typeface.create("sans-serif-medium", Typeface.BOLD)
    }

    private fun hint(t: String) = text(t, 12.5f, text2).apply { setPadding(0, dp(4), 0, 0); setLineSpacing(0f, 1.1f) }

    private fun pill(t: String, color: Int, back: Int) = TextView(this).apply {
        text = t; textSize = 11f; setTextColor(color); typeface = Typeface.DEFAULT_BOLD; letterSpacing = 0.06f
        background = rounded(back, null, 99); setPadding(dp(10), dp(6), dp(10), dp(6))
    }

    private fun chip(label: String, ok: Boolean, fix: String?) = TextView(this).apply {
        text = (if (ok) "✓ " else "! ") + label
        textSize = 12f; gravity = Gravity.CENTER; typeface = Typeface.DEFAULT_BOLD
        setTextColor(if (ok) green else amber)
        background = rounded(if (ok) Color.parseColor("#0F2119") else Color.parseColor("#241C0C"), null, 99)
        setPadding(dp(4), dp(7), dp(4), dp(7))
        layoutParams = LinearLayout.LayoutParams(0, -2, 1f).apply { rightMargin = dp(6) }
        if (!ok && fix != null) setOnClickListener { open(fix) }
    }

    private fun bigButton() = TextView(this).apply {
        textSize = 15.5f; typeface = Typeface.DEFAULT_BOLD; gravity = Gravity.CENTER
    }

    private fun smallButton(t: String, click: () -> Unit) = TextView(this).apply {
        text = t; textSize = 14f; setTextColor(text1); gravity = Gravity.CENTER
        background = rounded(Color.parseColor("#1F1F27"), stroke, 14)
        layoutParams = LinearLayout.LayoutParams(-1, dp(46)).apply { topMargin = dp(10) }
        setOnClickListener { click() }
    }

    private fun switchRow(label: String, sw: Switch) = LinearLayout(this).apply {
        orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL
        addView(text(label, 15f, text1, bold = true).apply { layoutParams = LinearLayout.LayoutParams(0, -2, 1f) })
        addView(sw)
    }

    private fun field(value: String, placeholder: String) = EditText(this).apply {
        setText(value); hint = placeholder; setHintTextColor(text3); setTextColor(text1); textSize = 14f
        inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_URI; isSingleLine = true
        background = rounded(surface2, stroke, 12); setPadding(dp(14), dp(12), dp(14), dp(12))
        layoutParams = LinearLayout.LayoutParams(-1, -2).apply { topMargin = dp(10) }
    }

    private fun number(value: String) = field(value, value).apply { inputType = InputType.TYPE_CLASS_NUMBER }

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
