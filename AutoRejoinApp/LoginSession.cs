using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace AutoRejoinApp;

internal class LoginSession
{
	private const string LoginUrl = "https://www.roblox.com/login";

	private const string NotApprovedUrl = "https://www.roblox.com/not-approved";

	private const string ProbeScript = "(function(){\r\n function vis(e){ if(!e) return false; var r=e.getBoundingClientRect(); return r.width>0 && r.height>0; }\r\n function q(s){ var l=document.querySelectorAll(s); for(var i=0;i<l.length;i++){ if(vis(l[i])) return l[i]; } return null; }\r\n var u=q('#login-username,input[name=\"username\"],input[autocomplete=\"username\"]');\r\n var p=q('#login-password,input[type=\"password\"]');\r\n var captcha=!!q('iframe[src*=\"arkoselabs\"],iframe[src*=\"funcaptcha\"],iframe[src*=\"hcaptcha\"],iframe[title*=\"captcha\" i],#arkose-iframe,[id*=\"captcha\" i],[class*=\"captcha\" i]');\r\n var code=!!q('#two-step-verification-code-input,input[autocomplete=\"one-time-code\"],.verification-code-input,input[id*=\"verification\" i],input[id*=\"otp\" i]');\r\n var err=q('#login-form-error,.login-error,.form-has-error .text-error,.alert-warning,[class*=\"error-message\"],[role=\"alert\"]');\r\n return {path:location.pathname.toLowerCase(), user:!!u, userEmpty:!!u && !u.value, pass:!!p, passEmpty:!!p && !p.value,\r\n   captcha:captcha, code:code, error: err ? (err.innerText||'').trim().slice(0,200) : ''};\r\n})()";

	private readonly LoginRequest request;

	private readonly Window owner;

	private Window window;

	private WebView2 web;

	private TextBlock statusText;

	private TextBlock statusIcon;

	private Border statusBar;

	private bool revealed;

	private bool closedByUser;

	private bool finished;

	private int submits;

	private DateTime started;

	private DateTime lastSubmitAt = DateTime.MinValue;

	private DateTime revealedAt = DateTime.MinValue;

	private DateTime lastLoginNav = DateTime.MinValue;

	private DateTime? waitingUserSince;

	private string lastCookieTried;

	private DateTime lastCookieCheck = DateTime.MinValue;

	private bool moderationShown;

	private bool WaitingForUser => waitingUserSince.HasValue;

	public LoginSession(LoginRequest request, Window owner)
	{
		this.request = request;
		this.owner = owner;
	}

