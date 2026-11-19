using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AutoRejoinApp;

public class AccountDialog
{
	public enum Outcome
	{
		Cancelled,
		Saved,
		Deleted
	}

	private readonly Window window;

	private readonly TextBox cookieBox;

	private readonly TextBox linkBox;

	private readonly TextBox userBox;

	private readonly PasswordBox passBox;

	private readonly TextBlock passHint;

	private readonly RadioButton modePassword;

	private readonly RadioButton modeCookie;

	private readonly StackPanel passwordPanel;

	private readonly StackPanel cookiePanel;

	private readonly CheckBox mainSwitch;

	private readonly Border resultPanel;

	private readonly TextBlock resultText;

	private readonly TextBlock resultIcon;

	private readonly TextBlock saveText;

	private readonly Button saveButton;

	private readonly Button cancelButton;

	private readonly Button deleteButton;

	private readonly Button reloginButton;

	private readonly VaultAccount existing;

	private readonly Func<string, string, string> duplicateCheck;

	private readonly bool autoAccept;

	private bool forceLogin;

	public Outcome Result { get; private set; }

	public string AccountId { get; private set; }

	public string LoginMode { get; private set; }

	public string Cookie { get; private set; }

	public string Master { get; private set; }

	public string Username { get; private set; }

	public string Password { get; private set; }

	public string Link { get; private set; }

	public bool Main { get; private set; }

	public RobloxUser User { get; private set; }

	public Window WindowForTest => window;

	private bool PasswordMode => modePassword.IsChecked == true;

