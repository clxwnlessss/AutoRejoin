using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Win32;

namespace AutoRejoinApp;

// Bot agent (Discord + Telegram): while the Top AFK account is linked to a bot, sends this PC's account list to the
// server every 10 s and runs the commands queued from Discord / Telegram / the website (start, stop, rejoin,
// screenshot). Several PCs under one account form one farm: each PC reports with its own id and name.
// Nothing is sent about accounts until a bot is linked.
public class BotAgent
{
	private readonly MainController main;

	private readonly DispatcherTimer timer;

	private bool busy;

	private DateTime nextIdleCheck = DateTime.MinValue;

	private DateTime fastUntil = DateTime.MinValue;

	public bool Linked { get; private set; }

	public string LinkedName { get; private set; }

	public bool TelegramLinked { get; private set; }

	public string TelegramName { get; private set; }

	public string TelegramBot { get; private set; }

	public event Action StateChanged;

	private string Token => main.Vault.leaderboardToken;

	private string BaseUrl => Leaderboard.BaseUrl(main.Config);

	// Stable id of this PC (hash of the Windows machine id) and its name, shown in the farm panel.
	public static readonly string PcId = MakePcId();

	public static string PcName => Environment.MachineName;

	public BotAgent(MainController main, Dispatcher dispatcher)
	{
		this.main = main;
		timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
		{
			Interval = TimeSpan.FromSeconds(10.0)
		};
		timer.Tick += async delegate
		{
			await Tick();
		};
		timer.Start();
	}

