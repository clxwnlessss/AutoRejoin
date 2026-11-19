using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace AutoRejoinApp;

// Отчёт в Discord через вебхук: раз в N минут (по умолчанию 15) — какие аккаунты в игре, а какие нет.
// Только отправка сообщений, никакого управления из Discord.
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

	private readonly MainController main;

	private readonly Dispatcher dispatcher;

	private CancellationTokenSource cts;

	private string runningKey;

	private string url;

	private volatile int intervalMinutes = 15;

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

	// ---------- вызывается из UI-потока ----------

	public void Apply()
	{
		AppConfig cfg = main.Config;
		string newUrl = (main.Vault.discordWebhook ?? "").Trim();
		intervalMinutes = Math.Max(1, Math.Min(1440, cfg.discordInterval));
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

	// Снимок состояния аккаунтов: vault и статус движка читаем только в UI-потоке.
	public void Update()
	{
		Snapshot snap = new Snapshot { Running = main.EngineRunning };
		IDictionary<string, object> status = snap.Running ? main.LastStatus : null;
		Dictionary<string, IDictionary<string, object>> byKey = new Dictionary<string, IDictionary<string, object>>();
		foreach (IDictionary<string, object> item in Store.List(status, "accounts"))
		{
			string k = Store.Str(item, "key");
			if (k != null)
			{
				byKey[k] = item;
			}
		}
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
				Text = (snap.Running ? "ждёт запуска" : "AutoRejoin остановлен")
			};
			if (byKey.TryGetValue(account.id, out var view))
			{
				string state = Store.Str(view, "state");
				row.InGame = state == "ingame" || state == "script";
				row.Text = Store.Str(view, "text") ?? row.Text;
				row.StartedAt = Store.Time(view, "startedAt");
				string engineName = Store.Str(view, "name");
				if (string.IsNullOrEmpty(account.name) && !string.IsNullOrEmpty(engineName))
				{
					row.Name = engineName;
				}
			}
			snap.Rows.Add(row);
		}
		snapshot = snap;
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
				while (!sendNow && DateTime.UtcNow < next)
				{
					await Task.Delay(500, ct);
				}
				sendNow = false;
				next = DateTime.UtcNow.AddMinutes(intervalMinutes);
				await Send(ct);
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

	private async Task Send(CancellationToken ct)
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
		int code = await Post(body, ct);
		if (code >= 200 && code < 300)
		{
			SetState("работает — последний отчёт в " + DateTime.Now.ToString("HH:mm"), healthy: true);
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
			req.UserAgent = "AutoRejoin/1.2";
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
			return "<1 мин";
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
