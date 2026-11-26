package com.autorejoin.mobile

import android.Manifest
import android.app.Activity
import android.content.Intent
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.PowerManager
import android.provider.Settings
import android.text.InputType
import android.view.Gravity
import android.view.View
import android.widget.Button
import android.widget.CheckBox
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import android.widget.Toast

class MainActivity : Activity() {

    private val bg = Color.parseColor("#0A0A0A")
    private val card = Color.parseColor("#151515")
    private val line = Color.parseColor("#262626")
    private val text2 = Color.parseColor("#9A9A9A")

    private lateinit var root: LinearLayout
    private lateinit var logView: TextView
    private lateinit var status: TextView
    private lateinit var startBtn: Button
    private lateinit var linkBox: EditText
    private lateinit var hookBox: EditText
    private lateinit var graceBox: EditText
    private lateinit var joinBox: EditText
    private val rows = ArrayList<Triple<Account, EditText, EditText>>()
    private val checks = HashMap<String, CheckBox>()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        window.statusBarColor = bg
        val scroll = ScrollView(this).apply { setBackgroundColor(bg); isFillViewport = true }
        root = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; setPadding(dp(18), dp(22), dp(18), dp(28)) }
        scroll.addView(root)
        setContentView(scroll)
        build()
        if (Build.VERSION.SDK_INT >= 33) requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 1)
    }

    override fun onResume() {
        super.onResume()
        RejoinService.listener = { l -> runOnUiThread { logView.append((if (logView.text.isEmpty()) "" else "\n") + l) } }
        logView.text = RejoinService.history()
        refreshStatus()
    }

    override fun onPause() {
        RejoinService.listener = null
        super.onPause()
    }

    private fun build() {
        root.addView(TextView(this).apply { text = "AutoRejoin"; textSize = 26f; setTextColor(Color.WHITE); typeface = Typeface.DEFAULT_BOLD })
        root.addView(hint("Keeps your Roblox apps in game: rejoins on disconnect, kick, error 256 or a closed app."))
        status = hint("").apply { setPadding(0, dp(8), 0, 0) }
        root.addView(status)

        // game
        val gameCard = card()
        gameCard.addView(label("Game for all accounts"))
        gameCard.addView(hint("Place ID, game link or VIP link. Bee Swarm = 1537690962"))
        linkBox = field(Store.get(this, "link", Roblox.BEE_SWARM), "1537690962")
        gameCard.addView(linkBox)
        root.addView(gameCard)

        // accounts
        val accCard = card()
        accCard.addView(label("Accounts"))
        accCard.addView(hint("Every Roblox app (and clone) is one account — already logged in. Without root, enter its Roblox username so AutoRejoin can see whether it's in game."))
        val saved = Store.accounts(this).associateBy { it.pkg }
        val apps = Roblox.installedApps(this)
        if (apps.isEmpty()) accCard.addView(hint("No Roblox apps found. Install Roblox first.").apply { setTextColor(Color.parseColor("#E5484D")) })
        for (pkg in apps) {
            val acc = saved[pkg] ?: Account(pkg)
            val box = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; setPadding(0, dp(12), 0, 0) }
            val check = CheckBox(this).apply { text = pkg; isChecked = acc.enabled; setTextColor(Color.WHITE); textSize = 14f }
            checks[pkg] = check
            box.addView(check)
            val user = field(acc.username, "Roblox username")
            val link = field(acc.link, "Own game / VIP link (optional)")
            box.addView(user)
            box.addView(link)
            accCard.addView(box)
            rows.add(Triple(acc, user, link))
        }
        root.addView(accCard)

        // speed
        val speedCard = card()
        speedCard.addView(label("Speed"))
        speedCard.addView(hint("Rejoin after the account is out of a game for N seconds (teleports into matches take ~10–20 s). Wait N seconds for a fresh launch to get into a game before trying again."))
        graceBox = field(Store.get(this, "grace", "20"), "20").apply { inputType = InputType.TYPE_CLASS_NUMBER }
        joinBox = field(Store.get(this, "joinTimeout", "60"), "60").apply { inputType = InputType.TYPE_CLASS_NUMBER }
        speedCard.addView(hint("Out of game → rejoin after (s)").apply { setPadding(0, dp(10), 0, 0) })
        speedCard.addView(graceBox)
        speedCard.addView(hint("Wait for a launch to join (s)").apply { setPadding(0, dp(10), 0, 0) })
        speedCard.addView(joinBox)
        root.addView(speedCard)

        // discord
        val hookCard = card()
        hookCard.addView(label("Discord alerts (optional)"))
        hookCard.addView(hint("Webhook URL: drop and back-in-game messages."))
        hookBox = field(Store.get(this, "webhook"), "https://discord.com/api/webhooks/…")
        hookCard.addView(hookBox)
        root.addView(hookCard)

        // permissions
        val permCard = card()
        permCard.addView(label("Permissions"))
        permCard.addView(hint("Without root Android lets an app open Roblox from the background only with \"Display over other apps\". Battery: so the phone doesn't stop AutoRejoin."))
        permCard.addView(button("Display over other apps", false) {
            startActivity(Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION, Uri.parse("package:$packageName")))
        })
        permCard.addView(button("Don't optimize battery", false) {
            @Suppress("BatteryLife")
            startActivity(Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS, Uri.parse("package:$packageName")))
        })
        root.addView(permCard)

        startBtn = button("Start", true) { toggle() }
        root.addView(startBtn)

        root.addView(label("Log").apply { setPadding(0, dp(18), 0, dp(6)) })
        logView = TextView(this).apply {
            setTextColor(Color.parseColor("#D6D6D6")); textSize = 12f; typeface = Typeface.MONOSPACE
            background = rounded(card); setPadding(dp(12), dp(10), dp(12), dp(10)); setTextIsSelectable(true)
        }
        root.addView(logView)
    }

    private fun toggle() {
        if (RejoinService.isRunning) {
            startService(Intent(this, RejoinService::class.java).setAction(RejoinService.ACTION_STOP))
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
            list.add(Account(acc.pkg, name, id, ownLink, checks[acc.pkg]?.isChecked ?: true))
        }
        if (list.none { it.enabled }) { toast("Tick at least one Roblox app."); return }
        Store.put(this, "link", link)
        Store.put(this, "webhook", hookBox.text.toString().trim())
        Store.put(this, "grace", (graceBox.text.toString().toIntOrNull() ?: 20).coerceIn(5, 600).toString())
        Store.put(this, "joinTimeout", (joinBox.text.toString().toIntOrNull() ?: 60).coerceIn(20, 600).toString())
        Store.saveAccounts(this, list)
        val intent = Intent(this, RejoinService::class.java)
        if (Build.VERSION.SDK_INT >= 26) startForegroundService(intent) else startService(intent)
        RejoinService.isRunning = true
        refreshStatus()
    }

    private fun refreshStatus() {
        startBtn.text = if (RejoinService.isRunning) "Stop" else "Start"
        Thread {
            val r = Root.available()
            val overlay = Build.VERSION.SDK_INT < 23 || Settings.canDrawOverlays(this)
            val battery = (getSystemService(POWER_SERVICE) as PowerManager).isIgnoringBatteryOptimizations(packageName)
            runOnUiThread {
                status.text = (if (RejoinService.isRunning) "● Running" else "○ Stopped") +
                    "   ·   root: " + (if (r) "yes" else "no") +
                    "   ·   overlay: " + (if (overlay) "ok" else "off") +
                    "   ·   battery: " + (if (battery) "ok" else "optimized")
            }
        }.start()
    }

    // ---------------- small UI helpers ----------------
    private fun dp(v: Int) = (v * resources.displayMetrics.density).toInt()

    private fun rounded(color: Int, stroke: Int? = null) = GradientDrawable().apply {
        cornerRadius = dp(14).toFloat(); setColor(color); if (stroke != null) setStroke(dp(1), stroke)
    }

    private fun card() = LinearLayout(this).apply {
        orientation = LinearLayout.VERTICAL
        background = rounded(card, line)
        setPadding(dp(16), dp(14), dp(16), dp(16))
        layoutParams = LinearLayout.LayoutParams(-1, -2).apply { topMargin = dp(14) }
    }

    private fun label(t: String) = TextView(this).apply { text = t; textSize = 16f; setTextColor(Color.WHITE); typeface = Typeface.DEFAULT_BOLD }

    private fun hint(t: String) = TextView(this).apply { text = t; textSize = 12.5f; setTextColor(text2); setPadding(0, dp(3), 0, 0) }

    private fun field(value: String, placeholder: String) = EditText(this).apply {
        setText(value); hint = placeholder; setHintTextColor(Color.parseColor("#5A5A5A")); setTextColor(Color.WHITE); textSize = 14f
        inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_URI; isSingleLine = true
        background = rounded(Color.parseColor("#0F0F0F"), line); setPadding(dp(12), dp(10), dp(12), dp(10))
        layoutParams = LinearLayout.LayoutParams(-1, -2).apply { topMargin = dp(8) }
    }

    private fun button(t: String, primary: Boolean, click: () -> Unit) = Button(this).apply {
        text = t; isAllCaps = false; textSize = 15f
        setTextColor(if (primary) Color.BLACK else Color.WHITE)
        background = rounded(if (primary) Color.WHITE else Color.parseColor("#1F1F1F"), if (primary) null else line)
        gravity = Gravity.CENTER
        layoutParams = LinearLayout.LayoutParams(-1, dp(if (primary) 52 else 46)).apply { topMargin = dp(if (primary) 18 else 10) }
        setOnClickListener { click() }
    }

    private fun toast(t: String) = Toast.makeText(this, t, Toast.LENGTH_LONG).show()

    @Suppress("unused")
    private fun hide(v: View) { v.visibility = View.GONE }
}
