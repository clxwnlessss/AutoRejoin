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

		public string PcVersion;

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
			PcVersion = RobloxInstall.Version,
			CheckedAt = DateTime.UtcNow
		};
		cached = s;
		cachedFor = title;
		return s;
	}

	/// One line for the UI: compares the Roblox build on this PC with the current one (weao.xyz/api/versions/current)
	/// and with the build the executor supports.
	public static string Describe(Status s)
	{
		if (s == null)
		{
			return "";
		}
		string name = s.Title == "Real" ? "REAL" : s.Title;
		string pc = s.PcVersion, cur = s.CurrentRoblox, exe = s.RobloxVersion;
		string version = string.IsNullOrEmpty(s.Version) ? "" : (" · " + name + " " + s.Version);
		if (string.IsNullOrEmpty(pc))
		{
			return (IsOk(s) ? L.T("WEAO: работает с текущим Роблоксом") : L.T("WEAO: не обновлён под текущий Роблокс — скрипт в окна может не попасть")) + version;
		}
		string pcText = string.Format(L.T("Роблокс на ПК: {0}"), RobloxInstall.Short(pc));
		if (!string.IsNullOrEmpty(exe) && pc == exe)
		{
			return pcText + " — " + string.Format(L.T("совпадает с версией, под которую обновлён {0} ✓"), name) + ((!string.IsNullOrEmpty(cur) && cur != pc) ? (" (" + string.Format(L.T("актуальный на WEAO: {0}"), RobloxInstall.Short(cur)) + ")") : "") + version;
		}
		if (!string.IsNullOrEmpty(cur) && pc != cur && exe == cur)
		{
			return pcText + " — " + string.Format(L.T("устарел, актуальный {0}. Обнови Роблокс: {1} уже под новую версию."), RobloxInstall.Short(cur), name) + version;
		}
		if (!string.IsNullOrEmpty(cur) && pc == cur)
		{
			return pcText + " — " + string.Format(L.T("актуальный, а {0} ещё не обновлён под него (собран под {1}). Ждём обновление {0}."), name, RobloxInstall.Short(exe)) + version;
		}
		return pcText + ", " + string.Format(L.T("на WEAO: {0}, {1} собран под {2} — версии не совпадают."), RobloxInstall.Short(cur), name, RobloxInstall.Short(exe)) + version;
	}

	/// The executor supports the Roblox build on this PC (or, if it is unknown, the current one).
	public static bool IsOk(Status s)
	{
		if (s == null)
		{
			return true;
		}
		string target = !string.IsNullOrEmpty(s.PcVersion) ? s.PcVersion : s.CurrentRoblox;
		if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(s.RobloxVersion))
		{
			return s.Updated;
		}
		return s.RobloxVersion == target;
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