	public AccountDialog(VaultAccount existing, Func<string, string, string> duplicateCheck, bool suggestMain, bool autoAccept)
	{
		AccountDialog accountDialog = this;
		this.existing = existing;
		this.duplicateCheck = duplicateCheck;
		this.autoAccept = autoAccept;
		AccountId = ((existing != null) ? existing.id : new VaultAccount().id);
		window = Xaml.Load<Window>("AccountDialog.xaml");
		cookieBox = Xaml.Find<TextBox>(window, "CookieBox");
		linkBox = Xaml.Find<TextBox>(window, "LinkBox");
		userBox = Xaml.Find<TextBox>(window, "UserBox");
		passBox = Xaml.Find<PasswordBox>(window, "PassBox");
		passHint = Xaml.Find<TextBlock>(window, "PassHint");
		modePassword = Xaml.Find<RadioButton>(window, "ModePassword");
		modeCookie = Xaml.Find<RadioButton>(window, "ModeCookie");
		passwordPanel = Xaml.Find<StackPanel>(window, "PasswordPanel");
		cookiePanel = Xaml.Find<StackPanel>(window, "CookiePanel");
		mainSwitch = Xaml.Find<CheckBox>(window, "MainSwitch");
		resultPanel = Xaml.Find<Border>(window, "ResultPanel");
		resultText = Xaml.Find<TextBlock>(window, "ResultText");
		resultIcon = Xaml.Find<TextBlock>(window, "ResultIcon");
		saveButton = Xaml.Find<Button>(window, "SaveButton");
		saveText = Xaml.Find<TextBlock>(window, "SaveText");
		cancelButton = Xaml.Find<Button>(window, "CancelButton");
		deleteButton = Xaml.Find<Button>(window, "DeleteButton");
		reloginButton = Xaml.Find<Button>(window, "ReloginButton");
		Border howPanel = Xaml.Find<Border>(window, "HowCookiePanel");
		if (existing != null)
		{
			Xaml.Find<TextBlock>(window, "TitleText").Text = existing.name;
			Xaml.Find<TextBlock>(window, "SubtitleText").Text = "ID " + existing.userId + (existing.UsesPassword ? " · вход по логину и паролю, хранится зашифрованным" : " · кука хранится зашифрованной");
			Xaml.Find<TextBlock>(window, "HeaderIcon").Text = "\ue70f";
			Xaml.Find<TextBlock>(window, "CookieLabel").Text = "НОВАЯ КУКА — ТОЛЬКО ЕСЛИ СТАРАЯ УСТАРЕЛА";
			cookieBox.Tag = "Пусто — оставить текущую куку";
			cookieBox.Height = 64.0;
			linkBox.Text = existing.link ?? "";
			mainSwitch.IsChecked = existing.main;
			deleteButton.Visibility = Visibility.Visible;
			userBox.Text = existing.username ?? "";
			if (existing.UsesPassword && !string.IsNullOrEmpty(existing.password))
			{
				passHint.Visibility = Visibility.Visible;
				reloginButton.Visibility = Visibility.Visible;
			}
		}
		else
		{
			mainSwitch.IsChecked = suggestMain;
		}
		if (existing == null || existing.UsesPassword)
		{
			modePassword.IsChecked = true;
		}
		else
		{
			modeCookie.IsChecked = true;
		}
		modePassword.Checked += delegate
		{
			UpdateMode();
		};
		modeCookie.Checked += delegate
		{
			UpdateMode();
		};
		UpdateMode();
		passBox.PasswordChanged += delegate
		{
			accountDialog.passHint.Visibility = ((accountDialog.passBox.Password.Length != 0 || existing == null || !existing.UsesPassword || string.IsNullOrEmpty(existing.password)) ? Visibility.Collapsed : Visibility.Visible);
		};
		Xaml.Find<Button>(window, "HowCookie").Click += delegate
		{
			howPanel.Visibility = ((howPanel.Visibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible);
		};
		Xaml.Find<Button>(window, "CloseX").Click += delegate
		{
			window.Close();
		};
		cancelButton.Click += delegate
		{
			window.Close();
		};
		deleteButton.Click += delegate
		{
			if (MessageDialog.Ask(accountDialog.window, "Удалить " + existing.name + "?", "AutoRejoin перестанет держать этот аккаунт в игре, кука и пароль удалятся из приложения. Окно Роблокса, если открыто, останется как есть.", "Удалить", MessageDialog.Kind.Danger, "Отмена"))
			{
				accountDialog.Result = Outcome.Deleted;
				accountDialog.window.Close();
			}
		};
		saveButton.Click += async delegate
		{
			await Save();
		};
		reloginButton.Click += async delegate
		{
			forceLogin = true;
			await Save();
		};
		window.KeyDown += delegate(object s, KeyEventArgs e)
		{
			if (e.Key == Key.Escape)
			{
				window.Close();
			}
		};
		Xaml.Find<Border>(window, "Frame").MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
		{
			if (e.OriginalSource is Border || e.OriginalSource is StackPanel || e.OriginalSource is Grid)
			{
				try
				{
					window.DragMove();
				}
				catch
				{
				}
			}
		};
		window.Loaded += delegate
		{
			if (existing != null)
			{
				accountDialog.linkBox.Focus();
			}
			else if (accountDialog.PasswordMode)
			{
				accountDialog.userBox.Focus();
			}
			else
			{
				accountDialog.cookieBox.Focus();
			}
		};
	}

	private void UpdateMode()
	{
		passwordPanel.Visibility = ((!PasswordMode) ? Visibility.Collapsed : Visibility.Visible);
		cookiePanel.Visibility = (PasswordMode ? Visibility.Collapsed : Visibility.Visible);
		if (existing != null)
		{
			saveText.Text = "Сохранить";
		}
		else
		{
			saveText.Text = (PasswordMode ? "Войти и добавить" : "Проверить и добавить");
		}
		resultPanel.Visibility = Visibility.Collapsed;
	}

	public Outcome ShowDialog(Window owner)
	{
		MessageDialog.ShowModal(window, owner);
		return Result;
	}

	private void ShowResult(string text, bool error)
	{
		resultPanel.Visibility = Visibility.Visible;
		resultText.Text = text;
		resultPanel.Background = (error ? Palette.RedSoft : Palette.GreenSoft);
		resultIcon.Text = (error ? "\ue7ba" : "\ue73e");
		resultIcon.Foreground = (error ? Palette.Red : Palette.Green);
	}

	private async Task Save()
	{
		string link = (linkBox.Text ?? "").Trim();
		bool force = forceLogin;
		forceLogin = false;
		if (!RobloxApi.IsValidLink(link, out var problem))
		{
			ShowResult(problem, error: true);
			linkBox.Focus();
			return;
		}
		Link = link;
		Main = mainSwitch.IsChecked == true;
		if (PasswordMode)
		{
			await SavePassword(force);
			return;
		}
		LoginMode = "cookie";
		Username = "";
		Password = "";
		Master = ((existing != null && existing.UsesPassword) ? "" : null);
		string cookie = RobloxApi.CleanCookie(cookieBox.Text);
		if (existing == null && cookie.Length == 0)
		{
			ShowResult("Вставь куку .ROBLOSECURITY этого аккаунта.", error: true);
			cookieBox.Focus();
			return;
		}
		if (cookie.Length == 0)
		{
			Cookie = null;
			Result = Outcome.Saved;
			window.Close();
			return;
		}
		saveButton.IsEnabled = false;
		cancelButton.IsEnabled = false;
		string oldText = saveText.Text;
		saveText.Text = "Проверяю куку…";
		try
		{
			RobloxUser user = await Task.Run(() => RobloxApi.CheckCookie(cookie));
			string duplicate = duplicateCheck(user.Id, (existing == null) ? null : existing.id);
			if (duplicate != null)
			{
				ShowResult("Аккаунт " + user.Name + " уже есть в списке" + ((duplicate != user.Name) ? (" (" + duplicate + ")") : "") + ".", error: true);
			}
			else if (existing == null || string.IsNullOrEmpty(existing.userId) || !(existing.userId != user.Id) || MessageDialog.Ask(window, "Это другой аккаунт", "Новая кука — от аккаунта " + user.Name + ", а не " + existing.name + ". Заменить " + existing.name + " на " + user.Name + "?", "Заменить", MessageDialog.Kind.Warning, "Отмена"))
			{
				User = user;
				Cookie = cookie;
				ShowResult("Аккаунт " + user.Name + ((string.IsNullOrEmpty(user.DisplayName) || user.DisplayName == user.Name) ? "" : (" (" + user.DisplayName + ")")) + " — кука рабочая.", error: false);
				Result = Outcome.Saved;
				await Task.Delay(550);
				window.Close();
			}
		}
		catch (CookieException ex)
		{
			ShowResult(ex.Message, error: true);
		}
		catch (Exception ex2)
		{
			ShowResult("Не получилось проверить куку: " + ex2.Message, error: true);
		}
		finally
		{
			saveButton.IsEnabled = true;
			cancelButton.IsEnabled = true;
			saveText.Text = oldText;
		}
	}

	private async Task SavePassword(bool force)
	{
		string username = (userBox.Text ?? "").Trim();
		string password = passBox.Password ?? "";
		bool hadPassword = existing != null && existing.UsesPassword && !string.IsNullOrEmpty(existing.password);
		if (username.Length == 0)
		{
			ShowResult("Введи логин аккаунта Роблокса (ник, почту или телефон).", error: true);
			userBox.Focus();
			return;
		}
		if (password.Length == 0 && !hadPassword)
		{
			ShowResult("Введи пароль.", error: true);
			passBox.Focus();
			return;
		}
		if (password.Length == 0)
		{
			password = existing.password;
		}
		LoginMode = "password";
		Username = username;
		Password = password;
		if (!force && existing != null && existing.UsesPassword && !(username != existing.username) && !(password != existing.password) && !string.IsNullOrEmpty(existing.cookie))
		{
			Cookie = null;
			Master = null;
			Result = Outcome.Saved;
			window.Close();
			return;
		}
		saveButton.IsEnabled = false;
		cancelButton.IsEnabled = false;
		reloginButton.IsEnabled = false;
		string oldText = saveText.Text;
		saveText.Text = (RobloxLogin.Busy ? "Жду другой вход…" : "Вхожу в Роблокс…");
		ShowResult("Открываю окно входа Роблокса. Логин и пароль AutoRejoin введёт сам; если Роблокс попросит капчу или код — введи их там.", error: false);
		try
		{
			LoginResult login = await RobloxLogin.Run(new LoginRequest
			{
				AccountId = AccountId,
				Name = ((existing != null) ? existing.name : username),
				Username = username,
				Password = password,
				Visible = true,
				AutoAccept = autoAccept
			}, window);
			if (!login.Ok)
			{
				ShowResult(login.Cancelled ? "Вход отменён — аккаунт не сохранён." : ("Не получилось войти: " + login.Error), error: true);
				return;
			}
			RobloxUser user = login.User;
			string duplicate = duplicateCheck(user.Id, (existing == null) ? null : existing.id);
			if (duplicate != null)
			{
				ShowResult("Аккаунт " + user.Name + " уже есть в списке" + ((duplicate != user.Name) ? (" (" + duplicate + ")") : "") + ".", error: true);
			}
			else
			{
				if (existing != null && !string.IsNullOrEmpty(existing.userId) && existing.userId != user.Id && !MessageDialog.Ask(window, "Это другой аккаунт", "Вход выполнен в " + user.Name + ", а не в " + existing.name + ". Заменить " + existing.name + " на " + user.Name + "?", "Заменить", MessageDialog.Kind.Warning, "Отмена"))
				{
					return;
				}
				saveText.Text = "Получаю куку…";
				string working;
				try
				{
					working = await Task.Run(() => RobloxApi.MintSession(login.Cookie));
				}
				catch (Exception error)
				{
					App.LogError("отдельная сессия после входа", error);
					working = login.Cookie;
				}
				User = user;
				Master = login.Cookie;
				Cookie = working;
				ShowResult("Вошёл в " + user.Name + ((string.IsNullOrEmpty(user.DisplayName) || user.DisplayName == user.Name) ? "" : (" (" + user.DisplayName + ")")) + " — кука получена, дальше AutoRejoin обновляет её сам.", error: false);
				Result = Outcome.Saved;
				await Task.Delay(700);
				window.Close();
			}
		}
		catch (Exception ex)
		{
			App.LogError("вход из окна аккаунта", ex);
			ShowResult("Не получилось войти: " + ex.Message, error: true);
		}
		finally
		{
			saveButton.IsEnabled = true;
			cancelButton.IsEnabled = true;
			reloginButton.IsEnabled = true;
			saveText.Text = oldText;
		}
	}
}
