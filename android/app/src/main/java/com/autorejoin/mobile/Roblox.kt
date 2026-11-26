package com.autorejoin.mobile

import android.content.Context
import android.content.Intent
import android.net.Uri
import org.json.JSONArray
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL
import java.net.URLDecoder
import java.net.URLEncoder

/** One account = one Roblox app (com.roblox.client or a clone) that is already logged in. */
data class Account(
    var pkg: String,
    var username: String = "",
    var userId: Long = 0,
    var link: String = "",
    var enabled: Boolean = true,
) {
    fun toJson() = JSONObject().put("pkg", pkg).put("username", username).put("userId", userId).put("link", link).put("enabled", enabled)

    companion object {
        fun fromJson(o: JSONObject) = Account(o.optString("pkg"), o.optString("username"), o.optLong("userId"), o.optString("link"), o.optBoolean("enabled", true))
    }
}

object Store {
    private const val PREFS = "autorejoin"

    fun accounts(ctx: Context): MutableList<Account> {
        val raw = ctx.getSharedPreferences(PREFS, 0).getString("accounts", "[]") ?: "[]"
        val arr = JSONArray(raw)
        return MutableList(arr.length()) { Account.fromJson(arr.getJSONObject(it)) }
    }

    fun saveAccounts(ctx: Context, list: List<Account>) {
        val arr = JSONArray()
        list.forEach { arr.put(it.toJson()) }
        ctx.getSharedPreferences(PREFS, 0).edit().putString("accounts", arr.toString()).apply()
    }

    fun get(ctx: Context, key: String, def: String = "") = ctx.getSharedPreferences(PREFS, 0).getString(key, def) ?: def
    fun put(ctx: Context, key: String, value: String) = ctx.getSharedPreferences(PREFS, 0).edit().putString(key, value).apply()
}

object Roblox {
    const val BEE_SWARM = "1537690962"

    /** Installed Roblox apps: everything that opens roblox:// links. */
    fun installedApps(ctx: Context): List<String> {
        val intent = Intent(Intent.ACTION_VIEW, Uri.parse("roblox://"))
        @Suppress("DEPRECATION")
        val found = ctx.packageManager.queryIntentActivities(intent, 0).map { it.activityInfo.packageName }
        val clones = ctx.packageManager.getInstalledApplications(0).map { it.packageName }.filter { it.contains("roblox", true) }
        return (found + clones).distinct().sorted()
    }

    /** Place ID, game link, VIP link (privateServerLinkCode) or share link -> roblox:// link. */
    fun deepLink(link: String): String {
        val text = link.trim()
        if (Regex("\\d{4,20}").matches(text)) return "roblox://experiences/start?placeId=$text"
        val uri = Uri.parse(text)
        val path = uri.path ?: ""
        fun q(name: String) = try { uri.getQueryParameter(name) } catch (e: Exception) { null }
        val code = q("code")
        if (path.contains("/share") && code != null) {
            return "roblox://navigation/share_links?code=${enc(code)}&type=${q("type") ?: "Server"}"
        }
        val place = Regex("/games/(\\d+)").find(path)?.groupValues?.get(1) ?: q("placeId")
            ?: throw IllegalArgumentException("Don't understand the link: $text")
        val vip = q("privateServerLinkCode") ?: q("linkCode")
        return "roblox://experiences/start?placeId=$place" + (if (vip != null) "&linkCode=${enc(vip)}" else "")
    }

    /** Link for the log: private server codes are cut. */
    fun hide(link: String) = Regex("((?:code|linkCode|privateServerLinkCode)=)([^&\\s]+)").replace(link) { m ->
        m.groupValues[1] + "…" + m.groupValues[2].takeLast(4)
    }

    private fun enc(s: String) = URLEncoder.encode(URLDecoder.decode(s, "UTF-8"), "UTF-8")

    fun userId(username: String): Long {
        val body = JSONObject().put("usernames", JSONArray().put(username)).put("excludeBannedUsers", false)
        val res = post("https://users.roblox.com/v1/usernames/users", body.toString())
        val data = JSONObject(res).optJSONArray("data")
        if (data == null || data.length() == 0) throw IllegalArgumentException("Roblox has no user \"$username\"")
        return data.getJSONObject(0).getLong("id")
    }

    /** userId -> presence type (0 offline, 1 online, 2 in game, 3 in Studio). Public API, no cookie. */
    fun presence(ids: List<Long>): Map<Long, Int> {
        if (ids.isEmpty()) return emptyMap()
        val body = JSONObject().put("userIds", JSONArray(ids)).toString()
        val arr = JSONObject(post("https://presence.roblox.com/v1/presence/users", body)).optJSONArray("userPresences") ?: JSONArray()
        val out = HashMap<Long, Int>()
        for (i in 0 until arr.length()) {
            val p = arr.getJSONObject(i)
            out[p.getLong("userId")] = p.optInt("userPresenceType", 0)
        }
        return out
    }

    fun post(url: String, json: String): String {
        val c = URL(url).openConnection() as HttpURLConnection
        c.requestMethod = "POST"
        c.connectTimeout = 15000
        c.readTimeout = 15000
        c.doOutput = true
        c.setRequestProperty("Content-Type", "application/json")
        c.setRequestProperty("User-Agent", "AutoRejoin-Mobile")
        c.outputStream.use { it.write(json.toByteArray()) }
        val code = c.responseCode
        val text = (if (code in 200..299) c.inputStream else c.errorStream)?.bufferedReader()?.readText() ?: ""
        if (code !in 200..299) throw RuntimeException("HTTP $code from ${URL(url).host}")
        return text
    }
}

/** Root shell, if the phone has it (UGPhone with root on, rooted emulators). */
object Root {
    @Volatile private var checked: Boolean? = null

    fun available(): Boolean {
        checked?.let { return it }
        val ok = run("id").startsWith("uid=0")
        checked = ok
        return ok
    }

    fun recheck() { checked = null }

    fun run(cmd: String): String = try {
        val p = ProcessBuilder("su", "-c", cmd).redirectErrorStream(true).start()
        val out = p.inputStream.bufferedReader().readText()
        p.waitFor()
        out
    } catch (e: Exception) {
        ""
    }
}