	private static string MakePcId()
	{
		string seed = null;
		try
		{
			using RegistryKey key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey("SOFTWARE\\Microsoft\\Cryptography");
			seed = key?.GetValue("MachineGuid") as string;
		}
		catch
		{
		}
		if (string.IsNullOrEmpty(seed))
		{
			seed = Environment.MachineName + "|" + Environment.UserName;
		}
		using SHA256 sha = SHA256.Create();
		return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes("autorejoin-pc:" + seed))).Replace("-", "").Substring(0, 16).ToLowerInvariant();
	}

	// After a code is shown in Settings: check every 10 s for the next 10 minutes so the link shows up quickly.
	public void ExpectLink()
	{
		fastUntil = DateTime.UtcNow.AddMinutes(10.0);
		nextIdleCheck = DateTime.MinValue;
	}

	public void Reset()
	{
		TelegramLinked = false;
		TelegramName = null;
		SetLinked(linked: false, null);
		nextIdleCheck = DateTime.MinValue;
	}

	private void SetLinked(bool linked, string name)
	{
		if (Linked != linked || LinkedName != name)
		{
			Linked = linked;
			LinkedName = name;
			StateChanged?.Invoke();
		}
	}

	// Discord name, Telegram name and the Telegram bot link — for Settings.
	private async Task RefreshLinkInfo()
	{
		try
		{
			IDictionary<string, object> info = await Leaderboard.DiscordLink(BaseUrl, Token);
			string discordName = Store.Flag(info, "linked") ? (Store.Str(info, "name") ?? "") : null;
			IDictionary<string, object> tg = Store.Obj(info, "telegram");
			bool tgLinked = Store.Flag(tg, "linked");
			string tgName = tgLinked ? (Store.Str(tg, "name") ?? "") : null;
			string tgBot = Store.Str(info, "telegramBot");
			bool changed = tgLinked != TelegramLinked || tgName != TelegramName || tgBot != TelegramBot;
			if (tgLinked && !TelegramLinked)
			{
				main.AppLog(null, L.T("Telegram-бот привязан") + (string.IsNullOrEmpty(tgName) ? "" : (": " + tgName)), "Green");
			}
			TelegramLinked = tgLinked;
			TelegramName = tgName;
			TelegramBot = tgBot;
			if (discordName != null && !Linked)
			{
				main.AppLog(null, L.T("Discord-бот привязан") + (string.IsNullOrEmpty(discordName) ? "" : (": " + discordName)), "Green");
			}
			SetLinked(linked: true, discordName ?? "");
			if (changed)
			{
				StateChanged?.Invoke();
			}
		}
		catch
		{
			SetLinked(linked: true, LinkedName ?? "");
		}
	}

	private DateTime nextInfo = DateTime.MinValue;

	private async Task Tick()
	{
		if (busy)
		{
			return;
		}
		if (string.IsNullOrEmpty(Token))
		{
			SetLinked(linked: false, null);
			return;
		}
		DateTime now = DateTime.UtcNow;
		// Not linked: look every minute (every 10 s while a code is waiting) instead of every 10 s.
		if (!Linked && now < nextIdleCheck && now > fastUntil)
		{
			return;
		}
		busy = true;
		try
		{
			IDictionary<string, object> r = await Leaderboard.AgentSync(BaseUrl, Token, Snapshot());
			bool linked = Store.Flag(r, "linked");
			if (linked && (!Linked || now >= nextInfo || now < fastUntil))
			{
				await RefreshLinkInfo();
				nextInfo = now.AddMinutes(5.0);
				if (now >= fastUntil || TelegramLinked)
				{
					fastUntil = DateTime.MinValue;
				}
			}
			else if (!linked)
			{
				TelegramLinked = false;
				SetLinked(linked: false, null);
				nextIdleCheck = now.AddMinutes(1.0);
			}
			foreach (IDictionary<string, object> c in Store.List(r, "commands"))
			{
				main.RemoteCommand(Store.Str(c, "cmd") ?? "", Store.Str(c, "arg") ?? "");
			}
		}
		catch (Leaderboard.ApiException)
		{
			// Server asleep or offline: try again on the next tick.
			nextIdleCheck = now.AddSeconds(30.0);
		}
		catch (Exception ex)
		{
			App.LogError(L.T("Бот: ошибка синхронизации"), ex);
		}
		finally
		{
			busy = false;
		}
	}

	private Dictionary<string, object> Snapshot()
	{
		Dictionary<string, object> state = new Dictionary<string, object>
		{
			["running"] = main.EngineRunning,
			["phase"] = main.EnginePhase ?? "",
			["version"] = Updater.CurrentText,
			["lang"] = L.IsEnglish ? "en" : "ru",
			["pc"] = new Dictionary<string, object>
			{
				["id"] = PcId,
				["name"] = PcName
			}
		};
		// Account names leave the PC only after the user linked a bot.
		bool honey = main.Config.honeyStats;
		state["accounts"] = Linked ? main.Cards.Select(delegate(AccountCard c)
		{
			Dictionary<string, object> a = new Dictionary<string, object>
			{
				["id"] = c.Key,
				["name"] = c.Name ?? "",
				["main"] = c.IsMain,
				["enabled"] = c.Enabled,
				["state"] = c.State ?? "",
				["text"] = c.StateText ?? "",
				["uptime"] = c.Uptime ?? ""
			};
			long? h = honey ? HoneyStats.Read(c.UserId) : null;
			if (h.HasValue)
			{
				a["honey"] = h.Value;
			}
			return a;
		}).Cast<object>().ToList() : new List<object>();
		return state;
	}

	// Drop / back / error / stuck / alert → server (Telegram notifications, analytics). Fire and forget.
	public void SendEvent(string kind, string text, string accountKey, string accountName, byte[] image)
	{
		if (!Linked || string.IsNullOrEmpty(Token))
		{
			return;
		}
		Dictionary<string, object> body = new Dictionary<string, object>
		{
			["kind"] = kind,
			["text"] = text,
			["account"] = accountName ?? "",
			["accountId"] = accountKey ?? "",
			["pcName"] = PcName
		};
		if (image != null)
		{
			body["image"] = Convert.ToBase64String(image);
		}
		string baseUrl = BaseUrl, token = Token;
		Task.Run(async delegate
		{
			try
			{
				await Leaderboard.AgentEvent(baseUrl, token, body);
			}
			catch
			{
			}
		});
	}

	public async Task UploadScreen(string id, byte[] jpeg, string error)
	{
		try
		{
			await Leaderboard.AgentScreen(BaseUrl, Token, id, jpeg, error);
		}
		catch (Exception ex)
		{
			App.LogError(L.T("скриншот не отправился"), ex);
		}
	}

	public Task<IDictionary<string, object>> DailyReport()
	{
		return Leaderboard.AgentReport(BaseUrl, Token);
	}
}
