using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AutoRejoinApp;

public static class RobloxApi
{
	private class ApiAnswer
	{
		public int Status;

		public string Body = "";

		public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
	}

	private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";

	static RobloxApi()
	{
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
	}

	public static string CleanCookie(string raw)
	{
		if (raw == null)
		{
			return "";
		}
		string text = Regex.Replace(raw, "\\s+", "").Trim('"', '\'');
		if (text.StartsWith(".ROBLOSECURITY=", StringComparison.OrdinalIgnoreCase))
		{
			text = text.Substring(".ROBLOSECURITY=".Length);
		}
		return text;
	}

	public static bool LooksLikeCookie(string cookie)
	{
		if (cookie.Length < 100 || cookie.IndexOf("WARNING", StringComparison.OrdinalIgnoreCase) < 0)
		{
			return cookie.Length >= 300;
		}
		return true;
	}

	public static RobloxUser CheckCookie(string cookie)
	{
		ApiAnswer apiAnswer = Send("GET", "https://users.roblox.com/v1/users/authenticated", cookie, null);
		if (apiAnswer.Status == 200)
		{
			IDictionary<string, object> data = Store.Json().DeserializeObject(apiAnswer.Body) as IDictionary<string, object>;
			string text = Store.Str(data, "id");
			if (string.IsNullOrEmpty(text))
			{
				throw new CookieException("Роблокс ответил без ID аккаунта — попробуй ещё раз.");
			}
			RobloxUser robloxUser = new RobloxUser();
			robloxUser.Id = text;
			robloxUser.Name = Store.Str(data, "name") ?? text;
			robloxUser.DisplayName = Store.Str(data, "displayName");
			return robloxUser;
		}
		throw Explain(apiAnswer);
	}

	private static CookieException Explain(ApiAnswer answer)
	{
		switch (answer.Status)
		{
		case 0:
			return new CookieException("Нет связи с Роблоксом: " + answer.Body, 0);
		case 401:
			return new CookieException("Кука не подходит: недействительна или устарела (Роблокс ответил 401).", 401);
		case 403:
			if (answer.Body.IndexOf("moderated", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return new ModeratedException("Роблокс не пускает этот аккаунт (403): на нём предупреждение или бан — его нужно принять на roblox.com.");
			}
			return new CookieException("Роблокс ответил 403 (доступ запрещён). Попробуй ещё раз.", 403);
		case 429:
			return new CookieException("Роблокс просит подождать (слишком много запросов, 429). Попробуй через минуту.", 429);
		default:
			return new CookieException("Роблокс ответил ошибкой " + answer.Status + ". Попробуй ещё раз.", answer.Status);
		}
	}

	public static string MintSession(string cookie)
	{
		ApiAnswer apiAnswer = SendWithCsrf("POST", "https://auth.roblox.com/v1/authentication-ticket", cookie, "");
		if (apiAnswer.Status != 200 || !apiAnswer.Headers.TryGetValue("rbx-authentication-ticket", out var value) || string.IsNullOrEmpty(value))
		{
			throw (apiAnswer.Status == 200) ? new CookieException("Роблокс не выдал билет на вход.") : Explain(apiAnswer);
		}
		string body = Store.Json().Serialize(new Dictionary<string, object> { { "authenticationTicket", value } });
		ApiAnswer apiAnswer2 = SendWithCsrf("POST", "https://auth.roblox.com/v1/authentication-ticket/redeem", null, body);
		if (apiAnswer2.Status != 200)
		{
			throw Explain(apiAnswer2);
		}
		apiAnswer2.Headers.TryGetValue("Set-Cookie", out var value2);
		Match match = Regex.Match(value2 ?? "", "\\.ROBLOSECURITY=([^;]+)");
		if (!match.Success || match.Groups[1].Value.Length < 100)
		{
			throw new CookieException("Роблокс не выдал новую куку.");
		}
		return match.Groups[1].Value;
	}

	public static ModerationInfo GetModeration(string cookie)
	{
		ApiAnswer apiAnswer = Send("GET", "https://usermoderation.roblox.com/v1/not-approved", cookie, null);
		if (apiAnswer.Status != 200)
		{
			throw Explain(apiAnswer);
		}
		IDictionary<string, object> data = Store.Json().DeserializeObject(apiAnswer.Body) as IDictionary<string, object>;
		string text = Store.Str(data, "punishmentTypeDescription");
		if (string.IsNullOrEmpty(text))
		{
			return null;
		}
		ModerationInfo moderationInfo = new ModerationInfo();
		moderationInfo.Type = text;
		moderationInfo.Message = Store.Str(data, "messageToUser");
		moderationInfo.EndUtc = Store.Time(data, "endDate");
		return moderationInfo;
	}

	public static void Reactivate(string cookie)
	{
		ApiAnswer apiAnswer = SendWithCsrf("POST", "https://usermoderation.roblox.com/v1/not-approved/reactivate", cookie, "");
		if (apiAnswer.Status != 200)
		{
			throw Explain(apiAnswer);
		}
	}

	private static ApiAnswer SendWithCsrf(string method, string url, string cookie, string body)
	{
		ApiAnswer apiAnswer = Send(method, url, cookie, body);
		if (apiAnswer.Status == 403 && apiAnswer.Headers.TryGetValue("x-csrf-token", out var value) && !string.IsNullOrEmpty(value))
		{
			apiAnswer = Send(method, url, cookie, body, value);
		}
		return apiAnswer;
	}

	private static ApiAnswer Send(string method, string url, string cookie, string body, string csrf = null)
	{
		HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(url);
		httpWebRequest.Method = method;
		httpWebRequest.Timeout = 15000;
		httpWebRequest.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";
		httpWebRequest.Referer = "https://www.roblox.com/";
		httpWebRequest.AllowAutoRedirect = false;
		if (!string.IsNullOrEmpty(cookie))
		{
			httpWebRequest.Headers["Cookie"] = ".ROBLOSECURITY=" + cookie;
		}
		httpWebRequest.Headers["RBXAuthenticationNegotiation"] = "1";
		if (csrf != null)
		{
			httpWebRequest.Headers["X-CSRF-TOKEN"] = csrf;
		}
		ApiAnswer apiAnswer = new ApiAnswer();
		try
		{
			if (body != null)
			{
				byte[] bytes = Encoding.UTF8.GetBytes(body);
				httpWebRequest.ContentType = "application/json";
				httpWebRequest.ContentLength = bytes.Length;
				using Stream stream = httpWebRequest.GetRequestStream();
				stream.Write(bytes, 0, bytes.Length);
			}
			HttpWebResponse httpWebResponse;
			try
			{
				httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse();
			}
			catch (WebException ex)
			{
				httpWebResponse = ex.Response as HttpWebResponse;
				if (httpWebResponse == null)
				{
					apiAnswer.Body = ex.Message;
					return apiAnswer;
				}
			}
			using (httpWebResponse)
			{
				apiAnswer.Status = (int)httpWebResponse.StatusCode;
				string[] allKeys = httpWebResponse.Headers.AllKeys;
				foreach (string text in allKeys)
				{
					apiAnswer.Headers[text] = httpWebResponse.Headers[text];
				}
				using StreamReader streamReader = new StreamReader(httpWebResponse.GetResponseStream(), Encoding.UTF8);
				apiAnswer.Body = streamReader.ReadToEnd();
			}
		}
		catch (WebException ex2)
		{
			apiAnswer.Body = ex2.Message;
		}
		catch (IOException ex3)
		{
			apiAnswer.Body = ex3.Message;
		}
		return apiAnswer;
	}

	public static Dictionary<string, string> GetHeadshotUrls(IEnumerable<string> userIds)
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>();
		List<string> list = userIds.Where((string id) => !string.IsNullOrEmpty(id)).Distinct().ToList();
		if (list.Count == 0)
		{
			return dictionary;
		}
		HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create("https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds=" + string.Join(",", list) + "&size=150x150&format=Png&isCircular=false");
		httpWebRequest.Timeout = 15000;
		httpWebRequest.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";
		using WebResponse webResponse = httpWebRequest.GetResponse();
		using StreamReader streamReader = new StreamReader(webResponse.GetResponseStream(), Encoding.UTF8);
		IDictionary<string, object> data = Store.Json().DeserializeObject(streamReader.ReadToEnd()) as IDictionary<string, object>;
		foreach (IDictionary<string, object> item in Store.List(data, "data"))
		{
			string text = Store.Str(item, "targetId");
			string value = Store.Str(item, "imageUrl");
			if (!string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(value) && Store.Str(item, "state") == "Completed")
			{
				dictionary[text] = value;
			}
		}
		return dictionary;
	}

