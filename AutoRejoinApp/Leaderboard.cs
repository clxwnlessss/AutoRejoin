using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace AutoRejoinApp;

// Клиент сервера AutoRejoin (Render): аккаунты, пульс АФК, топ стендеров за месяц.
// Пароль уходит только на сервер по HTTPS и нигде не хранится; хранится токен входа (зашифрованным в vault.dat).
public static class Leaderboard
{
	public const string DefaultUrl = "https://autorejoin-server.onrender.com";

	public class Entry
	{
		public int Rank;
		public string Nickname;
		public long Seconds;
		public bool Online;
	}

	public class Me
	{
		public string Nickname;
		public long Seconds;
		public int? Rank;
	}

	public class ApiException : Exception
	{
		public int Status;

		public ApiException(int status, string message)
			: base(message)
		{
			Status = status;
		}
	}

	static Leaderboard()
	{
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
	}

	public static string BaseUrl(AppConfig config)
	{
		string url = (config.leaderboardUrl ?? "").Trim().TrimEnd('/');
		// Только HTTPS (пароль не должен идти открытым текстом); исключение — локальный сервер для проверки.
		if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase))
		{
			url = DefaultUrl;
		}
		return url;
	}

	public static async Task<Tuple<string, string>> Register(string baseUrl, string nickname, string password)
	{
		IDictionary<string, object> r = await Call(baseUrl, "POST", "/api/register", new Dictionary<string, object>
		{
			["nickname"] = nickname,
			["password"] = password
		}, null);
		return Tuple.Create(Store.Str(r, "token"), Store.Str(r, "nickname"));
	}

	public static async Task<Tuple<string, string>> Login(string baseUrl, string nickname, string password)
	{
		IDictionary<string, object> r = await Call(baseUrl, "POST", "/api/login", new Dictionary<string, object>
		{
			["nickname"] = nickname,
			["password"] = password
		}, null);
		return Tuple.Create(Store.Str(r, "token"), Store.Str(r, "nickname"));
	}

	public static async Task Logout(string baseUrl, string token)
	{
		await Call(baseUrl, "POST", "/api/logout", null, token);
	}

	public static async Task<Me> Heartbeat(string baseUrl, string token, int inGame)
	{
		IDictionary<string, object> r = await Call(baseUrl, "POST", "/api/heartbeat", new Dictionary<string, object> { ["inGame"] = inGame }, token);
		return ToMe(r, null);
	}

	public static async Task<Me> GetMe(string baseUrl, string token)
	{
		IDictionary<string, object> r = await Call(baseUrl, "GET", "/api/me", null, token);
		return ToMe(r, Store.Str(r, "nickname"));
	}

	public static async Task<Tuple<string, List<Entry>>> Top(string baseUrl)
	{
		IDictionary<string, object> r = await Call(baseUrl, "GET", "/api/top?limit=50", null, null);
		List<Entry> list = Store.List(r, "top").Select((IDictionary<string, object> e) => new Entry
		{
			Rank = (int)(Store.Num(e, "rank") ?? 0.0),
			Nickname = Store.Str(e, "nickname") ?? "?",
			Seconds = (long)(Store.Num(e, "seconds") ?? 0.0),
			Online = Store.Flag(e, "online")
		}).ToList();
		return Tuple.Create(Store.Str(r, "month") ?? "", list);
	}

	// ---------- Discord bot ----------

	public class BotCode
	{
		public string Code;
		public int ExpiresIn;
		public string Invite;
	}

	public static async Task<BotCode> DiscordCode(string baseUrl, string token)
	{
		IDictionary<string, object> r = await Call(baseUrl, "POST", "/api/discord/code", null, token);
		return new BotCode
		{
			Code = Store.Str(r, "code"),
			ExpiresIn = (int)(Store.Num(r, "expiresIn") ?? 600.0),
			Invite = Store.Str(r, "invite")
		};
	}

	public static Task<IDictionary<string, object>> DiscordLink(string baseUrl, string token)
	{
		return Call(baseUrl, "GET", "/api/discord/link", null, token, 20000);
	}

	public static async Task DiscordUnlink(string baseUrl, string token)
	{
		await Call(baseUrl, "DELETE", "/api/discord/link", null, token);
	}

	public static Task<IDictionary<string, object>> AgentSync(string baseUrl, string token, object state)
	{
		return Call(baseUrl, "POST", "/api/agent/sync", state, token, 20000);
	}

	private static Me ToMe(IDictionary<string, object> r, string nickname)
	{
		double? rank = Store.Num(r, "rank");
		return new Me
		{
			Nickname = nickname,
			Seconds = (long)(Store.Num(r, "seconds") ?? 0.0),
			Rank = rank.HasValue ? new int?((int)rank.Value) : null
		};
	}

	// Бесплатный сервер Render засыпает без запросов и просыпается до минуты — отсюда большой таймаут.
	private static async Task<IDictionary<string, object>> Call(string baseUrl, string method, string path, object body, string token, int timeoutMs = 70000)
	{
		HttpWebRequest req = (HttpWebRequest)WebRequest.Create(baseUrl + path);
		req.Method = method;
		req.Timeout = timeoutMs;
		req.ReadWriteTimeout = timeoutMs;
		req.UserAgent = "AutoRejoin/" + Updater.CurrentText;
		req.Accept = "application/json";
		if (token != null)
		{
			req.Headers["Authorization"] = "Bearer " + token;
		}
		if (body != null)
		{
			byte[] bytes = Encoding.UTF8.GetBytes(Store.Json().Serialize(body));
			req.ContentType = "application/json";
			req.ContentLength = bytes.Length;
			using Stream stream = await req.GetRequestStreamAsync();
			await stream.WriteAsync(bytes, 0, bytes.Length);
		}
		else if (method == "POST")
		{
			req.ContentLength = 0L;
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
		catch (WebException ex)
		{
			throw new ApiException(0, (ex.Status == WebExceptionStatus.Timeout) ? L.T("Сервер не ответил — попробуй ещё раз через минуту.") : L.T("Нет связи с сервером."));
		}
		string text;
		int status;
		using (resp)
		{
			status = (int)resp.StatusCode;
			using StreamReader reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8);
			text = await reader.ReadToEndAsync();
		}
		IDictionary<string, object> json = null;
		try
		{
			json = Store.Json().DeserializeObject(text) as IDictionary<string, object>;
		}
		catch
		{
		}
		if (status < 200 || status >= 300)
		{
			throw new ApiException(status, L.T(Store.Str(json, "error")) ?? (L.T("Сервер ответил ") + status + "."));
		}
		return json ?? new Dictionary<string, object>();
	}

	public static string FormatHours(long seconds)
	{
		long h = seconds / 3600;
		long m = seconds % 3600 / 60;
		if (h <= 0)
		{
			return m + L.T(" мин");
		}
		return h + L.T(" ч ") + m.ToString("00") + L.T(" мин");
	}
}
