package com.autorejoin.mobile

import android.content.Context
import org.json.JSONObject
import java.io.ByteArrayOutputStream
import java.io.File
import java.net.HttpURLConnection
import java.net.URL

/**
 * Mobile executors (Delta, Arceus X, Codex, Fluxus…): finds their autoexec folders and puts our small Lua script there.
 * In every Roblox window the script writes a heartbeat (autorejoin_hb_<userId>.txt) every 10 s and, when Roblox shows
 * the "Disconnected" prompt, autorejoin_dc_<userId>.txt with the code and message. No heartbeat for a while = the game
 * froze; a fresh dc file = disconnected. Needs root (the folders are read and written as root).
 */
object Executors {
    const val SCRIPT_NAME = "autorejoin_watch.lua"

    private val SCRIPT = """
-- AutoRejoin Mobile: heartbeat and disconnect watcher. Installed and updated by the AutoRejoin app.
-- Every 10 s it writes autorejoin_hb_<UserId>.txt to the executor workspace; when Roblox shows the "Disconnected"
-- prompt it writes autorejoin_dc_<UserId>.txt, so AutoRejoin rejoins right away. It only reads the game.
task.spawn(function()
	pcall(function()
		local Players = game:GetService("Players")
		local player = Players.LocalPlayer
		while not player do task.wait(0.5); player = Players.LocalPlayer end
		local uid = tostring(player.UserId)
		local GuiService = game:GetService("GuiService")
		local sent = false
		local function report(title)
			if sent then return end
			local code = 0
			pcall(function() code = GuiService:GetErrorCode().Value end)
			local message = ""
			pcall(function() message = tostring(GuiService:GetErrorMessage()) end)
			local byTitle = title ~= nil and string.find(string.lower(tostring(title)), "disconnect", 1, true) ~= nil
			if not ((code >= 256 and code < 512) or byTitle) then return end
			sent = true
			pcall(writefile, "autorejoin_dc_" .. uid .. ".txt", tostring(os.time()) .. " " .. tostring(code) .. " " .. (string.gsub(message, "[\r\n]+", " ")))
		end
		GuiService.ErrorMessageChanged:Connect(function() task.wait(0.2); report(nil) end)
		task.spawn(function()
			local overlay = game:GetService("CoreGui"):WaitForChild("RobloxPromptGui", 60):WaitForChild("promptOverlay", 60)
			overlay.ChildAdded:Connect(function(child)
				if child.Name ~= "ErrorPrompt" then return end
				task.wait(0.3)
				local title = nil
				pcall(function() title = child.TitleFrame.ErrorTitle.Text end)
				report(title)
			end)
		end)
		while true do
			pcall(writefile, "autorejoin_hb_" .. uid .. ".txt", tostring(os.time()) .. " " .. tostring(game.PlaceId))
			task.wait(10)
		end
	end)
end)
""".trimStart()

    /** Autoexec folders of the executors on this device (by name, under internal storage). */
    fun autoexecDirs(): List<String> {
        val out = Root.run(
            "for d in /storage/emulated/0/*/ /storage/emulated/0/*/*/ /storage/emulated/0/Android/data/*/files/*/; do " +
                "for a in Autoexecute Autoexec autoexecute autoexec Autoexecutor autoexecutor AutoExec AutoExecute; do " +
                "[ -d \"\$d\$a\" ] && echo \"\$d\$a\"; done; done 2>/dev/null"
        )
        return out.lines().map { it.trim().replace("//", "/") }.filter { it.isNotEmpty() }.distinct()
    }

    /** The workspace next to an autoexec folder (Workspace / workspace), created if missing. */
    fun workspaceOf(autoexec: String): String {
        val parent = autoexec.substringBeforeLast('/')
        for (name in listOf("Workspace", "workspace")) {
            if (Root.run("[ -d '$parent/$name' ] && echo y").trim() == "y") return "$parent/$name"
        }
        Root.run("mkdir -p '$parent/Workspace'")
        return "$parent/Workspace"
    }

