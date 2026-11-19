using System;
using System.IO;

namespace AutoRejoinApp;

public static class AppPaths
{
	public static string AppDir;

	public static string DataDir;

	public static string ConfigFile => Path.Combine(DataDir, "config.json");

	public static string VaultFile => Path.Combine(DataDir, "vault.dat");

	public static string LogFile => Path.Combine(DataDir, "rejoin.log");

	public static string PidFile => Path.Combine(DataDir, "engine.pid");

	public static string ErrorLog => Path.Combine(DataDir, "app-error.log");

	public static string AvatarDir => Path.Combine(DataDir, "avatars");

	public static string BrowserDir => Path.Combine(DataDir, "browser");

	public static string EngineScript => Path.Combine(AppDir, "engine.ps1");

	public static void Init(string dataOverride)
	{
		AppDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
		DataDir = (string.IsNullOrEmpty(dataOverride) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoRejoin") : Path.GetFullPath(dataOverride));
		Directory.CreateDirectory(DataDir);
		Directory.CreateDirectory(AvatarDir);
	}

	public static string BrowserProfile(string accountId)
	{
		return Path.Combine(BrowserDir, accountId);
	}
}