	public static byte[] Download(string url)
	{
		using WebClient webClient = new WebClient();
		webClient.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";
		return webClient.DownloadData(url);
	}

	public static bool IsValidLink(string link, out string problem)
	{
		problem = null;
		link = (link ?? "").Trim();
		if (link.Length == 0)
		{
			return true;
		}
		if (Regex.IsMatch(link, "^\\d+$"))
		{
			return true;
		}
		if (!Regex.IsMatch(link, "^https?://(www\\.)?roblox\\.com/", RegexOptions.IgnoreCase))
		{
			problem = "Нужна ссылка roblox.com (VIP/share-ссылка или ссылка на игру) или ID плейса.";
			return false;
		}
		if (Regex.IsMatch(link, "/share\\b", RegexOptions.IgnoreCase))
		{
			if (!Regex.IsMatch(link, "[?&]code=[^&]+", RegexOptions.IgnoreCase))
			{
				problem = "В share-ссылке нет code=…";
				return false;
			}
			Match match = Regex.Match(link, "[?&]type=([^&#]+)", RegexOptions.IgnoreCase);
			if (match.Success && !match.Groups[1].Value.Equals("Server", StringComparison.OrdinalIgnoreCase))
			{
				problem = "Это share-ссылка не на сервер (type=" + match.Groups[1].Value + "). Нужна ссылка приглашения на приватный сервер.";
				return false;
			}
			return true;
		}
		if (Regex.IsMatch(link, "/games/\\d+", RegexOptions.IgnoreCase) || Regex.IsMatch(link, "[?&]placeId=\\d+", RegexOptions.IgnoreCase))
		{
			return true;
		}
		problem = "Не понимаю ссылку: нужна ссылка на игру (/games/…), VIP- или share-ссылка.";
		return false;
	}
}
