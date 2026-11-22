using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace AutoRejoinApp;

// Which Roblox build is on this PC ("version-2c6fd5e09aaf4cfa"): the running window, the folder Froststrap / Bloxstrap /
// Fishstrap point to (registry PlayerPath — the engine launches from there), or the newest Versions folder.
public static class RobloxInstall
{
	private static string cached;

	private static DateTime cachedAt = DateTime.MinValue;

	public static string Version
	{
		get
		{
			if (DateTime.UtcNow - cachedAt > TimeSpan.FromMinutes(1.0))
			{
				cached = Detect();
				cachedAt = DateTime.UtcNow;
			}
			return cached;
		}
	}

	private static string Detect()
	{
		// 1. A running Roblox window — that is the build that actually plays.
		foreach (Process p in Process.GetProcessesByName("RobloxPlayerBeta"))
		{
			try
			{
				string dir = Path.GetFileName(Path.GetDirectoryName(p.MainModule.FileName));
				if (dir.StartsWith("version-", StringComparison.OrdinalIgnoreCase))
				{
					return dir;
				}
			}
			catch
			{
			}
			finally
			{
				p.Dispose();
			}
		}
		// 2. The folder the bootstrapper launches from.
		foreach (string key in new string[3] { "Software\\Froststrap", "Software\\Bloxstrap", "Software\\Fishstrap" })
		{
			try
			{
				using RegistryKey k = Registry.CurrentUser.OpenSubKey(key);
				string path = k?.GetValue("PlayerPath") as string;
				if (!string.IsNullOrEmpty(path) && File.Exists(Path.Combine(path, "RobloxPlayerBeta.exe")))
				{
					return Path.GetFileName(path.TrimEnd('\\'));
				}
			}
			catch
			{
			}
		}
		// 3. The newest installed client.
		string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		string[] roots = new string[6]
		{
			Path.Combine(roaming, "Froststrap", "Versions"),
			Path.Combine(local, "Froststrap", "Versions"),
			Path.Combine(local, "Bloxstrap", "Versions"),
			Path.Combine(local, "Fishstrap", "Versions"),
			Path.Combine(local, "Voidstrap", "Versions"),
			Path.Combine(local, "Roblox", "Versions")
		};
		return roots.Where(Directory.Exists).SelectMany((string r) => new DirectoryInfo(r).GetDirectories("version-*"))
			.Where((DirectoryInfo d) => File.Exists(Path.Combine(d.FullName, "RobloxPlayerBeta.exe")))
			.OrderByDescending((DirectoryInfo d) => File.GetLastWriteTimeUtc(Path.Combine(d.FullName, "RobloxPlayerBeta.exe")))
			.Select((DirectoryInfo d) => d.Name)
			.FirstOrDefault();
	}

	public static string Short(string version)
	{
		if (string.IsNullOrEmpty(version))
		{
			return "?";
		}
		string v = version.StartsWith("version-", StringComparison.OrdinalIgnoreCase) ? version.Substring(8) : version;
		return v.Length > 8 ? v.Substring(0, 8) : v;
	}
}
