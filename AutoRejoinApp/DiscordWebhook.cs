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
//  • Ошибки движка (красные строки журнала) — сразу, одинаковые не чаще раза в 10 минут.
//  • Анализ перезаходов: аккаунт не может зайти 5 / 30 / 120 минут — на чём именно он застрял и что сделать;
//    в отчёте — сколько заходов удалось и на чём срывались неудачные, по каждому аккаунту.
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
		public DateTime? OutSince;   // с какого момента аккаунт не в игре (вылет или ещё не зашёл после запуска)
		public int FailsAtOut;       // счётчик неудачных попыток движка в этот момент
		public int StuckLevel;       // сколько порогов «не может перезайти» уже отправлено
	}

	// Статистика перезаходов аккаунта за этот запуск движка — для анализа «на чём он не перезаходит».
	private class Stat
	{
		public string Name;
		public bool Main;
		public int FailTotal;
		public int JoinOk;
		public int Drops;
		public string LastError;
		public readonly Dictionary<string, int> Causes = new Dictionary<string, int>();
	}

	private struct Diagnosis
	{
		public string Cause;
		public string Hint;
	}

	// Пороги «не может перезайти», минуты.
	private static readonly int[] StuckMinutes = new int[3] { 5, 30, 120 };

	private class Event
	{
		public string Text;
		public bool Mention;
		public string Kind;
		public string AccountName;
		public Task<byte[]> Image;
	}

	private string lastReportDay;

	// Перезаходы, которые запросил ты или расписание, — не вылет, о них не пишем.
	private static readonly Regex Planned = new Regex("^(перезаход по кнопке|плановый перезаход|перезаход по Balloon|уступил очередь)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	private readonly MainController main;

	private readonly Dispatcher dispatcher;

	private readonly Dictionary<string, Track> tracks = new Dictionary<string, Track>();

	private readonly ConcurrentQueue<Event> events = new ConcurrentQueue<Event>();

	private readonly Dictionary<string, Stat> stats = new Dictionary<string, Stat>();

	private volatile string analysisText;

	private readonly Dictionary<string, DateTime> errorSeen = new Dictionary<string, DateTime>();

	private bool wasRunning;

	private volatile bool errorsOn = true;

	private bool potassiumSignedOut;

	private CancellationTokenSource cts;

	private string runningKey;

	private string url;

	private volatile int intervalMinutes = 15;

	private volatile bool eventsOn = true;

	private volatile string[] mentionIds = new string[0];

	private volatile Snapshot snapshot;

	private volatile bool sendNow;

	public string State { get; private set; } = L.T("выключен");

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
		errorsOn = cfg.discordErrors;
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
			SetState(L.T("выключен"), healthy: false);
			return;
		}
		if (!IsValidUrl(newUrl))
		{
			SetState(L.T("не настроен — вставь ссылку вебхука"), healthy: false);
			return;
		}
		url = newUrl;
		cts = new CancellationTokenSource();
		CancellationToken ct = cts.Token;
		SetState(L.T("ждёт первого отчёта"), healthy: false);
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
			Enqueue("⚠️ " + text.Trim(), mention: true, kind: "alert");
		}
	}

	// Строка журнала движка. Красные — ошибки: пишем в Discord сразу (одинаковые — не чаще раза в 10 минут).
	public void OnLog(LogLine line)
	{
		if (line == null || !errorsOn || cts == null || !string.Equals(line.Color, "Red", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(line.Message))
		{
			return;
		}
		DateTime now = DateTime.UtcNow;
		string key = (line.Key ?? "") + "|" + Regex.Replace(line.Message, "\\d+", "#");
		if (errorSeen.TryGetValue(key, out var seen) && (now - seen).TotalMinutes < 10.0)
		{
			return;
		}
		errorSeen[key] = now;
		if (errorSeen.Count > 200)
		{
			foreach (string old in errorSeen.Where((KeyValuePair<string, DateTime> p) => (now - p.Value).TotalMinutes >= 10.0).Select((KeyValuePair<string, DateTime> p) => p.Key).ToList())
			{
				errorSeen.Remove(old);
			}
		}
		string who = string.IsNullOrEmpty(line.Account) ? "" : ("**" + Escape(line.Account) + "**: ");
		Enqueue("❌ " + who + L.Engine(line.Message.Trim()), mention: false, force: true, kind: "error", accountName: line.Account);
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
		// Новый запуск движка — статистика перезаходов с нуля (у движка счётчики тоже обнулились).
		if (running && !wasRunning)
		{
			stats.Clear();
		}
		wasRunning = running;
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
				Text = (running ? L.T("ждёт запуска") : L.T("AutoRejoin остановлен"))
			};
			string state = null;
			string lastReason = null;
			DateTime? lastReasonAt = null;
			if (byKey.TryGetValue(account.id, out var view))
			{
				state = Store.Str(view, "state");
				row.InGame = state == "ingame" || state == "script";
				row.Text = L.Engine(Store.Str(view, "text")) ?? row.Text;
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
			if (view != null)
			{
				Count(account.id, row, view);
			}
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
				Enqueue(L.T("⚠️ Потассиум разлогинился — скрипт в новые окна не подключится. Войди в Потассиум (или включи «Автовход» в настройках AutoRejoin)."), mention: true);
			}
			potassiumSignedOut = signedOut;
		}
		snapshot = snap;
		analysisText = Analysis();
		CheckDailyReport();
	}

	// At 23:55 (local time) once a day: the server builds the last-24-h report, sends it to Telegram, and the app posts
	// it to the Discord webhook.
	private void CheckDailyReport()
	{
		DateTime local = DateTime.Now;
		string day = local.ToString("yyyy-MM-dd");
		if (!main.Config.dailyReport || local.Hour != 23 || local.Minute < 55 || lastReportDay == day || main.Bot == null || !main.Bot.Linked)
		{
			return;
		}
		lastReportDay = day;
		BotAgent bot = main.Bot;
		Task.Run(async delegate
		{
			try
			{
				IDictionary<string, object> r = await bot.DailyReport();
				if (url != null && cts != null)
				{
					await Post(ReportEmbed(r), CancellationToken.None);
				}
			}
			catch (Exception ex)
			{
				App.LogError(L.T("отчёт за день"), ex);
			}
		});
	}

	private Dictionary<string, object> ReportEmbed(IDictionary<string, object> r)
	{
		StringBuilder text = new StringBuilder();
		long totalSec = 0, totalHoney = 0, totalDrops = 0;
		foreach (IDictionary<string, object> a in Store.List(r, "accounts"))
		{
			long sec = (long)(Store.Num(a, "seconds") ?? 0.0), honey = (long)(Store.Num(a, "honey") ?? 0.0), drops = (long)(Store.Num(a, "drops") ?? 0.0);
			totalSec += sec;
			totalHoney += honey;
			totalDrops += drops;
			string line = "• **" + Escape(Store.Str(a, "name") ?? "?") + "** — " + Ago(DateTime.UtcNow.AddSeconds(-sec)) + (honey > 0 ? (" · 🍯 " + Short(honey) + " (" + Short((long)(honey / Math.Max(1.0, sec / 3600.0))) + L.T("/ч)")) : "") + (drops > 0 ? (L.T(" · вылетов: ") + drops) : "");
			if (text.Length + line.Length > 3500)
			{
				break;
			}
			text.Append(line).Append('\n');
		}
		if (text.Length == 0)
		{
			text.Append(L.T("Данных за сутки пока нет."));
		}
		List<string> causes = Store.List(r, "causes").Select((IDictionary<string, object> c) => "• " + Store.Str(c, "text") + " ×" + (Store.Num(c, "n") ?? 0.0)).ToList();
		List<object> fields = new List<object>();
		if (causes.Count > 0)
		{
			fields.Add(new Dictionary<string, object> { ["name"] = L.T("Причины вылетов"), ["value"] = string.Join("\n", causes) });
		}
		return new Dictionary<string, object>
		{
			["username"] = "AutoRejoin",
			["allowed_mentions"] = new Dictionary<string, object> { ["parse"] = new object[0] },
			["embeds"] = new object[1]
			{
				new Dictionary<string, object>
				{
					["title"] = L.T("📊 Отчёт за сутки"),
					["description"] = L.T("⏱ В игре: **") + Ago(DateTime.UtcNow.AddSeconds(-totalSec)) + "**" + (totalHoney > 0 ? (L.T(" · 🍯 **") + Short(totalHoney) + "**") : "") + L.T(" · 🔴 вылетов: **") + totalDrops + "**\n\n" + text.ToString().TrimEnd(),
					["color"] = 0xA855F7,
					["fields"] = fields.ToArray(),
					["footer"] = new Dictionary<string, object> { ["text"] = Owner.Credit + " · " + Environment.MachineName },
					["timestamp"] = DateTime.UtcNow.ToString("o")
				}
			}
		};
	}

	private static string Short(long n)
	{
		if (n >= 1000000000)
		{
			return (n / 1e9).ToString("0.00", CultureInfo.InvariantCulture) + "B";
		}
		if (n >= 1000000)
		{
			return (n / 1e6).ToString("0.0", CultureInfo.InvariantCulture) + "M";
		}
		if (n >= 1000)
		{
			return (n / 1e3).ToString("0.0", CultureInfo.InvariantCulture) + "K";
		}
		return n.ToString(CultureInfo.InvariantCulture);
	}

	// Движок считает неудачные попытки зайти и пишет последнюю ошибку — копим, на чём именно срывался аккаунт.
	private void Count(string key, Row row, IDictionary<string, object> view)
	{
		if (!stats.TryGetValue(key, out var stat))
		{
			stat = (stats[key] = new Stat());
		}
		stat.Name = row.Name;
		stat.Main = row.Main;
		int failTotal = (int)(Store.Num(view, "failTotal") ?? 0.0);
		stat.JoinOk = (int)(Store.Num(view, "joinOk") ?? 0.0);
		string lastError = Store.Str(view, "lastError");
		if (failTotal > stat.FailTotal)
		{
			string cause = Classify(lastError).Cause;
			stat.Causes.TryGetValue(cause, out var had);
			stat.Causes[cause] = had + (failTotal - stat.FailTotal);
		}
		stat.FailTotal = failTotal;
		if (!string.IsNullOrEmpty(lastError))
		{
			stat.LastError = lastError;
		}
	}

	// По тексту ошибки движка — что это за причина и что с ней делать.
	private static Diagnosis Classify(string error)
	{
		string e = (error ?? "").ToLowerInvariant();
		string cause;
		string hint;
		if (e.Contains("не принял вход"))
		{
			cause = "Роблокс не принял вход";
			hint = "сессия аккаунта сброшена — войди в аккаунт в AutoRejoin заново";
		}
		else if (e.Contains("401") || e.Contains("кука"))
		{
			cause = "кука перестала работать";
			hint = "войди в аккаунт в AutoRejoin заново";
		}
		else if (e.Contains("модерац") || e.Contains("не пускает"))
		{
			cause = "Роблокс не пускает аккаунт";
			hint = "проверь аккаунт на сайте Роблокса (бан или предупреждение)";
		}
		else if (e.Contains("умерло сразу"))
		{
			cause = "окно умирает сразу после запуска";
			hint = "так бывает после падения окна с Потассиумом — поможет перезагрузка компьютера";
		}
		else if (e.Contains("не открылось"))
		{
			cause = "окно не открылось";
			hint = "Роблокс или Froststrap завис при запуске — закрой лишние процессы Роблокса";
		}
		else if (e.Contains("не получилось запустить"))
		{
			cause = "ошибка запуска";
			hint = "не удалось получить билет или запустить Роблокс — проверь интернет";
		}
		else if (e.Contains("не зашёл на сервер"))
		{
			cause = "не зашёл на сервер";
			hint = "сервер полон или недоступен, либо устарела ссылка на приватный сервер";
		}
		else if (e.Contains("меню"))
		{
			cause = "прыжок на сервер не удался";
			hint = "сервер полон или закрылся — проверь ссылку";
		}
		else if (e.Contains("не отвечает"))
		{
			cause = "окно зависло при загрузке";
			hint = "не хватает памяти или ЦП — уменьши число окон или графику";
		}
		else if (e.Contains("раздулось"))
		{
			cause = "окну не хватило памяти";
			hint = "уменьши число окон или подними лимит памяти";
		}
		else if (e.Contains("скрипт"))
		{
			cause = "скрипт не запустился";
			hint = "проверь, что Потассиум запущен и вошёл в аккаунт";
		}
		else if (e.Contains("закрылось"))
		{
			cause = "окно закрылось до захода";
			hint = "Роблокс падает при загрузке — проверь, не зашёл ли аккаунт с другого устройства";
		}
		else
		{
			cause = (e.Length == 0) ? "причина неизвестна" : ((error.Length > 60) ? (error.Substring(0, 60) + "…") : error);
			hint = null;
		}
		return new Diagnosis
		{
			Cause = cause,
			Hint = hint
		};
	}

	// Аккаунт не в игре дольше порога — пишем, на чём именно он не перезаходит.
	private void CheckStuck(Track track, string key, Row row, bool problem, DateTime now)
	{
		if (row.InGame || !track.OutSince.HasValue || track.StuckLevel >= StuckMinutes.Length)
		{
			return;
		}
		if ((now - track.OutSince.Value).TotalMinutes < StuckMinutes[track.StuckLevel])
		{
			return;
		}
		track.StuckLevel++;
		// Проблему, которая сама не чинится (кука, модерация, перезагрузка), уже прислали с упоминанием.
		if (problem)
		{
			return;
		}
		stats.TryGetValue(key, out var stat);
		int tries = (stat != null) ? Math.Max(0, stat.FailTotal - track.FailsAtOut) : 0;
		StringBuilder line = new StringBuilder();
		line.Append("🟠 **").Append(Escape(row.Name)).Append("**").Append(row.Main ? " ⭐" : "")
			.Append(L.T(" не может перезайти уже ")).Append(Ago(track.OutSince.Value))
			.Append(L.T(" · неудачных попыток: ")).Append(tries);
		string error = stat?.LastError;
		if (tries > 0 && !string.IsNullOrEmpty(error))
		{
			Diagnosis d = Classify(error);
			line.Append(L.T(" · последняя: ")).Append(L.Engine(error));
			if (d.Hint != null)
			{
				line.Append("\n   ↳ ").Append(L.T(d.Hint));
			}
		}
		else
		{
			line.Append(L.T(" · сейчас: ")).Append(row.Text);
		}
		Enqueue(line.ToString(), mention: track.StuckLevel >= 2, force: true, kind: "stuck", accountKey: key, accountName: row.Name);
	}

	private void Watch(string key, Row row, string state, string lastReason, DateTime? lastReasonAt, DateTime now)
	{
		bool problem = state == "problem" || state == "deferred" || state == "blocked";
		if (!tracks.TryGetValue(key, out var track))
		{
			// Первый статус после запуска: просто запоминаем, иначе каждый аккаунт, который ещё грузится, «вылетел» бы.
			stats.TryGetValue(key, out var first);
			tracks[key] = new Track
			{
				InGame = row.InGame,
				Problem = (problem ? row.Text : null),
				OutSince = (row.InGame ? ((DateTime?)null) : new DateTime?(now)),
				FailsAtOut = (first?.FailTotal ?? 0)
			};
			if (problem)
			{
				Enqueue("⚠️ **" + Escape(row.Name) + "**: " + row.Text, mention: true, kind: "alert", accountKey: key, accountName: row.Name);
			}
			return;
		}
		if (track.InGame && !row.InGame)
		{
			// Причина — та, что движок записал при перезапуске (свежая), иначе текущее состояние окна.
			string reason = (lastReasonAt.HasValue && (now - lastReasonAt.Value).TotalSeconds < 90.0 && !string.IsNullOrEmpty(lastReason)) ? lastReason : row.Text;
			track.DownSince = now;
			track.OutSince = now;
			track.StuckLevel = 0;
			track.FailsAtOut = stats.TryGetValue(key, out var st) ? st.FailTotal : 0;
			if (st != null)
			{
				st.Drops++;
			}
			track.DropReported = !Planned.IsMatch(reason ?? "");
			if (track.DropReported)
			{
				int? pid = main.Cards.FirstOrDefault((AccountCard c) => c.Key == key)?.Pid;
				Enqueue("🔴 **" + Escape(row.Name) + "**" + (row.Main ? " ⭐" : "") + L.T(" вылетел: ") + L.Engine(reason), mention: false,
					kind: "drop", accountKey: key, accountName: row.Name, image: main.Recorder.Storyboard(key, pid, row.Name));
			}
		}
		else if (!track.InGame && row.InGame)
		{
			if (track.DropReported || track.StuckLevel > 0)
			{
				int tries = stats.TryGetValue(key, out var st2) ? Math.Max(0, st2.FailTotal - track.FailsAtOut) : 0;
				string since = track.OutSince.HasValue ? Ago(track.OutSince.Value) : Ago(track.DownSince);
				Enqueue("🟢 **" + Escape(row.Name) + "**" + (row.Main ? " ⭐" : "") + L.T(" снова в игре (не было ") + since + ((tries > 0) ? (L.T(", неудачных попыток: ") + tries) : "") + ")", mention: false, force: track.StuckLevel > 0,
					kind: "back", accountKey: key, accountName: row.Name);
			}
			track.DropReported = false;
			track.OutSince = null;
			track.StuckLevel = 0;
		}
		track.InGame = row.InGame;
		CheckStuck(track, key, row, problem, now);
		if (problem)
		{
			if (track.Problem != row.Text)
			{
				Enqueue("⚠️ **" + Escape(row.Name) + "**: " + row.Text, mention: true, kind: "alert", accountKey: key, accountName: row.Name);
			}
			track.Problem = row.Text;
		}
		else
		{
			track.Problem = null;
		}
	}

	private void Enqueue(string text, bool mention, bool force = false, string kind = null, string accountKey = null, string accountName = null, Task<byte[]> image = null)
	{
		// force — ошибки и анализ перезаходов: у них свой переключатель «Ошибки и анализ перезаходов».
		if (force ? !errorsOn : !eventsOn)
		{
			return;
		}
		// Telegram and the farm analytics get every event through the server, whether the webhook is set up or not.
		string evKind = kind ?? (force ? "error" : "info");
		BotAgent bot = main.Bot;
		if (bot != null && bot.Linked)
		{
			if (image == null)
			{
				bot.SendEvent(evKind, text, accountKey, accountName, null);
			}
			else
			{
				image.ContinueWith(delegate(Task<byte[]> t)
				{
					bot.SendEvent(evKind, text, accountKey, accountName, t.Status == TaskStatus.RanToCompletion ? t.Result : null);
				});
			}
		}
		if (cts == null)
		{
			return;
		}
		events.Enqueue(new Event
		{
			Text = text,
			Mention = mention,
			Kind = evKind,
			AccountName = accountName,
			Image = image
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
				SetState(L.T("ошибка: ") + ex.Message, healthy: false);
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
		Report(await Post(body, ct), L.T("сообщение отправлено"));
		// Drop storyboards go as separate messages with the picture.
		foreach (Event e in batch.Where((Event x) => x.Image != null))
		{
			byte[] jpeg = null;
			try
			{
				jpeg = await e.Image;
			}
			catch
			{
			}
			if (jpeg != null)
			{
				await PostFile(L.T("🎞 Раскадровка вылета: **") + Escape(e.AccountName ?? "") + "**", jpeg, "drop.jpg", ct);
			}
		}
	}

	// Message with one picture (multipart/form-data: payload_json + files[0]).
	private async Task<int> PostFile(string content, byte[] file, string fileName, CancellationToken ct)
	{
		string boundary = "----AutoRejoin" + Guid.NewGuid().ToString("N");
		string payload = Store.Json().Serialize(new Dictionary<string, object>
		{
			["username"] = "AutoRejoin",
			["content"] = content,
			["allowed_mentions"] = new Dictionary<string, object> { ["parse"] = new object[0] }
		});
		using MemoryStream body = new MemoryStream();
		void W(string s)
		{
			byte[] b = Encoding.UTF8.GetBytes(s);
			body.Write(b, 0, b.Length);
		}
		W("--" + boundary + "\r\nContent-Disposition: form-data; name=\"payload_json\"\r\nContent-Type: application/json\r\n\r\n" + payload + "\r\n");
		W("--" + boundary + "\r\nContent-Disposition: form-data; name=\"files[0]\"; filename=\"" + fileName + "\"\r\nContent-Type: image/jpeg\r\n\r\n");
		body.Write(file, 0, file.Length);
		W("\r\n--" + boundary + "--\r\n");
		try
		{
			HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
			req.Method = "POST";
			req.Timeout = 30000;
			req.UserAgent = "AutoRejoin/" + Updater.CurrentText;
			req.ContentType = "multipart/form-data; boundary=" + boundary;
			byte[] bytes = body.ToArray();
			req.ContentLength = bytes.Length;
			using (Stream stream = await req.GetRequestStreamAsync())
			{
				await stream.WriteAsync(bytes, 0, bytes.Length, ct);
			}
			using HttpWebResponse resp = (HttpWebResponse)(await req.GetResponseAsync());
			return (int)resp.StatusCode;
		}
		catch (WebException ex) when (ex.Response is HttpWebResponse r)
		{
			return (int)r.StatusCode;
		}
		catch
		{
			return 0;
		}
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
			string line = (row.InGame ? "🟢" : "🔴") + " **" + Escape(row.Name) + "**" + (row.Main ? " ⭐" : "") + " — " + (row.InGame ? (L.T("в игре") + (row.StartedAt.HasValue ? (" " + Ago(row.StartedAt.Value)) : "")) : (L.T("не в игре (") + row.Text + ")"));
			if (text.Length + line.Length > 3900)
			{
				text.Append("\n…");
				break;
			}
			text.Append(line).Append('\n');
		}
		if (snap.Rows.Count == 0)
		{
			text.Append(L.T("Включённых аккаунтов нет."));
		}
		int total = snap.Rows.Count;
		string analysis = errorsOn ? analysisText : null;
		int color = ((!snap.Running || inGame == 0) ? 0xE5484D : ((inGame < total) ? 0xF5A524 : 0x30A46C));
		Dictionary<string, object> body = new Dictionary<string, object>
		{
			["username"] = "AutoRejoin",
			["allowed_mentions"] = new Dictionary<string, object> { ["parse"] = new object[0] },
			["embeds"] = new object[1]
			{
				new Dictionary<string, object>
				{
					["title"] = (snap.Running ? (L.T("В игре ") + inGame + L.T(" из ") + total) : L.T("AutoRejoin остановлен")),
					["description"] = text.ToString().TrimEnd(),
					["color"] = color,
					["fields"] = ((analysis == null) ? new object[0] : new object[1]
					{
						new Dictionary<string, object>
						{
							["name"] = L.T("Анализ перезаходов"),
							["value"] = analysis
						}
					}),
					["footer"] = new Dictionary<string, object> { ["text"] = Owner.Credit + " · " + Environment.MachineName },
					["timestamp"] = DateTime.UtcNow.ToString("o")
				}
			}
		};
		Report(await Post(body, ct), L.T("последний отчёт в ") + DateTime.Now.ToString("HH:mm"));
	}

	// По каждому аккаунту с неудачными попытками: сколько заходов удалось и на чём срывались остальные.
	private string Analysis()
	{
		Stat[] list = stats.Values.Where((Stat s) => s.FailTotal > 0).OrderByDescending((Stat s) => s.FailTotal).ToArray();
		if (list.Length == 0)
		{
			return null;
		}
		StringBuilder text = new StringBuilder();
		foreach (Stat s in list)
		{
			string causes = string.Join(", ", s.Causes.OrderByDescending((KeyValuePair<string, int> c) => c.Value).Select((KeyValuePair<string, int> c) => L.T(c.Key) + " ×" + c.Value));
			string line = "• **" + Escape(s.Name) + "**" + (s.Main ? " ⭐" : "") + " — ✅ " + s.JoinOk + " / ❌ " + s.FailTotal + ((causes.Length > 0) ? (": " + causes) : "");
			if (text.Length + line.Length > 1000)
			{
				text.Append("…");
				break;
			}
			text.Append(line).Append('\n');
		}
		return text.ToString().TrimEnd();
	}

	private void Report(int code, string okText)
	{
		if (code >= 200 && code < 300)
		{
			SetState(L.T("работает — ") + okText, healthy: true);
		}
		else if (code == 401 || code == 404)
		{
			SetState(L.T("вебхук не найден — его удалили или ссылка неверная"), healthy: false);
		}
		else
		{
			SetState(L.T("Discord ответил ") + code, healthy: false);
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
		return L.T("Аккаунт ") + n;
	}

	private static string Ago(DateTime utc)
	{
		TimeSpan span = DateTime.UtcNow - utc;
		if (span.TotalMinutes < 1.0)
		{
			return (int)Math.Max(1.0, span.TotalSeconds) + L.T(" сек");
		}
		if (span.TotalHours < 1.0)
		{
			return (int)span.TotalMinutes + L.T(" мин");
		}
		if (span.TotalDays < 1.0)
		{
			return (int)span.TotalHours + L.T(" ч ") + span.Minutes + L.T(" мин");
		}
		return (int)span.TotalDays + L.T(" д ") + span.Hours + L.T(" ч");
	}

	private static string Escape(string text)
	{
		return (text ?? "").Replace("*", "\\*").Replace("_", "\\_").Replace("`", "\\`").Replace("~", "\\~").Replace("|", "\\|");
	}
}
