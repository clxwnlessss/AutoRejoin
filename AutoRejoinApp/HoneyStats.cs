using System;
using System.Globalization;
using System.IO;

namespace AutoRejoinApp;

// Honey per account for the farm analytics. A tiny read-only script in Potassium's autoexec writes the player's
// current honey to the workspace every 30 s (autorejoin_stats_<UserId>.txt: "<honey>|<unix time>"); the app reads it
// and sends it with the account list. The script only reads the player's own stats and only in Bee Swarm Simulator.
public static class HoneyStats
{
	private const string ScriptName = "autorejoin_stats.lua";

	private const string Script = "-- AutoRejoin: honey stats for the farm analytics (read-only). Remove: AutoRejoin -> Settings -> Discord / Telegram.\n" +
		"if game.PlaceId ~= 1537690962 then return end\n" +
		"if not game:IsLoaded() then game.Loaded:Wait() end\n" +
		"local player = game:GetService(\"Players\").LocalPlayer\n" +
		"local function honey()\n" +
		"  local stats = player:FindFirstChild(\"CoreStats\")\n" +
		"  local h = stats and stats:FindFirstChild(\"Honey\")\n" +
		"  if h then return h.Value end\n" +
		"  local board = player:FindFirstChild(\"leaderstats\")\n" +
		"  local v = board and board:FindFirstChild(\"Honey\")\n" +
		"  return v and v.Value or -1\n" +
		"end\n" +
		"while task.wait(30) do\n" +
		"  pcall(function() writefile(\"autorejoin_stats_\" .. player.UserId .. \".txt\", tostring(honey()) .. \"|\" .. os.time()) end)\n" +
		"end\n";

	// The chosen executor's folder (autoexec + workspace): Potassium, Real, or the Volt / Synapse Z folder.
	public static volatile string Executor = "potassium";

	public static volatile string ExternalDir;

	private static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

	private static string PotassiumDir => ExecDir;

	internal static string ExecDir => Executor == "potassium" ? Path.Combine(Local, "Potassium") : (Executor == "real" ? Path.Combine(Local, "Real") : (ExternalDir ?? Path.Combine(Local, "AutoRejoin-none")));

	private static string OtherDir => Executor == "potassium" ? Path.Combine(Local, "Real") : Path.Combine(Local, "Potassium");

	// Puts the script into the chosen executor's autoexec (or removes it); our copy in the other executor is removed.
	// Only touches our own file.
	public static void Apply(bool enabled)
	{
		try
		{
			string other = Path.Combine(OtherDir, "autoexec", ScriptName);
			if (File.Exists(other) && File.ReadAllText(other).StartsWith("-- AutoRejoin:", StringComparison.Ordinal))
			{
				File.Delete(other);
			}
			if (!Directory.Exists(PotassiumDir))
			{
				return;
			}
			string dir = Path.Combine(PotassiumDir, "autoexec");
			string path = Path.Combine(dir, ScriptName);
			if (enabled)
			{
				Directory.CreateDirectory(dir);
				if (!File.Exists(path) || File.ReadAllText(path) != Script)
				{
					File.WriteAllText(path, Script);
				}
			}
			else if (File.Exists(path) && File.ReadAllText(path).StartsWith("-- AutoRejoin:", StringComparison.Ordinal))
			{
				File.Delete(path);
			}
		}
		catch (Exception ex)
		{
			App.LogError(L.T("скрипт статистики мёда"), ex);
		}
	}

	// Current honey of a Roblox user, if the script reported it in the last 3 minutes.
	public static long? Read(string userId)
	{
		if (string.IsNullOrEmpty(userId))
		{
			return null;
		}
		try
		{
			string path = Path.Combine(PotassiumDir, "workspace", "autorejoin_stats_" + userId + ".txt");
			if (!File.Exists(path) || DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > TimeSpan.FromMinutes(3.0))
			{
				return null;
			}
			string[] parts = File.ReadAllText(path).Trim().Split('|');
			if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var honey) && honey >= 0.0)
			{
				return (long)honey;
			}
		}
		catch
		{
		}
		return null;
	}
}
