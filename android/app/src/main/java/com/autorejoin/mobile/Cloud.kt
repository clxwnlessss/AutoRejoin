package com.autorejoin.mobile

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.os.Build
import android.util.Base64
import org.json.JSONArray
import org.json.JSONObject
import java.io.ByteArrayOutputStream
import java.net.HttpURLConnection
import java.net.URL
import java.util.UUID

/**
 * The AutoRejoin account (same as on PC and the site: Top AFK) and the link to the Telegram / Discord bots.
 * Signed in, the running service reports this phone's accounts every ~10 s (as a "phone" device) and takes the bots'
 * commands: start / stop, rejoin one or all, screenshot. Drops and returns go to Telegram.
 */
object Cloud {
    const val BASE = "https://autorejoin-server.onrender.com"
    const val SITE = "https://clxwnlessss.github.io/AutoRejoin/"

    class ApiError(message: String, val code: Int) : Exception(message)

    fun token(ctx: Context) = Store.get(ctx, "cloudToken")
    fun nickname(ctx: Context) = Store.get(ctx, "cloudNick")
    fun signedIn(ctx: Context) = token(ctx).isNotBlank()

    /** Stable id of this phone for the server (one device in the bots). */
    fun deviceId(ctx: Context): String {
        var id = Store.get(ctx, "deviceId")
        if (id.isBlank()) { id = "ph-" + UUID.randomUUID().toString().replace("-", "").take(12); Store.put(ctx, "deviceId", id) }
        return id
    }

    fun deviceName(ctx: Context): String = Store.get(ctx, "deviceName").ifBlank {
        val model = Build.MODEL ?: "Phone"
        val maker = Build.MANUFACTURER ?: ""
        if (model.startsWith(maker, true)) model else "$maker $model".trim()
    }.take(40)

    /** Account id the bots use: unique per phone, so two phones with the same clone package don't collide. */
    fun accountId(ctx: Context, pkg: String) = deviceId(ctx).takeLast(6) + "/" + pkg

    // ------------------------------------------------------------------ HTTP
    fun call(method: String, path: String, body: JSONObject?, token: String?, timeout: Int = 20000): JSONObject {
        val c = URL(BASE + path).openConnection() as HttpURLConnection
        c.requestMethod = method
        c.connectTimeout = timeout; c.readTimeout = timeout
        c.setRequestProperty("User-Agent", "AutoRejoin-Mobile")
        c.setRequestProperty("Content-Type", "application/json")
        if (!token.isNullOrBlank()) c.setRequestProperty("Authorization", "Bearer $token")
        if (body != null) { c.doOutput = true; c.outputStream.use { it.write(body.toString().toByteArray()) } }
        val code = c.responseCode
        val text = (if (code in 200..299) c.inputStream else c.errorStream)?.bufferedReader()?.readText() ?: ""
        val json = try { JSONObject(text) } catch (e: Exception) { JSONObject() }
        if (code !in 200..299) throw ApiError(json.optString("error").ifBlank { "Server error $code" }, code)
        return json
    }

    private fun saveSession(ctx: Context, r: JSONObject) {
        Store.put(ctx, "cloudToken", r.getString("token"))
        Store.put(ctx, "cloudNick", r.optString("nickname"))
    }

    fun login(ctx: Context, nick: String, password: String) =
        saveSession(ctx, call("POST", "/api/login", JSONObject().put("nickname", nick).put("password", password), null))

    /** Step 1 of sign-up: the server mails a 6-digit code. Returns the email it was sent to. */
    fun registerStart(nick: String, email: String, password: String): String =
        call("POST", "/api/register/start", JSONObject().put("nickname", nick).put("email", email).put("password", password), null)
            .optString("email", email)

    fun registerConfirm(ctx: Context, email: String, code: String) =
        saveSession(ctx, call("POST", "/api/register/confirm", JSONObject().put("email", email).put("code", code), null))

    fun logout(ctx: Context) {
        val t = token(ctx)
        Store.put(ctx, "cloudToken", ""); Store.put(ctx, "cloudNick", "")
        if (t.isNotBlank()) Thread { try { call("POST", "/api/logout", JSONObject(), t) } catch (_: Exception) {} }.start()
    }

