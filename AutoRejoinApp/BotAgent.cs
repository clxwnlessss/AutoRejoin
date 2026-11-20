using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace AutoRejoinApp;

// Discord bot agent: while the Top AFK account is linked to Discord, sends the account list to the server every 10 s
// and runs the commands queued from Discord (start, stop, rejoin). Nothing is sent about accounts until linked.
public class BotAgent
{
	private readonly MainController main;

	private readonly DispatcherTimer timer;

	private bool busy;

	private DateTime nextIdleCheck = DateTime.MinValue;

	private DateTime fastUntil = DateTime.MinValue;

	public bool Linked { get; private set; }

	public string LinkedName { get; private set; }

	public event Action StateChanged;

	private string Token => main.Vault.leaderboardToken;

	private string BaseUrl => Leaderboard.BaseUrl(main.Config);

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

	// After a code is shown in Settings: check every 10 s for the next 10 minutes so the link shows up quickly.
	public void ExpectLink()
	{
		fastUntil = DateTime.UtcNow.AddMinutes(10.0);
		nextIdleCheck = DateTime.MinValue;
	}

	public void Reset()
	{
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
			if (linked && (!Linked || LinkedName == null))
			{
				string name = null;
				try
				{
					name = Store.Str(await Leaderboard.DiscordLink(BaseUrl, Token), "name");
				}
				catch
				{
				}
				SetLinked(linked: true, name ?? "");
				fastUntil = DateTime.MinValue;
				main.AppLog(null, L.T("Discord-бот привязан") + (string.IsNullOrEmpty(name) ? "" : (": " + name)), "Green");
			}
			else if (!linked)
			{
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
			App.LogError(L.T("Discord-бот: ошибка синхронизации"), ex);
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
			["lang"] = L.IsEnglish ? "en" : "ru"
		};
		// Account names leave the PC only after the user linked the bot.
		state["accounts"] = Linked ? main.Cards.Select((AccountCard c) => new Dictionary<string, object>
		{
			["id"] = c.Key,
			["name"] = c.Name ?? "",
			["main"] = c.IsMain,
			["enabled"] = c.Enabled,
			["state"] = c.State ?? "",
			["text"] = c.StateText ?? "",
			["uptime"] = c.Uptime ?? ""
		}).Cast<object>().ToList() : new List<object>();
		return state;
	}
}
