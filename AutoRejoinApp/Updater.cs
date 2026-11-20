using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace AutoRejoinApp;

// Обновление с GitHub: берёт последний релиз репозитория, сравнивает его тег (v1.3.0) с версией программы,
// скачивает архив AutoRejoin.zip из релиза, распаковывает и подменяет файлы программы после её выхода.
// Данные пользователя (%LOCALAPPDATA%\AutoRejoin) не трогаются — в папке программы их нет.
public static class Updater
{
	public const string Repo = "clxwnlessss/AutoRejoin";

	public const string AssetName = "AutoRejoin.zip";

	public class Release
	{
		public Version Version;
		public string Tag;
		public string Notes;
		public string DownloadUrl;
		public string PageUrl;
	}

	static Updater()
	{
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
	}

	public static Version Current
	{
		get
		{
			Version v = Assembly.GetExecutingAssembly().GetName().Version;
			return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
		}
	}

	public static string CurrentText => Current.ToString(3);

	public static string RepoUrl => "https://github.com/" + Repo;

	// null — обновлений нет. Ошибка сети / GitHub — исключение с понятным текстом.
	public static async Task<Release> CheckAsync()
	{
		HttpWebRequest req = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + Repo + "/releases/latest");
		req.UserAgent = "AutoRejoin-updater";
		req.Accept = "application/vnd.github+json";
		req.Timeout = 15000;
		string body;
		try
		{
			using WebResponse resp = await req.GetResponseAsync();
			using StreamReader reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8);
			body = await reader.ReadToEndAsync();
		}
		catch (WebException ex) when (ex.Response is HttpWebResponse http)
		{
			if ((int)http.StatusCode == 404)
			{
				throw new Exception(L.T("на GitHub ещё нет ни одного релиза"));
			}
			if ((int)http.StatusCode == 403)
			{
				throw new Exception(L.T("GitHub временно ограничил проверки — попробуй через час"));
			}
			throw new Exception(L.T("GitHub ответил ") + (int)http.StatusCode);
		}
		catch (WebException ex)
		{
			throw new Exception(L.T("нет связи с GitHub (") + ex.Message + ")");
		}
		IDictionary<string, object> json = Store.Json().DeserializeObject(body) as IDictionary<string, object>;
		string tag = Store.Str(json, "tag_name") ?? "";
		if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version))
		{
			throw new Exception(L.T("непонятный номер версии в релизе: ") + tag);
		}
		if (version.Build < 0)
		{
			version = new Version(version.Major, version.Minor, 0);
		}
		if (version <= Current)
		{
			return null;
		}
		IDictionary<string, object> asset = Store.List(json, "assets").FirstOrDefault((IDictionary<string, object> a) => string.Equals(Store.Str(a, "name"), AssetName, StringComparison.OrdinalIgnoreCase));
		if (asset == null)
		{
			throw new Exception(L.T("в релизе ") + tag + L.T(" нет файла ") + AssetName);
		}
		return new Release
		{
			Version = version,
			Tag = tag,
			Notes = Store.Str(json, "body") ?? "",
			DownloadUrl = Store.Str(asset, "browser_download_url"),
			PageUrl = Store.Str(json, "html_url") ?? RepoUrl
		};
	}

	// Скачивает и распаковывает релиз, запускает скрипт, который дождётся выхода программы, заменит файлы
	// и запустит её снова. После этого вызывающий должен закрыть программу.
	public static async Task PrepareAsync(Release release)
	{
		string work = Path.Combine(Path.GetTempPath(), "AutoRejoin-update");
		if (Directory.Exists(work))
		{
			Directory.Delete(work, recursive: true);
		}
		Directory.CreateDirectory(work);
		string zip = Path.Combine(work, AssetName);
		using (WebClient client = new WebClient())
		{
			client.Headers[HttpRequestHeader.UserAgent] = "AutoRejoin-updater";
			await client.DownloadFileTaskAsync(new Uri(release.DownloadUrl), zip);
		}
		string files = Path.Combine(work, "files");
		ZipFile.ExtractToDirectory(zip, files);
		// Архив может содержать файлы в корне или в одной вложенной папке.
		string root = files;
		if (!File.Exists(Path.Combine(root, "AutoRejoin.exe")))
		{
			string[] dirs = Directory.GetDirectories(files);
			if (dirs.Length == 1 && File.Exists(Path.Combine(dirs[0], "AutoRejoin.exe")))
			{
				root = dirs[0];
			}
		}
		if (!File.Exists(Path.Combine(root, "AutoRejoin.exe")) || !File.Exists(Path.Combine(root, "engine.ps1")))
		{
			throw new Exception(L.T("в архиве обновления нет AutoRejoin.exe или engine.ps1"));
		}
		string appDir = AppPaths.AppDir.TrimEnd('\\');
		string exe = Path.Combine(appDir, "AutoRejoin.exe");
		int pid = Process.GetCurrentProcess().Id;
		string script = Path.Combine(work, "update.ps1");
		string text = "$ErrorActionPreference = 'Stop'\r\n" +
			"try { Wait-Process -Id " + pid + " -Timeout 60 -ErrorAction SilentlyContinue } catch {}\r\n" +
			"$ok = $false\r\n" +
			"for ($i = 0; $i -lt 20 -and -not $ok; $i++) {\r\n" +
			"    try { Copy-Item -Path " + Quote(Path.Combine(root, "*")) + " -Destination " + Quote(appDir) + " -Recurse -Force; $ok = $true }\r\n" +
			"    catch { Start-Sleep -Seconds 1 }\r\n" +
			"}\r\n" +
			"Start-Process -FilePath " + Quote(exe) + "\r\n";
		File.WriteAllText(script, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
		ProcessStartInfo info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell\\v1.0\\powershell.exe"), "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script + "\"")
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			WindowStyle = ProcessWindowStyle.Hidden
		};
		Process.Start(info);
	}

	private static string Quote(string path)
	{
		return "'" + path.Replace("'", "''") + "'";
	}
}
