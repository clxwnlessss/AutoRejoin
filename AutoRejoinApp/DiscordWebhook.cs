using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace AutoRejoinApp;

// Discord через вебхук. Только отправка сообщений, никакого управления из Discord.
//  • Отчёт раз в N минут (по умолчанию 15): какие аккаунты в игре, а какие нет.
//  • Сразу (если включено): аккаунт вылетел — с причиной; снова в игре — сколько его не было.
//  • Сразу, с упоминанием: проблемы, которые сами не чинятся (кука, предупреждение Роблокса, вход с капчей,
//    Потассиум разлогинился, сообщения движка «нужна твоя помощь»).
public class DiscordWebhook
{
	private class Row
	{
		public string Name;
		public bool Main;
		public bool InGame;
		public string Text;
		public DateTime? StartedAt;
	}

	private class Snapshot
	{
		public bool Running;
		public List<Row> Rows = new List<Row>();
	}

	// Что помним об аккаунте между статусами движка — чтобы ловить переходы «в игре → вылетел → снова в игре».
	private class Track
	{
		public bool InGame;
		public bool DropReported;
		public DateTime DownSince;
		public string Problem;
	}

	private class Event
	{
		public string Text;
		public bool Mention;
	}

	// Перезаходы, которые запросил ты или расписание, — не вылет, о них не пишем.
	private static readonly Regex Planned = new Regex("^(перезаход по кнопке|плановый перезаход|перезаход по Balloon|уступил очередь)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	private readonly MainController main;

	private readonly Dispatcher dispatcher;

	private readonly Dictionary<string, Track> tracks = new Dictionary<string, Track>();

	private readonly ConcurrentQueue<Event> events = new ConcurrentQueue<Event>();

	private bool potassiumSignedOut;

	private CancellationTokenSource cts;

	private string runningKey;

	private string url;

	private volatile int intervalMinutes = 15;

	private volatile bool eventsOn = true;

	private volatile string[] mentionIds = new string[0];

	private volatile Snapshot snapshot;

	private volatile bool sendNow;

	public string State { get; private set; } = "выключен";

	public bool Healthy { get; private set; }

	public event Action StateChanged;

	static DiscordWebhook()
	{
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
	}

	public DiscordWebhook(MainController main, Dispatcher dispatcher)
	{
		this.main = main;
		this.dispatcher = dispatcher;
	}

	public static bool IsValidUrl(string text)
	{
		text = (text ?? "").Trim();
		return text.StartsWith("https://discord.com/api/webhooks/", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://discordapp.com/api/webhooks/", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://ptb.discord.com/api/webhooks/", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://canary.discord.com/api/webhooks/", StringComparison.OrdinalIgnoreCase);
	}

	public static IEnumerable<string> ParseIds(string text)
	{
		foreach (string part in (text ?? "").Split(new char[4] { ',', ' ', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
		{
			string id = part.Trim().Trim('<', '>', '@', '!');
			if (id.Length > 0 && id.All(char.IsDigit))
			{
				yield return id;
			}
		}
	}

	// ---------- вызывается из UI-потока ----------

	public void Apply()
	{
		AppConfig cfg = main.Config;
		string newUrl = (main.Vault.discordWebhook ?? "").Trim();
		intervalMinutes = Math.Max(1, Math.Min(1440, cfg.discordInterval));
		eventsOn = cfg.discordEvents;
		mentionIds = ParseIds(cfg.discordMentions).Distinct().ToArray();
		Update();
		string key = cfg.discordOn + "|" + newUrl;
		if (key == runningKey && cts != null)
		{
			return;
		}
		Stop();
		runningKey = key;
		if (!cfg.discordOn)
		{
			SetState("выключен", healthy: false);
			return;
		}
		if (!IsValidUrl(newUrl))
		{
			SetState("не настроен — вставь ссылку вебхука", healthy: false);
			return;
		}
		url = newUrl;
		cts = new CancellationTokenSource();
		CancellationToken ct = cts.Token;
		SetState("ждёт первого отчёта", healthy: false);
		Task.Run(() => Loop(ct));
	}

	public void Stop()
	{
		if (cts != null)
		{
			try
			{
				cts.Cancel();
			}
			catch
			{
			}
			cts = null;
		}
		runningKey = null;
	}

	// Кнопка «Отправить сейчас» в настройках.
	public void SendNow()
	{
		sendNow = true;
	}

	// Сообщение движка «нужна твоя помощь» или окно входа ждёт капчу / код — сразу и с упоминанием.
	public void OnAlert(string text)
	{
		if (!string.IsNullOrWhiteSpace(text))
		{
			Enqueue("⚠️ " + text.Trim(), mention: true);
		}
	}

	// Снимок для отчёта + поиск переходов для мгновенных сообщений. Вызывается на каждый статус движка (раз в секунду).
	public void Update()
	{
		bool running = main.EngineRunning;
		string phase = main.EnginePhase;
		Snapshot snap = new Snapshot { Running = running };
		IDictionary<string, object> status = running ? main.LastStatus : null;
		Dictionary<string, IDictionary<string, object>> byKey = new Dictionary<string, IDictionary<string, object>>();
		foreach (IDictionary<string, object> item in Store.List(status, "accounts"))
		{
			string k = Store.Str(item, "key");
			if (k != null)
			{
				byKey[k] = item;
			}
		}
		// Следим за переходами только пока движок работает: запуск, остановка и «Остановить» (закрывает окна) — не вылеты.
		bool watch = running && phase == "running" && status != null;
		if (!watch)
		{
			tracks.Clear();
			potassiumSignedOut = false;
		}
		DateTime now = DateTime.UtcNow;
		int n = 0;
		foreach (VaultAccount account in main.Vault.accounts)
		{
			n++;
			if (!account.enabled)
			{
				continue;
			}
			Row row = new Row
			{
				Name = DisplayName(account, n),
				Main = account.main,
				Text = (running ? "ждёт запуска" : "AutoRejoin остановлен")
			};
			string state = null;
			string lastReason = null;
			DateTime? lastReasonAt = null;
			if (byKey.TryGetValue(account.id, out var view))
			{
				state = Store.Str(view, "state");
				row.InGame = state == "ingame" || state == "script";
				row.Text = Store.Str(view, "text") ?? row.Text;
				row.StartedAt = Store.Time(view, "startedAt");
				lastReason = Store.Str(view, "lastReason");
				lastReasonAt = Store.Time(view, "lastReasonAt");
				string engineName = Store.Str(view, "name");
				if (string.IsNullOrEmpty(account.name) && !string.IsNullOrEmpty(engineName))
				{
					row.Name = engineName;
				}
			}
			snap.Rows.Add(row);
			if (watch && view != null)
			{
				Watch(account.id, row, state, lastReason, lastReasonAt, now);
			}
		}
		if (watch)
		{
			IDictionary<string, object> pot = Store.Obj(status, "potassium");
			bool signedOut = Store.Flag(pot, "enabled") && Store.Flag(pot, "signedOut");
			if (signedOut && !potassiumSignedOut && !main.Config.potassiumAutoLogin)
			{
				Enqueue("⚠️ Потассиум разлогинился — скрипт в новые окна не подключится. Войди в Потассиум (или включи «Автовход» в настройках AutoRejoin).", mention: true);
			}
			potassiumSignedOut = signedOut;
		}
		snapshot = snap;
	}

	private void Watch(string key, Row row, string state, string lastReason, DateTime? lastReasonAt, DateTime now)
	{
		bool problem = state == "problem" || state == "deferred" || state == "blocked";
		if (!tracks.TryGetValue(key, out var track))
		{
			// Первый статус после запуска: просто запоминаем, иначе каждый аккаунт, который ещё грузится, «вылетел» бы.
			tracks[key] = new Track
			{
				InGame = row.InGame,
				Problem = (problem ? row.Text : null)
			};
			if (problem)
			{
				Enqueue("⚠️ **" + Escape(row.Name) + "**: " + row.Text, mention: true);
			}
			return;
		}
		if (track.InGame && !row.InGame)
		{
			// Причина — та, что движок записал при перезапуске (свежая), иначе текущее состояние окна.
			string reason = (lastReasonAt.HasValue && (now - lastReasonAt.Value).TotalSeconds < 90.0 && !string.IsNullOrEmpty(lastReason)) ? lastReason : row.Text;
			track.DownSince = now;
			track.DropReported = !Planned.IsMatch(reason ?? "");
			if (track.DropReported)
			{
				Enqueue("🔴 **" + Escape(row.Name) + "**" + (row.Main ? " ⭐" : "") + " вылетел: " + reason, mention: false);
			}
		}
		else if (!track.InGame && row.InGame)
		{
			if (track.DropReported)
			{
				Enqueue("🟢 **" + Escape(row.Name) + "**" + (row.Main ? " ⭐" : "") + " снова в игре (не было " + Ago(track.DownSince) + ")", mention: false);
			}
			track.DropReported = false;
		}
		track.InGame = row.InGame;
		if (problem)
		{
			if (track.Problem != row.Text)
			{
				Enqueue("⚠️ **" + Escape(row.Name) + "**: " + row.Text, mention: true);
			}
			track.Problem = row.Text;
		}
		else
		{
			track.Problem = null;
		}
	}

	private void Enqueue(string text, bool mention)
	{
		if (cts == null || !eventsOn)
		{
			return;
		}
		events.Enqueue(new Event
		{
			Text = text,
			Mention = mention
		});
	}

	// ---------- фоновый поток ----------

	private async Task Loop(CancellationToken ct)
	{
		// Первый отчёт — через минуту после включения: аккаунты успеют начать заходить.
		DateTime next = DateTime.UtcNow.AddMinutes(1.0);
		while (!ct.IsCancellationRequested)
		{
			try
			{
				if (!events.IsEmpty)
				{
					// Собираем всё, что случилось за пару секунд, в одно сообщение (например, вылет сразу нескольких окон).
					await Task.Delay(3000, ct);
					await SendEvents(ct);
				}
				if (sendNow || DateTime.UtcNow >= next)
				{
					sendNow = false;
					next = DateTime.UtcNow.AddMinutes(intervalMinutes);
					await SendReport(ct);
				}
				await Task.Delay(500, ct);
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch (Exception ex)
			{
				SetState("ошибка: " + ex.Message, healthy: false);
			}
		}
	}

	private async Task SendEvents(CancellationToken ct)
	{
		List<Event> batch = new List<Event>();
		while (batch.Count < 30 && events.TryDequeue(out var e))
		{
			batch.Add(e);
		}
		if (batch.Count == 0)
		{
			return;
		}
		string[] ids = (batch.Any((Event e) => e.Mention) ? mentionIds : new string[0]);
		string prefix = string.Join(" ", ids.Select((string id) => "<@" + id + ">"));
		StringBuilder text = new StringBuilder((prefix.Length > 0) ? (prefix + "\n") : "");
		foreach (Event e in batch)
		{
			if (text.Length + e.Text.Length > 1900)
			{
				text.Append("…");
				break;
			}
			text.Append(e.Text).Append('\n');
		}
		Dictionary<string, object> body = new Dictionary<string, object>
		{
			["username"] = "AutoRejoin",
			["content"] = text.ToString().TrimEnd(),
			["allowed_mentions"] = new Dictionary<string, object>
			{
				["parse"] = new object[0],
				["users"] = ids
			}
		};
		Report(await Post(body, ct), "сообщение отправлено");
	}

	private async Task SendReport(CancellationToken ct)
	{
		Snapshot snap = snapshot;
		if (snap == null)
		{
			return;
		}
		StringBuilder text = new StringBuilder();
		int inGame = 0;
		foreach (Row row in snap.Rows)
		{
			if (row.InGame)
			{
				inGame++;
			}
			string line = (row.InGame ? "🟢" : "🔴") + " **" + Escape(row.Name) + "**" + (row.Main ? " ⭐" : "") + " — " + (row.InGame ? ("в игре" + (row.StartedAt.HasValue ? (" " + Ago(row.StartedAt.Value)) : "")) : ("не в игре (" + row.Text + ")"));
			if (text.Length + line.Length > 3900)
			{
				text.Append("\n…");
				break;
			}
			text.Append(line).Append('\n');
		}
		if (snap.Rows.Count == 0)
		{
			text.Append("Включённых аккаунтов нет.");
		}
		int total = snap.Rows.Count;
		int color = ((!snap.Running || inGame == 0) ? 0xE5484D : ((inGame < total) ? 0xF5A524 : 0x30A46C));
		Dictionary<string, object> body = new Dictionary<string, object>
		{
			["username"] = "AutoRejoin",
			["allowed_mentions"] = new Dictionary<string, object> { ["parse"] = new object[0] },
			["embeds"] = new object[1]
			{
				new Dictionary<string, object>
				{
					["title"] = (snap.Running ? ("В игре " + inGame + " из " + total) : "AutoRejoin остановлен"),
					["description"] = text.ToString().TrimEnd(),
					["color"] = color,
					["footer"] = new Dictionary<string, object> { ["text"] = Environment.MachineName },
					["timestamp"] = DateTime.UtcNow.ToString("o")
				}
			}
		};
		Report(await Post(body, ct), "последний отчёт в " + DateTime.Now.ToString("HH:mm"));
	}

	private void Report(int code, string okText)
	{
		if (code >= 200 && code < 300)
		{
			SetState("работает — " + okText, healthy: true);
		}
		else if (code == 401 || code == 404)
		{
			SetState("вебхук не найден — его удалили или ссылка неверная", healthy: false);
		}
		else
		{
			SetState("Discord ответил " + code, healthy: false);
		}
	}

	private async Task<int> Post(object body, CancellationToken ct)
	{
		for (int attempt = 0; attempt < 3; attempt++)
		{
			ct.ThrowIfCancellationRequested();
			HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
			req.Method = "POST";
			req.Timeout = 15000;
			req.UserAgent = "AutoRejoin/" + Updater.CurrentText;
			req.ContentType = "application/json";
			byte[] bytes = Encoding.UTF8.GetBytes(Store.Json().Serialize(body));
			req.ContentLength = bytes.Length;
			using (Stream stream = await req.GetRequestStreamAsync())
			{
				await stream.WriteAsync(bytes, 0, bytes.Length, ct);
			}
			HttpWebResponse resp;
			try
			{
				resp = (HttpWebResponse)(await req.GetResponseAsync());
			}
			catch (WebException ex) when (ex.Response is HttpWebResponse)
			{
				resp = (HttpWebResponse)ex.Response;
			}
			int code;
			string answer;
			using (resp)
			{
				code = (int)resp.StatusCode;
				using StreamReader reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8);
				answer = await reader.ReadToEndAsync();
			}
			if (code != 429)
			{
				return code;
			}
			double wait = 1.0;
			try
			{
				wait = Store.Num(Store.Json().DeserializeObject(answer) as IDictionary<string, object>, "retry_after") ?? 1.0;
			}
			catch
			{
			}
			await Task.Delay(TimeSpan.FromSeconds(Math.Min(wait, 30.0) + 0.1), ct);
		}
		return 429;
	}

	// ---------- утилиты ----------

	private void SetState(string text, bool healthy)
	{
		State = text;
		Healthy = healthy;
		dispatcher.BeginInvoke((Action)delegate
		{
			this.StateChanged?.Invoke();
		});
	}

	private static string DisplayName(VaultAccount account, int n)
	{
		if (!string.IsNullOrWhiteSpace(account.name))
		{
			return account.name;
		}
		if (!string.IsNullOrWhiteSpace(account.username))
		{
			return account.username;
		}
		if (!string.IsNullOrWhiteSpace(account.userId))
		{
			return account.userId;
		}
		return "Аккаунт " + n;
	}

	private static string Ago(DateTime utc)
	{
		TimeSpan span = DateTime.UtcNow - utc;
		if (span.TotalMinutes < 1.0)
		{
			return (int)Math.Max(1.0, span.TotalSeconds) + " сек";
		}
		if (span.TotalHours < 1.0)
		{
			return (int)span.TotalMinutes + " мин";
		}
		if (span.TotalDays < 1.0)
		{
			return (int)span.TotalHours + " ч " + span.Minutes + " мин";
		}
		return (int)span.TotalDays + " д " + span.Hours + " ч";
	}

	private static string Escape(string text)
	{
		return (text ?? "").Replace("*", "\\*").Replace("_", "\\_").Replace("`", "\\`").Replace("~", "\\~").Replace("|", "\\|");
	}
}