	public async Task<LoginResult> RunAsync()
	{
		started = DateTime.UtcNow;
		BuildWindow();
		try
		{
			CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, AppPaths.BrowserProfile(request.AccountId), new CoreWebView2EnvironmentOptions
			{
				Language = "ru-RU"
			});
			web = new WebView2();
			Xaml.Find<Border>(window, "BrowserHost").Child = web;
			await web.EnsureCoreWebView2Async(environment);
			CoreWebView2 core = web.CoreWebView2;
			core.Settings.IsStatusBarEnabled = false;
			core.Settings.AreDevToolsEnabled = false;
			core.Settings.IsPasswordAutosaveEnabled = false;
			core.Settings.IsGeneralAutofillEnabled = false;
			core.NewWindowRequested += delegate(object s, CoreWebView2NewWindowRequestedEventArgs e)
			{
				e.Handled = true;
				try
				{
					core.Navigate(e.Uri);
				}
				catch
				{
				}
			};
			core.Navigate("https://www.roblox.com/login");
			lastLoginNav = DateTime.UtcNow;
			SetStatus(string.IsNullOrEmpty(request.Reason) ? "Открываю страницу входа Роблокса…" : (request.Reason + " Открываю страницу входа…"), Palette.Accent, "\ue895");
			while (!closedByUser)
			{
				await Task.Delay(700);
				if (closedByUser)
				{
					break;
				}
				LoginResult result = await Step(core);
				if (result != null)
				{
					return result;
				}
			}
			return new LoginResult
			{
				Cancelled = true,
				Error = "вход отменён"
			};
		}
		catch (Exception ex)
		{
			App.LogError("вход по логину и паролю", ex);
			LoginResult loginResult = new LoginResult();
			loginResult.Error = "встроенный браузер: " + ex.Message;
			return loginResult;
		}
		finally
		{
			finished = true;
			try
			{
				if (web != null)
				{
					web.Dispose();
				}
			}
			catch
			{
			}
			try
			{
				window.Close();
			}
			catch
			{
			}
		}
	}

	private async Task<LoginResult> Step(CoreWebView2 core)
	{
		DateTime now = DateTime.UtcNow;
		string cookie = await ReadCookie(core);
		if (cookie != null && (cookie != lastCookieTried || (now - lastCookieCheck).TotalSeconds >= 3.0))
		{
			lastCookieTried = cookie;
			lastCookieCheck = now;
			LoginResult checkedResult = await CheckCookie(core, cookie);
			if (checkedResult != null)
			{
				return checkedResult;
			}
		}
		IDictionary<string, object> page = await Probe(core);
		if (page == null)
		{
			return null;
		}
		string path = Store.Str(page, "path") ?? "";
		string error = Store.Str(page, "error") ?? "";
		bool onLogin = path.StartsWith("/login");
		if (cookie == null && submits > 0 && Store.Flag(page, "captcha"))
		{
			Reveal("Роблокс просит подтвердить, что ты не робот. Пройди проверку в окне ниже — куку AutoRejoin заберёт сам.");
		}
		else if (cookie == null && submits > 0 && Store.Flag(page, "code"))
		{
			Reveal("Роблокс просит код подтверждения (из почты, SMS или приложения 2FA). Введи его в окне ниже — дальше AutoRejoin сам.");
		}
		else if (cookie == null && onLogin && error.Length > 0 && submits > 0)
		{
			Reveal("Роблокс не пустил: «" + error + "». Проверь логин и пароль в окне ниже (или в настройках аккаунта в AutoRejoin).");
		}
		if (cookie == null && onLogin && !WaitingForUser)
		{
			bool hasUser = Store.Flag(page, "user");
			bool hasPass = Store.Flag(page, "pass");
			bool needUser = hasUser && Store.Flag(page, "userEmpty");
			bool needPass = hasPass && Store.Flag(page, "passEmpty");
			bool canSubmit = submits < 2 && (now - lastSubmitAt).TotalSeconds > 6.0;
			if ((needUser || needPass || (hasUser && hasPass && submits == 0)) && canSubmit && error.Length == 0)
			{
				SetStatus("Ввожу логин и пароль…", Palette.Accent, "\ue895");
				await core.ExecuteScriptAsync(FillScript(request.Username, request.Password));
				lastSubmitAt = now;
				submits++;
				return null;
			}
		}
		if (cookie == null && !onLogin && !path.StartsWith("/not-approved") && !revealed && (now - lastLoginNav).TotalSeconds > 8.0)
		{
			core.Navigate("https://www.roblox.com/login");
			lastLoginNav = now;
			return null;
		}
		if (cookie == null && submits > 0 && (now - lastSubmitAt).TotalSeconds > 9.0 && !WaitingForUser)
		{
			Reveal("Вход не завершился сам — Роблокс что-то спрашивает. Закончи вход в окне ниже, куку AutoRejoin заберёт сам.");
		}
		if (!revealed && (now - started).TotalSeconds > 40.0)
		{
			Reveal("Вход идёт дольше обычного — посмотри, что показывает Роблокс.");
		}
		if (revealed && (now - revealedAt).TotalMinutes > 60.0)
		{
			return new LoginResult
			{
				Error = "вход не завершён за час"
			};
		}
		return null;
	}

	private async Task<LoginResult> CheckCookie(CoreWebView2 core, string cookie)
	{
		SetStatus("Вход прошёл — проверяю куку…", Palette.Accent, "\ue895");
		RobloxUser user = null;
		bool moderated = false;
		CookieException failure = null;
		try
		{
			user = await Task.Run(() => RobloxApi.CheckCookie(cookie));
		}
		catch (ModeratedException)
		{
			moderated = true;
		}
		catch (CookieException ex2)
		{
			failure = ex2;
		}
		if (user != null)
		{
			if (!string.IsNullOrEmpty(request.ExpectedUserId) && user.Id != request.ExpectedUserId)
			{
				core.CookieManager.DeleteCookiesWithDomainAndPath(".ROBLOSECURITY", ".roblox.com", "/");
				core.Navigate("https://www.roblox.com/login");
				lastLoginNav = DateTime.UtcNow;
				submits = 0;
				waitingUserSince = null;
				Reveal("Это другой аккаунт (" + user.Name + "). Войди в " + request.Name + ".");
				return null;
			}
			SetStatus("Готово: " + user.Name + ".", Palette.Green, "\ue73e");
			LoginResult loginResult = new LoginResult();
			loginResult.Ok = true;
			loginResult.Cookie = cookie;
			loginResult.User = user;
			return loginResult;
		}
		if (moderated)
		{
			if (request.AutoAccept)
			{
				LoginResult accepted = await TryAccept(cookie);
				if (accepted != null)
				{
					return accepted;
				}
			}
			if (!moderationShown)
			{
				moderationShown = true;
				core.Navigate("https://www.roblox.com/not-approved");
			}
			Reveal("Роблокс показывает предупреждение для этого аккаунта (из-за него была ошибка 403). Прочитай его и нажми «I agree» / «Reactivate» — AutoRejoin продолжит сам.");
			return null;
		}
		if (failure != null && failure.Status == 401)
		{
			core.CookieManager.DeleteCookiesWithDomainAndPath(".ROBLOSECURITY", ".roblox.com", "/");
			core.Navigate("https://www.roblox.com/login");
			lastLoginNav = DateTime.UtcNow;
			SetStatus("Старая сессия устарела — вхожу заново…", Palette.Accent, "\ue895");
			return null;
		}
		SetStatus(((failure != null) ? failure.Message : "Роблокс не ответил.") + " Пробую ещё раз…", Palette.Yellow, "\ue7ba");
		lastCookieTried = null;
		return null;
	}

	private static async Task<LoginResult> TryAccept(string cookie)
	{
		try
		{
			ModerationInfo info = await Task.Run(() => RobloxApi.GetModeration(cookie));
			if (info == null || !info.CanReactivate)
			{
				return null;
			}
			await Task.Run(delegate
			{
				RobloxApi.Reactivate(cookie);
			});
			RobloxUser user = await Task.Run(() => RobloxApi.CheckCookie(cookie));
			return new LoginResult
			{
				Ok = true,
				Cookie = cookie,
				User = user
			};
		}
		catch (Exception error)
		{
			App.LogError("принятие предупреждения при входе", error);
			return null;
		}
	}

	private static async Task<string> ReadCookie(CoreWebView2 core)
	{
		int num = default(int);
		_ = num;
		_ = 0;
		try
		{
			return (await core.CookieManager.GetCookiesAsync("https://www.roblox.com")).FirstOrDefault((CoreWebView2Cookie c) => c.Name == ".ROBLOSECURITY" && !string.IsNullOrEmpty(c.Value))?.Value;
		}
		catch
		{
			return null;
		}
	}

	private static async Task<IDictionary<string, object>> Probe(CoreWebView2 core)
	{
		int num = default(int);
		_ = num;
		_ = 0;
		try
		{
			string json = await core.ExecuteScriptAsync("(function(){\r\n function vis(e){ if(!e) return false; var r=e.getBoundingClientRect(); return r.width>0 && r.height>0; }\r\n function q(s){ var l=document.querySelectorAll(s); for(var i=0;i<l.length;i++){ if(vis(l[i])) return l[i]; } return null; }\r\n var u=q('#login-username,input[name=\"username\"],input[autocomplete=\"username\"]');\r\n var p=q('#login-password,input[type=\"password\"]');\r\n var captcha=!!q('iframe[src*=\"arkoselabs\"],iframe[src*=\"funcaptcha\"],iframe[src*=\"hcaptcha\"],iframe[title*=\"captcha\" i],#arkose-iframe,[id*=\"captcha\" i],[class*=\"captcha\" i]');\r\n var code=!!q('#two-step-verification-code-input,input[autocomplete=\"one-time-code\"],.verification-code-input,input[id*=\"verification\" i],input[id*=\"otp\" i]');\r\n var err=q('#login-form-error,.login-error,.form-has-error .text-error,.alert-warning,[class*=\"error-message\"],[role=\"alert\"]');\r\n return {path:location.pathname.toLowerCase(), user:!!u, userEmpty:!!u && !u.value, pass:!!p, passEmpty:!!p && !p.value,\r\n   captcha:captcha, code:code, error: err ? (err.innerText||'').trim().slice(0,200) : ''};\r\n})()");
			return Store.Json().DeserializeObject(json) as IDictionary<string, object>;
		}
		catch
		{
			return null;
		}
	}

	private static string FillScript(string username, string password)
	{
		return "(function(U,P){\r\n function vis(e){ if(!e) return false; var r=e.getBoundingClientRect(); return r.width>0 && r.height>0; }\r\n function q(s){ var l=document.querySelectorAll(s); for(var i=0;i<l.length;i++){ if(vis(l[i])) return l[i]; } return null; }\r\n var setter=Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set;\r\n function put(e,v){ e.focus(); setter.call(e,v); e.dispatchEvent(new Event('input',{bubbles:true})); e.dispatchEvent(new Event('change',{bubbles:true})); }\r\n var u=q('#login-username,input[name=\"username\"],input[autocomplete=\"username\"]');\r\n var p=q('#login-password,input[type=\"password\"]');\r\n if(u && !u.value) put(u,U);\r\n if(p && !p.value) put(p,P);\r\n var b=q('#login-button,button[type=\"submit\"]');\r\n if(b) setTimeout(function(){ b.click(); }, 300);\r\n return 'ok';\r\n})(" + JsString(username) + "," + JsString(password) + ")";
	}

	private static string JsString(string value)
	{
		return Store.Json().Serialize(value ?? "").Replace("\u2028", "\\u2028")
			.Replace("\u2029", "\\u2029");
	}

	private void BuildWindow()
	{
		window = Xaml.Load<Window>("LoginWindow.xaml");
		BitmapFrame bitmapFrame = Xaml.AppIcon();
		if (bitmapFrame != null)
		{
			window.Icon = bitmapFrame;
		}
		statusText = Xaml.Find<TextBlock>(window, "StatusText");
		statusIcon = Xaml.Find<TextBlock>(window, "StatusIcon");
		statusBar = Xaml.Find<Border>(window, "StatusBar");
		Xaml.Find<TextBlock>(window, "TitleText").Text = "Вход в Roblox — " + (string.IsNullOrEmpty(request.Name) ? request.Username : request.Name);
		Xaml.Find<TextBlock>(window, "SubtitleText").Text = "Логин и пароль AutoRejoin вводит сам. Куку заберёт сам, как только вход пройдёт.";
		Action cancel = delegate
		{
			if (!finished)
			{
				closedByUser = true;
			}
		};
		Xaml.Find<Button>(window, "CancelButton").Click += delegate
		{
			cancel();
		};
		Xaml.Find<Button>(window, "CloseButton").Click += delegate
		{
			cancel();
		};
		Xaml.Find<Button>(window, "MinButton").Click += delegate
		{
			window.WindowState = WindowState.Minimized;
		};
		window.Closing += delegate(object s, CancelEventArgs e)
		{
			if (!finished)
			{
				e.Cancel = true;
				closedByUser = true;
			}
		};
		window.KeyDown += delegate(object s, KeyEventArgs e)
		{
			if (e.Key == Key.Escape && revealed)
			{
				cancel();
			}
		};
		if (request.Visible)
		{
			revealed = true;
			revealedAt = DateTime.UtcNow;
			if (owner != null && owner.IsVisible)
			{
				window.Owner = owner;
				window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
			}
			window.Show();
			window.Activate();
		}
		else
		{
			window.WindowStartupLocation = WindowStartupLocation.Manual;
			window.Left = -32000.0;
			window.Top = -32000.0;
			window.ShowInTaskbar = false;
			window.ShowActivated = false;
			window.Show();
		}
	}

	private void Reveal(string text)
	{
		SetStatus(text, Palette.Yellow, "\ue7ba");
		if (!waitingUserSince.HasValue)
		{
			waitingUserSince = DateTime.UtcNow;
		}
		if (revealed)
		{
			if (window.WindowState == WindowState.Minimized)
			{
				window.WindowState = WindowState.Normal;
			}
			return;
		}
		revealed = true;
		revealedAt = DateTime.UtcNow;
		Rect workArea = SystemParameters.WorkArea;
		window.Left = workArea.Left + Math.Max(0.0, (workArea.Width - window.Width) / 2.0);
		window.Top = workArea.Top + Math.Max(0.0, (workArea.Height - window.Height) / 2.0);
		window.ShowInTaskbar = true;
		window.Activate();
		window.Topmost = true;
		window.Topmost = false;
		RobloxLogin.RaiseNeedsUser(string.IsNullOrEmpty(request.Name) ? request.Username : request.Name, text);
	}

	private void SetStatus(string text, Brush color, string glyph)
	{
		if (statusText != null)
		{
			statusText.Text = text;
			statusIcon.Text = glyph;
			statusIcon.Foreground = color;
			statusBar.Background = ((color == Palette.Yellow) ? Palette.YellowSoft : ((color == Palette.Green) ? Palette.GreenSoft : Palette.AccentSoft));
		}
	}
}
