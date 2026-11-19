using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AutoRejoinApp;

public class AuthManager
{
	private readonly MainController main;

	private readonly HashSet<string> busy = new HashSet<string>();

	private readonly Dictionary<string, DateTime> nextTry = new Dictionary<string, DateTime>();

	public event Action<string> StateChanged;

	public AuthManager(MainController main)
	{
		this.main = main;
	}

	public bool IsBusy(string key)
	{
		return busy.Contains(key);
	}

	public async void OnEngineRequest(string key, string reason, string detail)
	{
		VaultAccount entry = main.FindAccount(key);
		if (entry != null && !busy.Contains(key) && (!nextTry.TryGetValue(key, out var next) || !(DateTime.UtcNow < next)))
		{
			await Refresh(entry, reason, detail, interactive: false);
		}
	}

	public async Task<bool> Refresh(VaultAccount entry, string reason, string detail, bool interactive)
	{
		string key = entry.id;
		if (busy.Contains(key))
		{
			return false;
		}
		busy.Add(key);
		Raise(key);
		try
		{
			bool flag = (entry.UsesPassword ? (await RefreshByPassword(entry, reason, interactive)) : (await RefreshCookieAccount(entry, reason, detail)));
			bool done = flag;
			if (done)
			{
				nextTry.Remove(key);
			}
			return done;
		}
		catch (Exception ex)
		{
			App.LogError("обновление куки", ex);
			main.AppLog(entry.name, "не получилось обновить куку: " + ex.Message, "Red");
			Later(key, 5);
			return false;
		}
		finally
		{
			busy.Remove(key);
			Raise(key);
		}
	}

	private async Task<bool> RefreshByPassword(VaultAccount entry, string reason, bool interactive)
	{
		string master = entry.master;
		RobloxUser masterUser = null;
		bool masterModerated = false;
		CookieException masterError = null;
		if (!string.IsNullOrEmpty(master))
		{
			try
			{
				masterUser = await Task.Run(() => RobloxApi.CheckCookie(master));
			}
			catch (ModeratedException)
			{
				masterModerated = true;
			}
			catch (CookieException ex2)
			{
				masterError = ex2;
			}
		}
		if (masterError != null && masterError.Status != 401)
		{
			main.AppLog(entry.name, "не получилось обновить куку: " + masterError.Message + " Повторю через 2 мин.", "DarkYellow");
			Later(entry.id, 2);
			return false;
		}
		if (masterUser != null && (string.IsNullOrEmpty(entry.userId) || entry.userId == masterUser.Id))
		{
			Apply(entry, await Task.Run(() => RobloxApi.MintSession(master)), master, masterUser, (reason == "moderated") ? "Роблокс снова пускает аккаунт — взял новую куку из сохранённого входа." : "кука устарела — взял новую из сохранённого входа (без пароля и капчи).");
			return true;
		}
		if (masterModerated && await TryAccept(entry, master))
		{
			string fresh = await Task.Run(() => RobloxApi.MintSession(master));
			Apply(user: await Task.Run(() => RobloxApi.CheckCookie(fresh)), entry: entry, cookie: fresh, master: master, message: "предупреждение принято — взял новую куку из сохранённого входа.");
			return true;
		}
		main.AppLog(entry.name, masterModerated ? "Роблокс не пускает аккаунт (предупреждение) — открываю вход, чтобы его принять." : "кука устарела — вхожу заново по логину и паролю…", masterModerated ? "DarkYellow" : "Cyan");
		LoginResult login = await RobloxLogin.Run(new LoginRequest
		{
			AccountId = entry.id,
			Name = entry.name,
			Username = entry.username,
			Password = entry.password,
			ExpectedUserId = entry.userId,
			Visible = interactive,
			AutoAccept = main.Config.autoAcceptWarnings,
			Reason = (masterModerated ? "Роблокс не пускает аккаунт (403)." : "Кука устарела.")
		}, main.Window);
		if (!login.Ok)
		{
			main.AppLog(entry.name, login.Cancelled ? "вход отменён — без него аккаунт не зайдёт. Попробую снова через 30 мин (или нажми ↻ у аккаунта)." : ("не получилось войти: " + login.Error + ". Попробую снова через 10 мин."), "Red");
			Later(entry.id, login.Cancelled ? 30 : 10);
			return false;
		}
		string working;
		try
		{
			string browser = login.Cookie;
			working = await Task.Run(() => RobloxApi.MintSession(browser));
		}
		catch (Exception error)
		{
			App.LogError("отдельная сессия после входа", error);
			working = login.Cookie;
		}
		Apply(entry, working, login.Cookie, login.User, "вошёл по логину и паролю — кука обновлена.");
		return true;
	}

	private async Task<bool> RefreshCookieAccount(VaultAccount entry, string reason, string detail)
	{
		if (reason == "moderated" && await TryAccept(entry, entry.cookie))
		{
			main.SendEngine("rejoin " + entry.id);
			return true;
		}
		main.ShowAuthBanner(entry, reason, detail);
		Later(entry.id, 30);
		return false;
	}

	private async Task<bool> TryAccept(VaultAccount entry, string cookie)
	{
		if (!main.Config.autoAcceptWarnings || string.IsNullOrEmpty(cookie))
		{
			return false;
		}
		ModerationInfo info;
		try
		{
			info = await Task.Run(() => RobloxApi.GetModeration(cookie));
		}
		catch (Exception ex)
		{
			main.AppLog(entry.name, "не получилось узнать, что за модерация: " + ex.Message, "DarkYellow");
			return false;
		}
		if (info == null)
		{
			return false;
		}
		if (!info.CanReactivate)
		{
			main.AppLog(entry.name, "Роблокс: " + info.Describe() + " — снять это нельзя, аккаунт ждёт конца срока.", "Red");
			return false;
		}
		await Task.Run(delegate
		{
			RobloxApi.Reactivate(cookie);
		});
		await Task.Run(() => RobloxApi.CheckCookie(cookie));
		main.AppLog(entry.name, "принял " + info.Describe(), "DarkYellow");
		return true;
	}

	private void Apply(VaultAccount entry, string cookie, string master, RobloxUser user, string message)
	{
		entry.cookie = cookie;
		entry.master = master ?? "";
		entry.cookieAt = DateTime.UtcNow.ToString("o");
		if (user != null)
		{
			entry.userId = user.Id;
			entry.name = user.Name;
		}
		main.AccountRefreshed(entry, message);
	}

	private void Later(string key, int minutes)
	{
		nextTry[key] = DateTime.UtcNow.AddMinutes(minutes);
	}

	public void Forget(string key)
	{
		nextTry.Remove(key);
	}

	private void Raise(string key)
	{
		this.StateChanged?.Invoke(key);
	}
}
