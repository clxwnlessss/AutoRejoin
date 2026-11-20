using System;
using System.Diagnostics;
using System.Windows;

namespace AutoRejoinApp;

// Владелец и разработчик AutoRejoin — clxwnlesss. Связь — Discord.
public static class Owner
{
	public const string Name = "clxwnlesss";

	public const string DiscordId = "1505693601932382241";

	public const string DiscordUrl = "https://discord.com/users/" + DiscordId;

	public const string Credit = "AutoRejoin by " + Name;

	// Копирует ник в буфер (на случай, если Discord не откроется) и открывает профиль в Discord.
	public static void OpenDiscord()
	{
		try
		{
			Clipboard.SetText(Name);
		}
		catch
		{
		}
		try
		{
			Process.Start(new ProcessStartInfo(DiscordUrl)
			{
				UseShellExecute = true
			});
		}
		catch (Exception error)
		{
			App.LogError(L.T("не открылась ссылка на Discord"), error);
		}
	}
}