    class LinkCode(val code: String, val telegram: String?, val discord: String?)
    fun linkCode(ctx: Context): LinkCode {
        val r = call("POST", "/api/discord/code", JSONObject(), token(ctx))
        return LinkCode(r.getString("code"), r.optString("telegram").ifBlank { null }, r.optString("invite").ifBlank { null })
    }

    class LinkState(val discord: Boolean, val discordName: String?, val telegram: Boolean, val telegramName: String?)
    fun linkState(ctx: Context): LinkState {
        val r = call("GET", "/api/discord/link", null, token(ctx))
        val tg = r.optJSONObject("telegram")
        return LinkState(r.optBoolean("linked"), r.optString("name").ifBlank { null }, tg?.optBoolean("linked") == true, tg?.optString("name")?.ifBlank { null })
    }

    // ------------------------------------------------------------------ from the service
    class Acc(val id: String, val name: String, val main: Boolean, val state: String, val text: String, val uptime: String)
    class Command(val cmd: String, val arg: String)

    /** Report and take the queued commands. Null when not signed in; empty when no bot is linked. */
    fun sync(ctx: Context, running: Boolean, accounts: List<Acc>): List<Command>? {
        val t = token(ctx).ifBlank { return null }
        val arr = JSONArray()
        accounts.forEach {
            arr.put(JSONObject().put("id", it.id).put("name", it.name).put("main", it.main).put("enabled", true)
                .put("state", it.state).put("text", it.text).put("uptime", it.uptime))
        }
        val body = JSONObject()
            .put("running", running).put("phase", if (running) "running" else "stopped")
            .put("version", "m" + Updater.currentVersion(ctx)).put("lang", "en")
            .put("pc", JSONObject().put("id", deviceId(ctx)).put("name", deviceName(ctx)).put("kind", "phone"))
            .put("accounts", arr)
        val r = try { call("POST", "/api/agent/sync", body, t, 15000) } catch (e: ApiError) {
            if (e.code == 401) Store.put(ctx, "cloudToken", "")   // session expired: sign in again in the app
            throw e
        }
        val cmds = r.optJSONArray("commands") ?: JSONArray()
        return List(cmds.length()) { cmds.getJSONObject(it).let { c -> Command(c.optString("cmd"), c.optString("arg")) } }
    }

    fun event(ctx: Context, kind: String, text: String, account: String?, accountId: String?) {
        val t = token(ctx).ifBlank { return }
        try {
            call("POST", "/api/agent/event", JSONObject().put("kind", kind).put("text", text).put("account", account ?: "")
                .put("accountId", accountId ?: "").put("pcName", deviceName(ctx)), t, 15000)
        } catch (_: Exception) {}
    }

    /** Screenshot asked from a bot: JPEG (smaller than PNG, fits the upload limit) or the reason it failed. */
    fun sendScreen(ctx: Context, requestId: String, png: ByteArray?, error: String?) {
        val t = token(ctx).ifBlank { return }
        val body = JSONObject().put("id", requestId)
        val jpeg = png?.let { toJpeg(it) }
        if (jpeg != null) body.put("image", Base64.encodeToString(jpeg, Base64.NO_WRAP)) else body.put("error", error ?: "no picture")
        try { call("POST", "/api/agent/screen", body, t, 30000) } catch (_: Exception) {}
    }

    private fun toJpeg(png: ByteArray): ByteArray? = try {
        var bmp = BitmapFactory.decodeByteArray(png, 0, png.size)
        val side = maxOf(bmp.width, bmp.height)
        if (side > 1280) {
            val k = 1280f / side
            bmp = Bitmap.createScaledBitmap(bmp, (bmp.width * k).toInt(), (bmp.height * k).toInt(), true)
        }
        ByteArrayOutputStream().also { bmp.compress(Bitmap.CompressFormat.JPEG, 72, it) }.toByteArray()
    } catch (e: Exception) { null }
}