    fun executorName(autoexec: String): String = autoexec.substringBeforeLast('/').substringAfterLast('/')

    /** Puts (or refreshes) our script into every found autoexec folder. Returns the folders. */
    fun install(ctx: Context): List<String> {
        val dirs = autoexecDirs()
        if (dirs.isEmpty()) return dirs
        val tmp = File(ctx.cacheDir, SCRIPT_NAME)
        tmp.writeText(SCRIPT)
        for (d in dirs) {
            val target = "$d/$SCRIPT_NAME"
            val same = Root.run("cat '$target' 2>/dev/null") == SCRIPT
            if (!same) Root.run("cp '${tmp.absolutePath}' '$target' && chmod 666 '$target'")
        }
        return dirs
    }

    /** Last heartbeat of a user (ms) in any of the workspaces, or 0. */
    fun heartbeat(workspaces: List<String>, userId: Long): Long = newest(workspaces, "autorejoin_hb_$userId.txt")

    /** (time ms, "code message") of the last Disconnected prompt, or null. */
    fun disconnect(workspaces: List<String>, userId: Long): Pair<Long, String>? {
        var best: Pair<Long, String>? = null
        for (w in workspaces) {
            val path = "$w/autorejoin_dc_$userId.txt"
            val t = (Root.run("stat -c %Y '$path' 2>/dev/null").trim().toLongOrNull() ?: continue) * 1000
            if (best == null || t > best.first) {
                val parts = Root.run("cat '$path'").trim().split(" ", limit = 3)
                val code = parts.getOrNull(1) ?: "0"
                best = t to (code + (parts.getOrNull(2)?.let { " — $it" } ?: ""))
            }
        }
        return best
    }

    private fun newest(workspaces: List<String>, file: String): Long {
        var best = 0L
        for (w in workspaces) {
            val t = (Root.run("stat -c %Y '$w/$file' 2>/dev/null").trim().toLongOrNull() ?: continue) * 1000
            if (t > best) best = t
        }
        return best
    }
}

/** Discord webhook: text messages and messages with a screenshot. */
object Discord {
    fun send(hook: String, text: String, image: ByteArray? = null) {
        if (hook.isBlank()) return
        val payload = JSONObject().put("content", text).put("username", "AutoRejoin Mobile").toString()
        if (image == null) { Roblox.post(hook, payload); return }
        val boundary = "----autorejoin" + System.currentTimeMillis()
        val body = ByteArrayOutputStream()
        fun part(s: String) = body.write(s.toByteArray())
        part("--$boundary\r\nContent-Disposition: form-data; name=\"payload_json\"\r\nContent-Type: application/json\r\n\r\n$payload\r\n")
        part("--$boundary\r\nContent-Disposition: form-data; name=\"files[0]\"; filename=\"screen.png\"\r\nContent-Type: image/png\r\n\r\n")
        body.write(image)
        part("\r\n--$boundary--\r\n")
        val c = URL(hook).openConnection() as HttpURLConnection
        c.requestMethod = "POST"; c.doOutput = true; c.connectTimeout = 15000; c.readTimeout = 30000
        c.setRequestProperty("Content-Type", "multipart/form-data; boundary=$boundary")
        c.setRequestProperty("User-Agent", "AutoRejoin-Mobile")
        c.outputStream.use { it.write(body.toByteArray()) }
        val code = c.responseCode
        if (code !in 200..299) throw RuntimeException("HTTP $code from Discord")
    }

    /** Screenshot of the screen as PNG (root), or null. */
    fun screenshot(): ByteArray? {
        Root.run("screencap -p /data/local/tmp/autorejoin_screen.png")
        val bytes = Root.bytes("cat /data/local/tmp/autorejoin_screen.png; rm -f /data/local/tmp/autorejoin_screen.png")
        return if (bytes.size > 1000) bytes else null
    }
}
