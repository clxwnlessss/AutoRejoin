using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace AutoRejoinApp;

public static class Store
{
	private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AutoRejoin.vault.v1");

	private static readonly UTF8Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

	public static bool ConfigExists => File.Exists(AppPaths.ConfigFile);

	public static JavaScriptSerializer Json()
	{
		JavaScriptSerializer javaScriptSerializer = new JavaScriptSerializer();
		javaScriptSerializer.MaxJsonLength = int.MaxValue;
		return javaScriptSerializer;
	}

	public static AppConfig LoadConfig()
	{
		AppConfig appConfig = null;
		try
		{
			if (File.Exists(AppPaths.ConfigFile))
			{
				appConfig = Json().Deserialize<AppConfig>(File.ReadAllText(AppPaths.ConfigFile, Encoding.UTF8));
			}
		}
		catch (Exception error)
		{
			App.LogError(L.T("config.json не читается, беру настройки по умолчанию"), error);
			try
			{
				File.Copy(AppPaths.ConfigFile, AppPaths.ConfigFile + ".broken", overwrite: true);
			}
			catch
			{
			}
		}
		if (appConfig == null)
		{
			appConfig = new AppConfig();
		}
		appConfig.Normalize();
		return appConfig;
	}

	public static void SaveConfig(AppConfig config)
	{
		WriteAtomic(AppPaths.ConfigFile, Utf8.GetBytes(Pretty(Json().Serialize(config))));
	}

	public static Vault LoadVault()
	{
		if (!File.Exists(AppPaths.VaultFile))
		{
			return new Vault();
		}
		byte[] bytes = ProtectedData.Unprotect(File.ReadAllBytes(AppPaths.VaultFile), Entropy, DataProtectionScope.CurrentUser);
		Vault vault = Json().Deserialize<Vault>(Encoding.UTF8.GetString(bytes)) ?? new Vault();
		if (vault.accounts == null)
		{
			vault.accounts = new List<VaultAccount>();
		}
		if (vault.potassium == null)
		{
			vault.potassium = new PotassiumLogin();
		}
		foreach (VaultAccount account in vault.accounts)
		{
			if (string.IsNullOrEmpty(account.id))
			{
				account.id = Guid.NewGuid().ToString("N").Substring(0, 12);
			}
			if (account.link == null)
			{
				account.link = "";
			}
			if (account.name == null)
			{
				account.name = "";
			}
			if (account.userId == null)
			{
				account.userId = "";
			}
			if (account.login != "password")
			{
				account.login = "cookie";
			}
			if (account.username == null)
			{
				account.username = "";
			}
			if (account.password == null)
			{
				account.password = "";
			}
			if (account.master == null)
			{
				account.master = "";
			}
			if (account.cookieAt == null)
			{
				account.cookieAt = "";
			}
			if (account.cookie == null)
			{
				account.cookie = "";
			}
		}
		return vault;
	}

	public static void SaveVault(Vault vault)
	{
		byte[] bytes = Utf8.GetBytes(Json().Serialize(vault));
		WriteAtomic(AppPaths.VaultFile, ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser));
	}

	private static void WriteAtomic(string path, byte[] data)
	{
		string text = path + ".tmp";
		File.WriteAllBytes(text, data);
		int num = 1;
		while (true)
		{
			try
			{
				if (File.Exists(path))
				{
					File.Replace(text, path, null, ignoreMetadataErrors: true);
				}
				else
				{
					File.Move(text, path);
				}
				break;
			}
			catch (IOException)
			{
				if (num >= 10)
				{
					throw;
				}
				Thread.Sleep(50);
			}
			catch (UnauthorizedAccessException)
			{
				if (num >= 10)
				{
					throw;
				}
				Thread.Sleep(50);
			}
			num++;
		}
	}

	public static string Pretty(string json)
	{
		StringBuilder stringBuilder = new StringBuilder();
		int num = 0;
		bool flag = false;
		bool flag2 = false;
		for (int i = 0; i < json.Length; i++)
		{
			char c = json[i];
			if (flag)
			{
				stringBuilder.Append(c);
				if (flag2)
				{
					flag2 = false;
					continue;
				}
				switch (c)
				{
				case '\\':
					flag2 = true;
					break;
				case '"':
					flag = false;
					break;
				}
				continue;
			}
			switch (c)
			{
			case '"':
				flag = true;
				stringBuilder.Append(c);
				break;
			case '[':
			case '{':
				if (i + 1 < json.Length && (json[i + 1] == '}' || json[i + 1] == ']'))
				{
					stringBuilder.Append(c).Append(json[i + 1]);
					i++;
				}
				else
				{
					stringBuilder.Append(c).Append("\r\n").Append(' ', ++num * 2);
				}
				break;
			case ']':
			case '}':
				stringBuilder.Append("\r\n").Append(' ', --num * 2).Append(c);
				break;
			case ',':
				stringBuilder.Append(",\r\n").Append(' ', num * 2);
				break;
			case ':':
				stringBuilder.Append(": ");
				break;
			default:
				stringBuilder.Append(c);
				break;
			}
		}
		return stringBuilder.ToString();
	}

	public static string Str(IDictionary<string, object> data, string key)
	{
		if (data == null || !data.TryGetValue(key, out var value) || value == null)
		{
			return null;
		}
		return Convert.ToString(value, CultureInfo.InvariantCulture);
	}

	public static double? Num(IDictionary<string, object> data, string key)
	{
		if (data == null || !data.TryGetValue(key, out var value) || value == null)
		{
			return null;
		}
		try
		{
			return Convert.ToDouble(value, CultureInfo.InvariantCulture);
		}
		catch
		{
			return null;
		}
	}

	public static bool Flag(IDictionary<string, object> data, string key)
	{
		if (data == null || !data.TryGetValue(key, out var value) || value == null)
		{
			return false;
		}
		if (value is bool)
		{
			return (bool)value;
		}
		return string.Equals(Convert.ToString(value), "true", StringComparison.OrdinalIgnoreCase);
	}

	public static IDictionary<string, object> Obj(IDictionary<string, object> data, string key)
	{
		if (data == null || !data.TryGetValue(key, out var value))
		{
			return null;
		}
		return value as IDictionary<string, object>;
	}

	public static IEnumerable<IDictionary<string, object>> List(IDictionary<string, object> data, string key)
	{
		if (data == null || !data.TryGetValue(key, out var value) || value == null || !(value is IEnumerable items))
		{
			yield break;
		}
		foreach (object item in items)
		{
			if (item is IDictionary<string, object> dictionary)
			{
				yield return dictionary;
			}
		}
	}

	public static DateTime? Time(IDictionary<string, object> data, string key)
	{
		string text = Str(data, key);
		if (string.IsNullOrEmpty(text))
		{
			return null;
		}
		if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result))
		{
			return result.ToUniversalTime();
		}
		return null;
	}
}
