using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace AutoRejoinApp;

// WEAO (whatexpsare.online / weao.xyz): is the chosen executor updated for the current Roblox version.
// Public API; WEAO asks third-party apps to send the User-Agent "WEAO-3PService". Cached for 10 minutes.
public static class Weao
{
	public class Status
	{
		public string Title;

		public string Version;

		public bool Updated;

		public string RobloxVersion;

		public string CurrentRoblox;

		public string UpdatedDate;

		public string Website;

		public DateTime CheckedAt;
	}

	private const string Api = "https://weao.xyz/api/";

	private static Status cached;

	private static string cachedFor;

	public static string Title(string executor)
	{
		return executor switch
		{
			"real" => "Real",
			"volt" => "Volt",
			"synapsez" => "Synapse Z",
			_ => "Potassium",
		};
	}

	public static string DisplayName(string executor)
	{
		return executor switch
		{
			"real" => "REAL",
			"volt" => "Volt",
			"synapsez" => "Synapse Z",
			_ => L.T("Потассиум"),
		};
	}

	public static Status Last => cached;

	public static async Task<Status> GetAsync(string executor, bool force = false)
	{
		string title = Title(executor);
		if (!force && cached != null && cachedFor == title && DateTime.UtcNow - cached.CheckedAt < TimeSpan.FromMinutes(10.0))
		{
			return cached;
		}
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		object list = Store.Json().DeserializeObject(await Get("status/exploits"));
		IDictionary<string, object> entry = (list as object[] ?? new object[0]).OfType<IDictionary<string, object>>()
			.FirstOrDefault((IDictionary<string, object> e) => string.Equals(Store.Str(e, "title"), title, StringComparison.OrdinalIgnoreCase) && Store.Str(e, "platform") == "Windows");
		if (entry == null)
		{
			throw new Exception(L.T("WEAO не знает такой экзекьютор"));
		}
		string current = null;
		try
		{
			current = Store.Str(Store.Json().DeserializeObject(await Get("versions/current")) as IDictionary<string, object>, "Windows");
		}
		catch
		{
		}
		Status s = new Status
		{
			Title = Store.Str(entry, "title"),
			Version = Store.Str(entry, "version"),
			Updated = Store.Flag(entry, "updateStatus"),
			RobloxVersion = Store.Str(entry, "rbxversion"),
			CurrentRoblox = current,
			UpdatedDate = Store.Str(entry, "updatedDate"),
			Website = Store.Str(entry, "websitelink"),
			CheckedAt = DateTime.UtcNow
		};
		cached = s;
		cachedFor = title;
		return s;
	}

	/// One line for the UI: "✓ обновлён под Роблокс · 3.2.3" or "⚠ не обновлён под текущий Роблокс".
	public static string Describe(Status s)
	{
		if (s == null)
		{
			return "";
		}
		bool ok = s.Updated && (string.IsNullOrEmpty(s.CurrentRoblox) || string.IsNullOrEmpty(s.RobloxVersion) || s.CurrentRoblox == s.RobloxVersion);
		string version = string.IsNullOrEmpty(s.Version) ? "" : (" · " + s.Version);
		return ok ? (L.T("WEAO: работает с текущим Роблоксом") + version) : (L.T("WEAO: не обновлён под текущий Роблокс — скрипт в окна может не попасть") + version);
	}

	public static bool IsOk(Status s)
	{
		return s == null || (s.Updated && (string.IsNullOrEmpty(s.CurrentRoblox) || string.IsNullOrEmpty(s.RobloxVersion) || s.CurrentRoblox == s.RobloxVersion));
	}

	private static async Task<string> Get(string path)
	{
		HttpWebRequest req = (HttpWebRequest)WebRequest.Create(Api + path);
		req.UserAgent = "WEAO-3PService";
		req.Accept = "application/json";
		req.Timeout = 15000;
		using WebResponse resp = await req.GetResponseAsync();
		using StreamReader reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8);
		return await reader.ReadToEndAsync();
	}
}
