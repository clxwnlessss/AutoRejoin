using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using Microsoft.Win32;

namespace AutoRejoinApp;

public static class SystemTools
{
	private const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

	private const string RunName = "AutoRejoin";

	private static string ExePath => Process.GetCurrentProcess().MainModule.FileName;

	public static bool IsAutostartOn()
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
			string text = ((registryKey == null) ? null : (registryKey.GetValue("AutoRejoin") as string));
			return text != null && text.IndexOf(ExePath, StringComparison.OrdinalIgnoreCase) >= 0;
		}
		catch
		{
			return false;
		}
	}

	public static void SetAutostart(bool on)
	{
		using RegistryKey registryKey = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
		if (on)
		{
			registryKey.SetValue("AutoRejoin", "\"" + ExePath + "\" --autostart");
		}
		else if (registryKey.GetValue("AutoRejoin") != null)
		{
			registryKey.DeleteValue("AutoRejoin", throwOnMissingValue: false);
		}
	}

	public static void RepairAutostart()
	{
		try
		{
			using (RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"))
			{
				string text = ((registryKey == null) ? null : (registryKey.GetValue("AutoRejoin") as string));
				if (text == null || text.IndexOf(ExePath, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return;
				}
			}
			SetAutostart(on: true);
		}
		catch
		{
		}
	}

	private static IEnumerable<ManagementBaseObject> Processes(string filter)
	{
		List<ManagementBaseObject> list = new List<ManagementBaseObject>();
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("SELECT ProcessId, Name, CommandLine FROM Win32_Process WHERE " + filter);
			using ManagementObjectCollection managementObjectCollection = managementObjectSearcher.Get();
			foreach (ManagementBaseObject item in managementObjectCollection)
			{
				list.Add(item);
			}
		}
		catch
		{
		}
		return list;
	}

	private static void Kill(int pid)
	{
		try
		{
			using Process process = Process.GetProcessById(pid);
			process.Kill();
		}
		catch
		{
		}
	}

	public static void KillOrphanEngine()
	{
		int result;
		try
		{
			if (!File.Exists(AppPaths.PidFile) || !int.TryParse(File.ReadAllText(AppPaths.PidFile).Trim(), out result))
			{
				return;
			}
		}
		catch
		{
			return;
		}
		foreach (ManagementBaseObject item in Processes("ProcessId=" + result))
		{
			string text = (item["CommandLine"] as string) ?? "";
			if (text.IndexOf("engine.ps1", StringComparison.OrdinalIgnoreCase) >= 0 && text.IndexOf(AppPaths.DataDir, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				Kill(result);
			}
		}
	}

	public static int KillOtherAutoRejoins(int ownEnginePid)
	{
		int num = 0;
		foreach (ManagementBaseObject item in Processes("Name='cmd.exe'"))
		{
			string text = (item["CommandLine"] as string) ?? "";
			if (text.IndexOf("AutoRejoin-autostart.bat", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("AutoRejoin.bat", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				Kill(Convert.ToInt32(item["ProcessId"]));
				num++;
			}
		}
		foreach (ManagementBaseObject item2 in Processes("Name='powershell.exe' OR Name='pwsh.exe'"))
		{
			int num2 = Convert.ToInt32(item2["ProcessId"]);
			if (num2 != ownEnginePid)
			{
				string text2 = (item2["CommandLine"] as string) ?? "";
				if (text2.IndexOf("RobloxAutoRejoin.ps1", StringComparison.OrdinalIgnoreCase) >= 0 || (text2.IndexOf("engine.ps1", StringComparison.OrdinalIgnoreCase) >= 0 && text2.IndexOf("-SetupPagefile", StringComparison.OrdinalIgnoreCase) < 0))
				{
					Kill(num2);
					num++;
				}
			}
		}
		return num;
	}

	public static void OpenFolder(string path)
	{
		try
		{
			Process.Start("explorer.exe", "\"" + path + "\"");
		}
		catch
		{
		}
	}

	public static void OpenFile(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				ProcessStartInfo processStartInfo = new ProcessStartInfo(path);
				processStartInfo.UseShellExecute = true;
				Process.Start(processStartInfo);
			}
			else
			{
				OpenFolder(Path.GetDirectoryName(path));
			}
		}
		catch
		{
		}
	}
}
