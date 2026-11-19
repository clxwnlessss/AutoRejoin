using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace AutoRejoinApp;

public class MainController
{
	private readonly Window window;

	private readonly EngineHost engine;

	private readonly AuthManager auth;

	private readonly Tray tray;

	private readonly bool autostart;

	private AppConfig config;

	private Vault vault;

	private readonly ObservableCollection<AccountCard> cards = new ObservableCollection<AccountCard>();

	private readonly List<LogLine> logAll = new List<LogLine>();

	private readonly ObservableCollection<LogLine> logShown = new ObservableCollection<LogLine>();

	private readonly DispatcherTimer clock;

	private readonly DispatcherTimer saveTimer;

	private readonly DispatcherTimer restartTimer;

	private string phase = "stopped";

	private IDictionary<string, object> lastStatus;

	private DateTime? rebootAtUtc;

	private string rebootWhy;

	private bool quitting;

	private bool engineWanted;

	private bool scheduleLoading;

	private string bannerKind;

	private Action bannerAction;

	private readonly List<DateTime> crashes = new List<DateTime>();

	private ScrollViewer logScroll;

	private SettingsPage settingsPage;

	private readonly Border root;

	private readonly Border banner;

	private readonly Border accountsEmpty;

	private readonly Border scheduleWarn;

	private readonly TextBlock engineTitle;

	private readonly TextBlock engineSub;

	private readonly TextBlock engineButtonIcon;

	private readonly TextBlock engineButtonText;

	private readonly TextBlock accountsSub;

	private readonly TextBlock bannerText;

	private readonly TextBlock bannerIcon;

	private readonly TextBlock statGameValue;

	private readonly TextBlock statGameSub;

	private readonly TextBlock statMemValue;

	private readonly TextBlock statMemSub;

	private readonly TextBlock statPotValue;

	private readonly TextBlock statPotSub;

	private readonly TextBlock statSchedValue;

	private readonly TextBlock statSchedSub;

	private readonly TextBlock scheduleNext;

	private readonly TextBlock scheduleNextSub;

	private readonly TextBlock scheduleWarnText;

	private readonly TextBlock zoneHint;

	private readonly Ellipse engineDot;

	private readonly Ellipse engineGlow;

	private readonly Button engineButton;

	private readonly Button bannerActionButton;

	private readonly Button maxButton;

	private readonly Button scheduleWarnFix;

	private readonly ItemsControl accountsList;

	private readonly ListBox logList;

	private readonly ComboBox logFilter;

	private readonly ComboBox zoneBox;

	private readonly CheckBox scheduleSwitch;

	private readonly StackPanel scheduleBody;

	private readonly TextBox hourBox;

	private readonly TextBox minuteBox;

	private readonly RadioButton actionReboot;

	private readonly RadioButton actionRejoin;

	private readonly Dictionary<string, FrameworkElement> pages = new Dictionary<string, FrameworkElement>();

	private readonly Dictionary<string, RadioButton> navs = new Dictionary<string, RadioButton>();

	private bool fakeRunning;

	public AppConfig Config => config;

	public Vault Vault => vault;

	private DiscordWebhook discord;

	public DiscordWebhook Discord => discord;

	public IDictionary<string, object> LastStatus => lastStatus;

	public bool EngineRunning => engine.IsRunning;

	public string EnginePhase => phase;

	public Window Window => window;

	private bool Running
	{
		get
		{
			if (!engine.IsRunning)
			{
				return fakeRunning;
			}
			return true;
		}
	}

	private ScrollViewer LogScroll
	{
		get
		{
			if (logScroll == null && logList.IsLoaded)
			{
				logScroll = FindChild<ScrollViewer>(logList);
			}
			return logScroll;
		}
	}

	public MainController(Window window, bool autostart)
	{
		this.window = window;
		this.autostart = autostart;
		config = Store.LoadConfig();
		try
		{
			vault = Store.LoadVault();
		}
		catch (Exception error)
		{
			App.LogError("vault.dat не расшифровывается", error);
			vault = new Vault();
			MessageDialog.Show(null, "Не читается список аккаунтов", "Файл vault.dat не расшифровывается — скорее всего, его скопировали с другого компьютера или из другой учётной записи Windows. Добавь аккаунты заново. Старый файл сохранён как vault.dat.broken.", MessageDialog.Kind.Warning);
			try
			{
				File.Copy(AppPaths.VaultFile, AppPaths.VaultFile + ".broken", overwrite: true);
			}
			catch
			{
			}
		}
		root = Find<Border>("Root");
		banner = Find<Border>("Banner");
		bannerText = Find<TextBlock>("BannerText");
		bannerIcon = Find<TextBlock>("BannerIcon");
		bannerActionButton = Find<Button>("BannerAction");
		accountsEmpty = Find<Border>("AccountsEmpty");
		accountsList = Find<ItemsControl>("AccountsList");
		accountsSub = Find<TextBlock>("AccountsSub");
		engineTitle = Find<TextBlock>("EngineTitle");
		engineSub = Find<TextBlock>("EngineSub");
		engineDot = Find<Ellipse>("EngineDot");
		engineGlow = Find<Ellipse>("EngineGlow");
		engineButton = Find<Button>("EngineButton");
		engineButtonIcon = Find<TextBlock>("EngineButtonIcon");
		engineButtonText = Find<TextBlock>("EngineButtonText");
		statGameValue = Find<TextBlock>("StatGameValue");
		statGameSub = Find<TextBlock>("StatGameSub");
		statMemValue = Find<TextBlock>("StatMemValue");
		statMemSub = Find<TextBlock>("StatMemSub");
		statPotValue = Find<TextBlock>("StatPotValue");
		statPotSub = Find<TextBlock>("StatPotSub");
		statSchedValue = Find<TextBlock>("StatSchedValue");
		statSchedSub = Find<TextBlock>("StatSchedSub");
		maxButton = Find<Button>("MaxButton");
		logList = Find<ListBox>("LogList");
		logFilter = Find<ComboBox>("LogFilter");
		scheduleSwitch = Find<CheckBox>("ScheduleSwitch");
		scheduleBody = Find<StackPanel>("ScheduleBody");
		hourBox = Find<TextBox>("HourBox");
		minuteBox = Find<TextBox>("MinuteBox");
		zoneBox = Find<ComboBox>("ZoneBox");
		zoneHint = Find<TextBlock>("ZoneHint");
		actionReboot = Find<RadioButton>("ActionReboot");
		actionRejoin = Find<RadioButton>("ActionRejoin");
		scheduleNext = Find<TextBlock>("ScheduleNext");
		scheduleNextSub = Find<TextBlock>("ScheduleNextSub");
		scheduleWarn = Find<Border>("ScheduleWarn");
		scheduleWarnText = Find<TextBlock>("ScheduleWarnText");
		scheduleWarnFix = Find<Button>("ScheduleWarnFix");
		Find<TextBlock>("VersionText").Text = (Updater.Current.Build > 0) ? Updater.CurrentText : Updater.Current.ToString(2);
		BitmapFrame bitmapFrame = Xaml.AppIcon();
		if (bitmapFrame != null)
		{
			window.Icon = bitmapFrame;
		}
		engine = new EngineHost(window.Dispatcher);
		engine.Message += OnEngineMessage;
		engine.ErrorLine += delegate(string line)
		{
			AddLog(new LogLine
			{
				Time = DateTime.Now.ToString("HH:mm:ss"),
				Message = line,
				Color = "Red"
			});
		};
		engine.Exited += OnEngineExited;
		auth = new AuthManager(this);
		auth.StateChanged += OnAuthState;
		RobloxLogin.NeedsUser += delegate(string name, string what)
		{
			ShowBanner("login", name + ": " + what, null, null, warning: true);
			discord?.OnAlert("**" + name + "**: " + what + " — нужно твоё действие в окне входа AutoRejoin.");
			tray.Balloon("AutoRejoin: вход в " + name, what, warning: true);
		};
		tray = new Tray();
		tray.OpenRequested += ShowWindow;
		tray.StartStopRequested += delegate
		{
			if (engine.IsRunning)
			{
				StopEngine();
			}
			else
			{
				StartEngine(0);
			}
		};
		tray.RejoinAllRequested += RejoinAll;
		tray.ExitRequested += Quit;
		clock = new DispatcherTimer(DispatcherPriority.Background)
		{
			Interval = TimeSpan.FromSeconds(1.0)
		};
		clock.Tick += delegate
		{
			OnClock();
		};
		saveTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(400.0)
		};
		saveTimer.Tick += delegate
		{
			saveTimer.Stop();
			SaveConfigNow();
		};
		restartTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(15.0)
		};
		restartTimer.Tick += delegate
		{
			restartTimer.Stop();
			if (engineWanted && !engine.IsRunning)
			{
				StartEngine(0);
			}
		};
		WireChrome();
		WireNavigation();
		WireAccounts();
		WireSchedule();
		WireLog();
		discord = new DiscordWebhook(this, window.Dispatcher);
		settingsPage = new SettingsPage(this, Find<StackPanel>("SettingsHost"));
		Find<Button>("ContactButton").Click += delegate
		{
			ContactOwner();
		};
		Find<Button>("HelpContact").Click += delegate
		{
			ContactOwner();
		};
		Find<Button>("HelpOpenData").Click += delegate
		{
			SystemTools.OpenFolder(AppPaths.DataDir);
		};
		Find<Button>("HelpOpenApp").Click += delegate
		{
			SystemTools.OpenFolder(AppPaths.AppDir);
		};
		Find<Button>("BannerClose").Click += delegate
		{
			HideBanner();
		};
		bannerActionButton.Click += delegate
		{
			bannerAction?.Invoke();
		};
		engineButton.Click += delegate
		{
			if (engine.IsRunning)
			{
				StopEngine();
			}
			else
			{
				StartEngine(0);
			}
		};
		window.Closing += delegate(object s, CancelEventArgs e)
		{
			if (!quitting)
			{
				e.Cancel = true;
				HideToTray();
			}
		};
		Application.Current.SessionEnding += delegate
		{
			quitting = true;
		};
		ReloadCards();
		UpdateEngineBlock();
		UpdateStats();
		UpdateSchedulePreview();
		LoadLogHistory();
		if (!Store.ConfigExists)
		{
			SaveConfigNow();
		}
		SystemTools.RepairAutostart();
		clock.Start();
		discord.Apply();
	}

	private T Find<T>(string name) where T : class
	{
		return Xaml.Find<T>(window, name);
	}

	public void Begin()
	{
		bool flag = vault.accounts.Any((VaultAccount a) => a.enabled && !string.IsNullOrEmpty(a.cookie));
		if (autostart && flag)
		{
			StartEngine(config.startupDelay);
		}
		else if (config.autoStartEngine && flag)
		{
			StartEngine(0);
		}
		// Тихая проверка обновлений через 20 сек после запуска: нашлось — баннер с кнопкой «Обновить».
		DispatcherTimer updateTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(20.0)
		};
		updateTimer.Tick += async delegate
		{
			updateTimer.Stop();
			await CheckForUpdates(interactive: false);
		};
		updateTimer.Start();
		// Очистка старых логов: проверяем раз в 10 мин (первый раз — через 2 мин после запуска), чистим раз в N часов.
		DispatcherTimer cleanupTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMinutes(2.0)
		};
		cleanupTimer.Tick += async delegate
		{
			cleanupTimer.Interval = TimeSpan.FromMinutes(10.0);
			if (config.logCleanupOn && LogCleaner.IsDue(config.logCleanupHours))
			{
				await CleanLogsNow();
			}
		};
		cleanupTimer.Start();
	}

	private bool cleaningLogs;

	public async Task<string> CleanLogsNow()
	{
		if (cleaningLogs)
		{
			return null;
		}
		cleaningLogs = true;
		try
		{
			int hours = config.logCleanupHours;
			LogCleaner.Result result = await Task.Run(() => LogCleaner.Run(hours));
			AppLog(null, "Очистка логов старше " + hours + " ч: " + result.Text + ".", "DarkGray");
			return result.Text;
		}
		finally
		{
			cleaningLogs = false;
		}
	}

	// ---------- обновления с GitHub ----------

	private bool updateBusy;

	// interactive — по кнопке «Проверить обновления»: показывает результат в окне и сразу предлагает установить.
	// Возвращает строку для подписи в настройках.
	public async Task<string> CheckForUpdates(bool interactive)
	{
		if (updateBusy)
		{
			return null;
		}
		updateBusy = true;
		Updater.Release release;
		try
		{
			release = await Updater.CheckAsync();
		}
		catch (Exception ex)
		{
			updateBusy = false;
			if (interactive)
			{
				MessageDialog.Show(window, "Не получилось проверить", "Не получилось проверить обновления: " + ex.Message + ".", MessageDialog.Kind.Warning);
			}
			return "проверка не удалась: " + ex.Message;
		}
		updateBusy = false;
		if (release == null)
		{
			if (interactive)
			{
				MessageDialog.Show(window, "Обновлений нет", "У тебя последняя версия — " + Updater.CurrentText + ".", MessageDialog.Kind.Success);
			}
			return "последняя версия — " + Updater.CurrentText;
		}
		string version = release.Version.ToString(3);
		if (!interactive)
		{
			ShowBanner("update", "Вышла новая версия AutoRejoin " + version + ".", "Обновить", delegate
			{
				InstallUpdate(release);
			}, warning: false);
			return "доступна версия " + version;
		}
		InstallUpdate(release);
		return "доступна версия " + version;
	}

	private async void InstallUpdate(Updater.Release release)
	{
		if (updateBusy)
		{
			return;
		}
		ShowWindow();
		string version = release.Version.ToString(3);
		string notes = (release.Notes ?? "").Trim();
		if (notes.Length > 600)
		{
			notes = notes.Substring(0, 600) + "…";
		}
		string message = "Сейчас " + Updater.CurrentText + ", вышла " + version + "." + ((notes.Length > 0) ? ("\n\n" + notes) : "") + "\n\nAutoRejoin скачает обновление, закроется и откроется уже новой версией. Окна Роблокса останутся открытыми, аккаунты и настройки сохранятся.";
		if (!MessageDialog.Ask(window, "Установить обновление " + version + "?", message, "Обновить", MessageDialog.Kind.Info, "Позже"))
		{
			return;
		}
		updateBusy = true;
		ShowBanner("update", "Скачиваю обновление " + version + "…", null, null, warning: false);
		try
		{
			await Updater.PrepareAsync(release);
		}
		catch (Exception ex)
		{
			updateBusy = false;
			App.LogError("обновление не установилось", ex);
			ShowBanner("update", "Обновление не установилось: " + ex.Message, "Повторить", delegate
			{
				InstallUpdate(release);
			}, warning: true);
			return;
		}
		AppLog(null, "Устанавливаю обновление " + version + " — AutoRejoin перезапустится.", "Cyan");
		engineWanted = false;
		if (engine.IsRunning)
		{
			await engine.StopAsync();
		}
		discord?.Stop();
		quitting = true;
		if (saveTimer.IsEnabled)
		{
			saveTimer.Stop();
			SaveConfigNow();
		}
		tray.Dispose();
		Application.Current.Shutdown();
	}

	private void WireChrome()
	{
		Find<Button>("MinButton").Click += delegate
		{
			window.WindowState = WindowState.Minimized;
		};
		maxButton.Click += delegate
		{
			window.WindowState = ((window.WindowState != WindowState.Maximized) ? WindowState.Maximized : WindowState.Normal);
		};
		Find<Button>("CloseButton").Click += delegate
		{
			HideToTray();
		};
		window.StateChanged += delegate
		{
			bool flag = window.WindowState == WindowState.Maximized;
			root.Margin = (flag ? new Thickness(7.0) : new Thickness(0.0));
			root.BorderThickness = (flag ? new Thickness(0.0) : new Thickness(1.0));
			maxButton.Content = (flag ? "\ue923" : "\ue922");
			maxButton.ToolTip = (flag ? "Восстановить" : "Развернуть");
		};
		maxButton.ToolTip = "Развернуть";
	}

	public void ShowWindow()
	{
		if (!window.IsVisible)
		{
			window.Show();
		}
		if (window.WindowState == WindowState.Minimized)
		{
			window.WindowState = WindowState.Normal;
		}
		window.Activate();
		window.Topmost = true;
		window.Topmost = false;
		window.Focus();
	}

	private void HideToTray()
	{
		window.Hide();
		if (!config.trayHintShown)
		{
			config.trayHintShown = true;
			ConfigChanged();
			tray.Balloon("AutoRejoin работает в трее", engine.IsRunning ? "Аккаунты продолжают перезаходить. Открыть — значок у часов, выйти — правой кнопкой по нему." : "Открыть — значок у часов, выйти — правой кнопкой по нему.", warning: false);
		}
	}

	private async void Quit()
	{
		if (engine.IsRunning)
		{
			ShowWindow();
			if (!MessageDialog.Ask(window, "Выйти из AutoRejoin?", "Аккаунты перестанут перезаходить. Окна Роблокса останутся открытыми.", "Выйти", MessageDialog.Kind.Warning, "Отмена"))
			{
				return;
			}
			engineWanted = false;
			await engine.StopAsync();
		}
		discord?.Stop();
		quitting = true;
		if (saveTimer.IsEnabled)
		{
			saveTimer.Stop();
			SaveConfigNow();
		}
		tray.Dispose();
		Application.Current.Shutdown();
	}

	private void WireNavigation()
	{
		pages["accounts"] = Find<FrameworkElement>("PageAccounts");
		pages["schedule"] = Find<FrameworkElement>("PageSchedule");
		pages["settings"] = Find<FrameworkElement>("PageSettings");
		pages["log"] = Find<FrameworkElement>("PageLog");
		pages["help"] = Find<FrameworkElement>("PageHelp");
		navs["accounts"] = Find<RadioButton>("NavAccounts");
		navs["schedule"] = Find<RadioButton>("NavSchedule");
		navs["settings"] = Find<RadioButton>("NavSettings");
		navs["log"] = Find<RadioButton>("NavLog");
		navs["help"] = Find<RadioButton>("NavHelp");
		foreach (KeyValuePair<string, RadioButton> nav in navs)
		{
			string name = nav.Key;
			nav.Value.Checked += delegate
			{
				ShowPage(name);
			};
		}
		Find<Border>("StatSchedCard").MouseLeftButtonUp += delegate
		{
			navs["schedule"].IsChecked = true;
		};
	}

	private void ContactOwner()
	{
		Owner.OpenDiscord();
		ShowBanner("contact", "Ник " + Owner.Name + " скопирован — открываю Discord.", null, null, warning: false);
		DispatcherTimer hide = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(4.0)
		};
		hide.Tick += delegate
		{
			hide.Stop();
			HideBanner("contact");
		};
		hide.Start();
	}

	public void ShowPage(string name)
	{
		foreach (KeyValuePair<string, FrameworkElement> page in pages)
		{
			bool show = page.Key == name;
			bool wasHidden = page.Value.Visibility != Visibility.Visible;
			page.Value.Visibility = (show ? Visibility.Visible : Visibility.Collapsed);
			if (show && wasHidden)
			{
				Anim.SlideIn(page.Value);
			}
		}
		if (navs[name].IsChecked != true)
		{
			navs[name].IsChecked = true;
		}
		if (name == "log")
		{
			ScrollLogToEnd();
		}
		if (name == "schedule")
		{
			UpdateSchedulePreview();
		}
	}

	private void ShowBanner(string kind, string text, string actionText, Action action, bool warning)
	{
		bannerKind = kind;
		bannerText.Text = text;
		bannerAction = action;
		bannerActionButton.Visibility = ((actionText == null) ? Visibility.Collapsed : Visibility.Visible);
		bannerActionButton.Content = actionText;
		bannerIcon.Text = (warning ? "\ue7ba" : "\ue946");
		bannerIcon.Foreground = (warning ? Palette.Yellow : Palette.Blue);
		banner.Background = (warning ? Palette.YellowSoft : Palette.BlueSoft);
		banner.BorderBrush = (warning ? Palette.YellowLine : Palette.BlueLine);
		if (banner.Visibility != Visibility.Visible)
		{
			banner.Visibility = Visibility.Visible;
			Anim.SlideIn(banner, -10.0, 0.28);
		}
	}

	private void HideBanner(string kind = null)
	{
		if (kind == null || !(bannerKind != kind))
		{
			banner.Visibility = Visibility.Collapsed;
			bannerKind = null;
			bannerAction = null;
		}
	}

	public void StartEngine(int startupDelay)
	{
		if (engine.IsRunning)
		{
			return;
		}
		if (!vault.accounts.Any((VaultAccount a) => a.enabled && !string.IsNullOrEmpty(a.cookie)))
		{
			ShowPage("accounts");
			ShowBanner("info", "Сначала добавь хотя бы один аккаунт — без аккаунтов запускать нечего.", "Добавить аккаунт", AddAccount, warning: false);
			return;
		}
		restartTimer.Stop();
		HideBanner("crash");
		HideBanner("conflict");
		HideBanner("info");
		try
		{
			engine.Start(startupDelay);
			engineWanted = true;
			phase = ((startupDelay > 0) ? "delay" : "starting");
		}
		catch (Exception ex)
		{
			App.LogError("движок не запустился", ex);
			ShowBanner("crash", "Не получилось запустить движок: " + ex.Message, null, null, warning: true);
		}
		UpdateEngineBlock();
		UpdateCardsIdle();
	}

	public async void StopEngine()
	{
		engineWanted = false;
		restartTimer.Stop();
		if (engine.IsRunning)
		{
			phase = "stopping";
			UpdateEngineBlock();
			await engine.StopAsync();
		}
		// Кнопка «Остановить» закрывает все окна Роблокса. Движок уже остановлен, так что никто их не перезапустит,
		// пока не нажмут «Запустить».
		int closed = await Task.Run(() => CloseAllRoblox());
		if (closed > 0)
		{
			AppLog(null, "Закрыл окна Роблокса: " + closed + " шт.", "Cyan");
		}
	}

	private static int CloseAllRoblox()
	{
		int closed = 0;
		foreach (Process process in Process.GetProcessesByName("RobloxPlayerBeta"))
		{
			try
			{
				process.Kill();
				process.WaitForExit(5000);
				closed++;
			}
			catch
			{
			}
			finally
			{
				process.Dispose();
			}
		}
		return closed;
	}

	private void OnEngineExited(int code, bool byUs)
	{
		phase = "stopped";
		lastStatus = null;
		rebootAtUtc = null;
		HideBanner("reboot");
		if (byUs || quitting)
		{
			engineWanted = false;
		}
		else
		{
			switch (code)
			{
			case 4:
				engineWanted = false;
				ShowBanner("conflict", "Уже работает другой AutoRejoin — например, старая версия в окне консоли. Два сразу перезапускали бы одни и те же окна.", "Закрыть другой и запустить этот", async delegate
				{
					int killed = SystemTools.KillOtherAutoRejoins(engine.ProcessId);
					AddLog(new LogLine
					{
						Time = DateTime.Now.ToString("HH:mm:ss"),
						Message = "Закрыл другой AutoRejoin (" + killed + " процесс.).",
						Color = "Cyan"
					});
					await Task.Delay(1500);
					StartEngine(0);
				}, warning: true);
				break;
			case 3:
				engineWanted = false;
				break;
			default:
			{
				if (!engineWanted)
				{
					break;
				}
				DateTime now = DateTime.UtcNow;
				crashes.Add(now);
				crashes.RemoveAll((DateTime t) => (now - t).TotalMinutes > 10.0);
				if (crashes.Count >= 5)
				{
					engineWanted = false;
					ShowBanner("crash", "Движок AutoRejoin падает раз за разом (5 раз за 10 минут) — остановил. Подробности в журнале.", "Запустить снова", delegate
					{
						StartEngine(0);
					}, warning: true);
					tray.Balloon("AutoRejoin остановлен", "Движок падает раз за разом — открой AutoRejoin.", warning: true);
				}
				else
				{
					ShowBanner("crash", "Движок AutoRejoin неожиданно закрылся (код " + code + ") — перезапускаю через 15 сек.", null, null, warning: true);
					restartTimer.Start();
				}
				break;
			}
			}
		}
		UpdateEngineBlock();
		UpdateCardsIdle();
		UpdateStats();
		discord?.Update();
	}

	private void OnEngineMessage(IDictionary<string, object> data)
	{
		switch (Store.Str(data, "t"))
		{
		case "log":
			AddLog(new LogLine
			{
				Time = Store.Str(data, "time"),
				Account = Store.Str(data, "acc"),
				Key = Store.Str(data, "key"),
				Message = Store.Str(data, "msg"),
				Color = Store.Str(data, "color")
			});
			break;
		case "status":
			ApplyStatus(data);
			break;
		case "alert":
		{
			string text = Store.Str(data, "msg") ?? "";
			ShowBanner("alert", text, null, null, warning: true);
			discord?.OnAlert(text);
			if (!window.IsVisible || window.WindowState == WindowState.Minimized)
			{
				tray.Balloon("AutoRejoin: нужна твоя помощь", text, warning: true);
			}
			break;
		}
		case "reboot":
			rebootAtUtc = Store.Time(data, "at");
			rebootWhy = Store.Str(data, "why");
			tray.Balloon("Перезагрузка компьютера", (rebootWhy ?? "") + ".", warning: true);
			UpdateRebootBanner();
			break;
		case "auth":
			auth.OnEngineRequest(Store.Str(data, "key"), Store.Str(data, "reason"), Store.Str(data, "detail"));
			break;
		}
	}

	public VaultAccount FindAccount(string key)
	{
		if (key != null)
		{
			return vault.accounts.FirstOrDefault((VaultAccount a) => a.id == key);
		}
		return null;
	}

	public void SendEngine(string command)
	{
		engine.Send(command);
	}

	public void AppLog(string account, string message, string color)
	{
		DateTime now = DateTime.Now;
		AddLog(new LogLine
		{
			Time = now.ToString("HH:mm:ss"),
			Account = account,
			Message = message,
			Color = color
		});
		string s = "[" + now.ToString("HH:mm:ss") + "] " + (string.IsNullOrEmpty(account) ? "" : ("[" + account + "] ")) + message + Environment.NewLine;
		for (int i = 0; i < 5; i++)
		{
			try
			{
				using FileStream fileStream = new FileStream(AppPaths.LogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
				byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(s);
				fileStream.Write(bytes, 0, bytes.Length);
				break;
			}
			catch (IOException)
			{
				Thread.Sleep(30);
			}
			catch
			{
				break;
			}
		}
	}

	public void AccountRefreshed(VaultAccount entry, string message)
	{
		if (SaveVault())
		{
			ReloadCards();
			HideBanner("auth");
			HideBanner("login");
			AppLog(entry.name, message, "Green");
		}
	}

	public void ShowAuthBanner(VaultAccount entry, string reason, string detail)
	{
		string text = ((reason == "moderated") ? (entry.name + ": Роблокс не пускает аккаунт (403)" + (string.IsNullOrEmpty(detail) ? "" : (" — " + detail)) + ". Прими предупреждение на roblox.com под этим аккаунтом, включи «Принимать предупреждения Роблокса» в настройках или переключи аккаунт на вход по логину и паролю.") : (entry.name + ": кука устарела (401). Вставь новую или переключи аккаунт на вход по логину и паролю — тогда AutoRejoin будет обновлять куку сам."));
		AccountCard card = cards.FirstOrDefault((AccountCard c) => c.Key == entry.id);
		ShowBanner("auth", text, "Открыть аккаунт", delegate
		{
			if (card != null)
			{
				EditAccount(card);
			}
		}, warning: true);
		if (!window.IsVisible || window.WindowState == WindowState.Minimized)
		{
			tray.Balloon("AutoRejoin: " + entry.name, (reason == "moderated") ? "Роблокс не пускает аккаунт (403)." : "Кука устарела — нужна новая.", warning: true);
		}
	}

	private void OnAuthState(string key)
	{
		AccountCard accountCard = cards.FirstOrDefault((AccountCard c) => c.Key == key);
		if (accountCard == null)
		{
			return;
		}
		accountCard.SetAuthNote((!auth.IsBusy(key)) ? null : (RobloxLogin.Busy ? "Входит в аккаунт — новая кука…" : "Берёт новую куку…"));
		if (!auth.IsBusy(key))
		{
			if (lastStatus != null && Running)
			{
				ApplyStatus(lastStatus);
			}
			else
			{
				UpdateCardsIdle();
			}
		}
	}

	private void ApplyStatus(IDictionary<string, object> status)
	{
		lastStatus = status;
		string value = Store.Str(status, "phase");
		if (!string.IsNullOrEmpty(value) && Running)
		{
			phase = value;
		}
		Dictionary<string, IDictionary<string, object>> dictionary = new Dictionary<string, IDictionary<string, object>>();
		foreach (IDictionary<string, object> item in Store.List(status, "accounts"))
		{
			string text = Store.Str(item, "key");
			if (text != null)
			{
				dictionary[text] = item;
			}
		}
		foreach (AccountCard card in cards)
		{
			if (card.Enabled && dictionary.TryGetValue(card.Key, out var value2))
			{
				card.ApplyEngine(value2);
			}
			else
			{
				card.ApplyIdle(IdleStateText(), IdleDetail());
			}
		}
		DateTime? dateTime = Store.Time(status, "rebootAt");
		if (dateTime.HasValue)
		{
			rebootAtUtc = dateTime;
		}
		else if (rebootAtUtc.HasValue && Store.Str(status, "rebootAt") == null)
		{
			rebootAtUtc = null;
		}
		UpdateRebootBanner();
		UpdateEngineBlock();
		UpdateStats();
		discord?.Update();
	}

	private string IdleStateText()
	{
		if (!Running)
		{
			return "Остановлен";
		}
		if (!(phase == "running"))
		{
			return "Ожидание";
		}
		return "Проверка";
	}

	private string IdleDetail()
	{
		if (!Running)
		{
			return "AutoRejoin остановлен — аккаунт не перезаходит. Нажми «Запустить».";
		}
		return phase switch
		{
			"delay" => "Автозапуск: даю Windows прогрузиться…", 
			"network" => "Жду интернет…", 
			"accounts" => "Проверяю куки аккаунтов…", 
			"potassium" => "Открываю Потассиум…", 
			"running" => "Проверяю куку у Роблокса…", 
			_ => "Запускаюсь…", 
		};
	}

	private void UpdateCardsIdle()
	{
		if (Running && lastStatus != null)
		{
			return;
		}
		foreach (AccountCard card in cards)
		{
			card.ApplyIdle(IdleStateText(), IdleDetail());
		}
	}

	private void UpdateEngineBlock()
	{
		bool running = Running;
		int num = cards.Count((AccountCard c) => c.Enabled);
		int num2 = cards.Count((AccountCard c) => c.Enabled && c.InGame);
		Brush fill;
		string text;
		string text2;
		if (!running)
		{
			fill = Palette.Text3;
			text = "Остановлен";
			text2 = ((num == 0) ? "Добавь аккаунт, потом «Запустить»" : "Аккаунты не перезаходят");
			if (bannerKind == "crash" && restartTimer.IsEnabled)
			{
				fill = Palette.Red;
				text = "Перезапуск…";
				text2 = "Движок закрылся, через 15 сек запущу снова";
			}
		}
		else if (phase == "running")
		{
			fill = ((num > 0 && num2 == num) ? Palette.Green : Palette.Yellow);
			text = "Работает";
			text2 = num2 + " из " + num + " в игре";
		}
		else if (phase == "stopping")
		{
			fill = Palette.Text3;
			text = "Останавливаю…";
			text2 = "Закрываю окна Роблокса";
		}
		else
		{
			fill = Palette.Blue;
			text = "Запускается";
			text2 = IdleDetail();
		}
		engineDot.Fill = fill;
		engineGlow.Fill = fill;
		engineTitle.Text = text;
		engineSub.Text = text2;
		engineButton.Style = (Style)window.FindResource(running ? "Btn" : "BtnPrimary");
		engineButtonIcon.Text = (running ? "\ue71a" : "\ue768");
		engineButtonText.Text = (running ? "Остановить" : "Запустить");
		engineButton.IsEnabled = phase != "stopping";
		tray.SetRunning(running);
		tray.SetText(Owner.Credit + " — " + (running ? ((phase == "running") ? (num2 + " из " + num + " в игре") : "запускается") : "остановлен"));
		accountsSub.Text = ((cards.Count == 0) ? "Держит аккаунты в игре." : ((running && phase == "running") ? ((num2 == num) ? "Все в игре." : (num2 + " из " + num + " в игре")) : ("Аккаунтов: " + cards.Count + ((num != cards.Count) ? (", включено " + num) : ""))));
	}

	private void UpdateStats()
	{
		int num = cards.Count((AccountCard c) => c.Enabled);
		int num2 = cards.Count((AccountCard c) => c.Enabled && c.InGame);
		if (Running && phase == "running")
		{
			statGameValue.Text = num2 + " / " + num;
			int result;
			int num3 = cards.Sum((AccountCard c) => int.TryParse(c.Restarts, out result) ? result : 0);
			statGameSub.Text = ((num2 != num) ? (num - num2 + " заходят или ждут") : ((num3 == 0) ? "все на месте" : ("перезаходов: " + num3)));
		}
		else
		{
			statGameValue.Text = (Running ? "…" : "—");
			statGameSub.Text = (Running ? "запускается" : "AutoRejoin остановлен");
		}
		MemoryInfo memoryInfo = MemoryInfo.Get();
		if (memoryInfo != null)
		{
			statMemValue.Text = memoryInfo.FreeCommitGB.ToString("0.0", CultureInfo.InvariantCulture) + " ГБ";
			statMemSub.Text = "ОЗУ " + Math.Round(memoryInfo.TotalRamGB) + " ГБ + подкачка " + Math.Max(0.0, Math.Round(memoryInfo.CommitLimitGB - memoryInfo.TotalRamGB)) + " ГБ";
			statMemValue.Foreground = ((memoryInfo.FreeCommitGB < 2.0) ? Palette.Red : ((memoryInfo.FreeCommitGB < 4.0) ? Palette.Yellow : Palette.Text));
		}
		IDictionary<string, object> dictionary = Store.Obj(lastStatus, "potassium");
		if (!config.potassium)
		{
			statPotValue.Text = "Выключен";
			statPotSub.Text = "AutoRejoin работает без него";
			statPotValue.Foreground = Palette.Text2;
		}
		else if (dictionary != null && Running)
		{
			if (Store.Flag(dictionary, "signedOut"))
			{
				statPotValue.Text = "Нужен вход";
				statPotSub.Text = "Потассиум разлогинился";
				statPotValue.Foreground = Palette.Red;
			}
			else if (!Store.Flag(dictionary, "running"))
			{
				statPotValue.Text = "Закрыт";
				statPotSub.Text = "AutoRejoin откроет его сам";
				statPotValue.Foreground = Palette.Yellow;
			}
			else
			{
				statPotValue.Text = "Работает";
				statPotValue.Foreground = Palette.Text;
				double? num4 = Store.Num(dictionary, "clients");
				statPotSub.Text = (Store.Flag(dictionary, "autoAttachProblem") ? "автоаттач выключен!" : (num4.HasValue ? ("подключён к окнам: " + num4.Value) : "автоаттач включён"));
			}
		}
		else
		{
			bool flag = Process.GetProcessesByName("Potassium").Length > 0;
			statPotValue.Text = (flag ? "Открыт" : "Закрыт");
			statPotSub.Text = (flag ? "AutoRejoin не следит за ним" : "откроется при запуске");
			statPotValue.Foreground = Palette.Text;
		}
		if (!config.schedule.enabled)
		{
			statSchedValue.Text = "Выключен";
			statSchedSub.Text = "настроить →";
			statSchedValue.Foreground = Palette.Text2;
			return;
		}
		DateTime utc = NextScheduleUtc();
		statSchedValue.Text = utc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
		statSchedValue.Foreground = Palette.Text;
		string text = ((config.schedule.action == "reboot") ? "перезагрузка ПК" : "перезаход всеми");
		string text2 = ScheduleMath.DescribeLocal(utc);
		statSchedSub.Text = text2.Substring(0, text2.LastIndexOf(" в ", StringComparison.Ordinal)) + " · " + text;
	}

	private void UpdateRebootBanner()
	{
		if (!rebootAtUtc.HasValue)
		{
			HideBanner("reboot");
			return;
		}
		int num = (int)Math.Ceiling((rebootAtUtc.Value - DateTime.UtcNow).TotalSeconds);
		if (num < -5)
		{
			rebootAtUtc = null;
			HideBanner("reboot");
			return;
		}
		ShowBanner("reboot", "Компьютер перезагрузится через " + Math.Max(num, 0) + " сек" + (string.IsNullOrEmpty(rebootWhy) ? "" : (": " + rebootWhy)) + ".", "Отменить перезагрузку", delegate
		{
			engine.Send("cancel-reboot");
			if (!engine.IsRunning)
			{
				try
				{
					Process.Start(new ProcessStartInfo("shutdown.exe", "/a")
					{
						CreateNoWindow = true,
						UseShellExecute = false
					});
				}
				catch
				{
				}
			}
			rebootAtUtc = null;
			HideBanner("reboot");
		}, warning: true);
	}

	private void OnClock()
	{
		foreach (AccountCard card in cards)
		{
			card.RefreshUptime();
		}
		if (rebootAtUtc.HasValue)
		{
			UpdateRebootBanner();
		}
		UpdateStats();
		if (pages["schedule"].Visibility == Visibility.Visible)
		{
			UpdateSchedulePreview();
		}
		engineGlow.Opacity = ((!Running || !(phase != "running")) ? 0.35 : ((engineGlow.Opacity > 0.3) ? 0.15 : 0.45));
	}

	public void ConfigChanged()
	{
		saveTimer.Stop();
		saveTimer.Start();
		UpdateStats();
		UpdateSchedulePreview();
		discord?.Apply();
	}

	public void SetDiscordWebhook(string url)
	{
		url = (url ?? "").Trim();
		if ((vault.discordWebhook ?? "") != url)
		{
			vault.discordWebhook = url;
			SaveVault();
			discord?.Apply();
		}
	}

	private void SaveConfigNow()
	{
		try
		{
			Store.SaveConfig(config);
			engine.Send("reload");
		}
		catch (Exception ex)
		{
			App.LogError("не сохранился config.json", ex);
			ShowBanner("info", "Не получилось сохранить настройки: " + ex.Message, null, null, warning: true);
		}
	}

	private bool SaveVault()
	{
		try
		{
			Store.SaveVault(vault);
			engine.Send("reload");
			return true;
		}
		catch (Exception ex)
		{
			App.LogError("не сохранился vault.dat", ex);
			MessageDialog.Show(window, "Не сохранилось", "Не получилось сохранить аккаунты: " + ex.Message, MessageDialog.Kind.Danger);
			return false;
		}
	}

	public void SetPotassiumLogin(string user, string password)
	{
		if (!(vault.potassium.user == user) || !(vault.potassium.password == password))
		{
			vault.potassium.user = user;
			vault.potassium.password = password;
			SaveVault();
		}
	}

	public void SetAutostart(bool on)
	{
		try
		{
			SystemTools.SetAutostart(on);
		}
		catch (Exception ex)
		{
			MessageDialog.Show(window, "Автозапуск", "Не получилось изменить автозапуск: " + ex.Message, MessageDialog.Kind.Danger);
		}
		UpdateSchedulePreview();
	}

	public async void SetupPagefile(Button button)
	{
		int count = vault.accounts.Count((VaultAccount a) => a.enabled);
		if (count == 0)
		{
			MessageDialog.Show(window, "Файл подкачки", "Сначала добавь аккаунты: размер подкачки считается по их числу.", MessageDialog.Kind.Info);
		}
		else
		{
			if (!MessageDialog.Ask(window, "Настроить файл подкачки?", "Файл подкачки на диске " + Environment.GetEnvironmentVariable("SystemDrive") + " станет фиксированного размера под " + count + " окон Роблокса. Windows попросит права администратора. Заработает после перезагрузки компьютера.", "Настроить", MessageDialog.Kind.Info, "Отмена"))
			{
				return;
			}
			button.IsEnabled = false;
			List<string> messages = new List<string>();
			int code = -1;
			try
			{
				await Task.Run(delegate
				{
					ProcessStartInfo startInfo = EngineHost.CreateStartInfo("-SetupPagefile -AccountCount " + count);
					using Process process = Process.Start(startInfo);
					process.StandardInput.Close();
					string input;
					while ((input = process.StandardOutput.ReadLine()) != null)
					{
						try
						{
							if (Store.Json().DeserializeObject(input) is IDictionary<string, object> data && Store.Str(data, "t") == "log")
							{
								messages.Add(Store.Str(data, "msg"));
							}
						}
						catch
						{
						}
					}
					process.WaitForExit();
					code = process.ExitCode;
				});
			}
			catch (Exception ex)
			{
				messages.Add(ex.Message);
			}
			button.IsEnabled = true;
			MessageDialog.Show(message: (messages.Count > 0) ? messages[messages.Count - 1] : "Без подробностей.", owner: window, title: (code == 0) ? "Файл подкачки настроен" : "Файл подкачки не изменён", kind: (code != 0) ? MessageDialog.Kind.Warning : MessageDialog.Kind.Success);
		}
	}

	private void WireAccounts()
	{
		accountsList.ItemsSource = cards;
		accountsList.AddHandler(ButtonBase.ClickEvent, (RoutedEventHandler)delegate(object s, RoutedEventArgs e)
		{
			Button button = e.OriginalSource as Button;
			AccountCard accountCard = ((button == null) ? null : (button.DataContext as AccountCard));
			if (accountCard != null)
			{
				if (button.Tag as string == "rejoin")
				{
					RejoinAccount(accountCard);
				}
				else if (button.Tag as string == "edit")
				{
					EditAccount(accountCard);
				}
			}
		});
		Find<Button>("AddAccountButton").Click += delegate
		{
			AddAccount();
		};
		Find<Button>("EmptyAddButton").Click += delegate
		{
			AddAccount();
		};
		Find<Button>("EmptyImportButton").Click += delegate
		{
			ImportAccountsTxt();
		};
		Find<Button>("RejoinAllButton").Click += delegate
		{
			RejoinAll();
		};
	}

	private void ReloadCards()
	{
		discord?.Update();
		Dictionary<string, AccountCard> dictionary = cards.ToDictionary((AccountCard c) => c.Key);
		cards.Clear();
		foreach (VaultAccount account in vault.accounts)
		{
			if (dictionary.TryGetValue(account.id, out var value))
			{
				value.Update(account);
			}
			else
			{
				value = new AccountCard(account);
				value.EnabledToggled += OnCardToggled;
			}
			cards.Add(value);
		}
		accountsEmpty.Visibility = ((cards.Count != 0) ? Visibility.Collapsed : Visibility.Visible);
		if (lastStatus != null && Running)
		{
			ApplyStatus(lastStatus);
		}
		else
		{
			UpdateCardsIdle();
		}
		RefreshLogFilter();
		UpdateEngineBlock();
		LoadAvatars();
	}

	private void OnCardToggled(AccountCard card)
	{
		VaultAccount vaultAccount = vault.accounts.FirstOrDefault((VaultAccount a) => a.id == card.Key);
		if (vaultAccount != null)
		{
			vaultAccount.enabled = card.Enabled;
			SaveVault();
			if (lastStatus != null && Running)
			{
				ApplyStatus(lastStatus);
			}
			else
			{
				UpdateCardsIdle();
			}
			UpdateEngineBlock();
		}
	}

	private string DuplicateOf(string userId, string exceptId)
	{
		VaultAccount vaultAccount = vault.accounts.FirstOrDefault((VaultAccount a) => a.userId == userId && a.id != exceptId);
		if (vaultAccount != null)
		{
			if (!string.IsNullOrEmpty(vaultAccount.name))
			{
				return vaultAccount.name;
			}
			return userId;
		}
		return null;
	}

	public void AddAccount()
	{
		ShowWindow();
		AccountDialog accountDialog = new AccountDialog(null, DuplicateOf, !vault.accounts.Any((VaultAccount a) => a.main), config.autoAcceptWarnings);
		if (accountDialog.ShowDialog(window) != AccountDialog.Outcome.Saved)
		{
			return;
		}
		VaultAccount vaultAccount = new VaultAccount();
		vaultAccount.id = accountDialog.AccountId;
		vaultAccount.cookie = accountDialog.Cookie;
		vaultAccount.link = accountDialog.Link;
		vaultAccount.main = accountDialog.Main;
		vaultAccount.enabled = true;
		vaultAccount.userId = accountDialog.User.Id;
		vaultAccount.name = accountDialog.User.Name;
		vaultAccount.login = accountDialog.LoginMode;
		vaultAccount.username = accountDialog.Username ?? "";
		vaultAccount.password = accountDialog.Password ?? "";
		vaultAccount.master = accountDialog.Master ?? "";
		vaultAccount.cookieAt = DateTime.UtcNow.ToString("o");
		VaultAccount vaultAccount2 = vaultAccount;
		vault.accounts.Add(vaultAccount2);
		if (!SaveVault())
		{
			vault.accounts.Remove(vaultAccount2);
			return;
		}
		ReloadCards();
		HideBanner("info");
		AddLog(new LogLine
		{
			Time = DateTime.Now.ToString("HH:mm:ss"),
			Account = vaultAccount2.name,
			Message = "добавлен в приложении.",
			Color = "Cyan"
		});
		if (!engine.IsRunning)
		{
			ShowBanner("info", "Аккаунт " + vaultAccount2.name + " добавлен. AutoRejoin сейчас остановлен.", "Запустить", delegate
			{
				StartEngine(0);
			}, warning: false);
		}
	}

	private void EditAccount(AccountCard card)
	{
		VaultAccount vaultAccount = vault.accounts.FirstOrDefault((VaultAccount a) => a.id == card.Key);
		if (vaultAccount == null)
		{
			return;
		}
		AccountDialog accountDialog = new AccountDialog(vaultAccount, DuplicateOf, suggestMain: false, config.autoAcceptWarnings);
		switch (accountDialog.ShowDialog(window))
		{
		case AccountDialog.Outcome.Deleted:
			vault.accounts.Remove(vaultAccount);
			SaveVault();
			try
			{
				File.Delete(System.IO.Path.Combine(AppPaths.AvatarDir, vaultAccount.userId + ".png"));
			}
			catch
			{
			}
			DeleteBrowserProfile(vaultAccount.id);
			ReloadCards();
			break;
		case AccountDialog.Outcome.Saved:
		{
			vaultAccount.link = accountDialog.Link;
			vaultAccount.main = accountDialog.Main;
			bool usesPassword = vaultAccount.UsesPassword;
			vaultAccount.login = accountDialog.LoginMode;
			vaultAccount.username = accountDialog.Username ?? "";
			vaultAccount.password = accountDialog.Password ?? "";
			if (accountDialog.Master != null)
			{
				vaultAccount.master = accountDialog.Master;
			}
			if (accountDialog.Cookie != null)
			{
				vaultAccount.cookie = accountDialog.Cookie;
				vaultAccount.userId = accountDialog.User.Id;
				vaultAccount.name = accountDialog.User.Name;
				vaultAccount.cookieAt = DateTime.UtcNow.ToString("o");
				auth.Forget(vaultAccount.id);
				HideBanner("auth");
			}
			SaveVault();
			if (usesPassword && !vaultAccount.UsesPassword)
			{
				DeleteBrowserProfile(vaultAccount.id);
			}
			ReloadCards();
			if (accountDialog.Cookie != null)
			{
				AppLog(vaultAccount.name, vaultAccount.UsesPassword ? "вошёл по логину и паролю — кука обновлена." : "новая кука сохранена.", "Green");
			}
			break;
		}
		}
	}

	private static void DeleteBrowserProfile(string id)
	{
		try
		{
			Directory.Delete(AppPaths.BrowserProfile(id), recursive: true);
		}
		catch
		{
		}
	}

	private async void RejoinAccount(AccountCard card)
	{
		VaultAccount entry = FindAccount(card.Key);
		if (entry != null && entry.UsesPassword && card.Enabled && (card.State == "problem" || card.State == "deferred"))
		{
			auth.Forget(entry.id);
			await auth.Refresh(entry, (card.State == "deferred") ? "moderated" : "expired", null, interactive: true);
		}
		else if (!engine.IsRunning || phase != "running")
		{
			ShowBanner("info", engine.IsRunning ? "AutoRejoin ещё запускается — перезайти можно, когда он заработает." : "AutoRejoin остановлен — сначала нажми «Запустить».", null, null, warning: false);
		}
		else if (card.Enabled)
		{
			engine.Send("rejoin " + card.Key);
		}
	}

	private void RejoinAll()
	{
		ShowWindow();
		if (!engine.IsRunning || phase != "running")
		{
			ShowBanner("info", engine.IsRunning ? "AutoRejoin ещё запускается — перезайти можно, когда он заработает." : "AutoRejoin остановлен — сначала нажми «Запустить».", null, null, warning: false);
		}
		else if (MessageDialog.Ask(window, "Перезайти всеми аккаунтами?", "Окна всех аккаунтов закроются разом и через 10 сек зайдут заново по одному.", "Перезайти", MessageDialog.Kind.Warning, "Отмена"))
		{
			engine.Send("rejoin-all");
		}
	}

	public async void ImportAccountsTxt()
	{
		ShowWindow();
		OpenFileDialog dialog = new OpenFileDialog
		{
			Filter = "accounts.txt|*.txt|Все файлы|*.*",
			Title = "Файл с куками (accounts.txt)"
		};
		if (dialog.ShowDialog(window) != true)
		{
			return;
		}
		List<string[]> lines = new List<string[]>();
		string[] array = File.ReadAllLines(dialog.FileName, Encoding.UTF8);
		foreach (string text in array)
		{
			string text2 = text.Trim();
			if (text2.Length != 0 && !text2.StartsWith("#"))
			{
				string[] array2 = Regex.Split(text2, "\\s+", RegexOptions.None);
				lines.Add(new string[2]
				{
					RobloxApi.CleanCookie(array2[0]),
					(array2.Length > 1) ? string.Join(" ", array2.Skip(1)).Trim() : ""
				});
			}
		}
		if (lines.Count == 0)
		{
			MessageDialog.Show(window, "Импорт", "В файле нет ни одной куки.", MessageDialog.Kind.Info);
			return;
		}
		int added = 0;
		List<string> problems = new List<string>();
		for (int j = 0; j < lines.Count; j++)
		{
			ShowBanner("import", "Импорт: проверяю куку " + (j + 1) + " из " + lines.Count + "…", null, null, warning: false);
			string cookie = lines[j][0];
			string link = lines[j][1];
			if (!RobloxApi.IsValidLink(link, out var linkProblem))
			{
				problems.Add("строка " + (j + 1) + ": " + linkProblem);
				link = "";
			}
			try
			{
				RobloxUser user = await Task.Run(() => RobloxApi.CheckCookie(cookie));
				if (DuplicateOf(user.Id, null) != null)
				{
					problems.Add(user.Name + " уже есть в списке");
					continue;
				}
				vault.accounts.Add(new VaultAccount
				{
					cookie = cookie,
					link = link,
					enabled = true,
					userId = user.Id,
					name = user.Name,
					main = false
				});
				added++;
			}
			catch (CookieException ex)
			{
				problems.Add("строка " + (j + 1) + ": " + ex.Message);
			}
			catch (Exception ex2)
			{
				problems.Add("строка " + (j + 1) + ": " + ex2.Message);
			}
		}
		HideBanner("import");
		if (added > 0)
		{
			SaveVault();
			ReloadCards();
		}
		MessageDialog.Show(window, (added > 0) ? ("Импортировано аккаунтов: " + added) : "Ничего не импортировано", ((problems.Count > 0) ? ("Не вышло:\n• " + string.Join("\n• ", problems) + "\n\n") : "") + ((added > 0) ? "Мейна выбери кнопкой ✎ у аккаунта. Сам файл accounts.txt больше не нужен — удали его, в нём куки открытым текстом." : ""), (added <= 0) ? MessageDialog.Kind.Warning : MessageDialog.Kind.Success);
	}

	private async void LoadAvatars()
	{
		List<AccountCard> missing = new List<AccountCard>();
		foreach (AccountCard item in cards.Where((AccountCard c) => !string.IsNullOrEmpty(c.UserId)))
		{
			string path = System.IO.Path.Combine(AppPaths.AvatarDir, item.UserId + ".png");
			if (File.Exists(path))
			{
				if (item.Avatar == null)
				{
					item.Avatar = LoadImage(path);
				}
				if ((DateTime.Now - File.GetLastWriteTime(path)).TotalDays < 3.0)
				{
					continue;
				}
			}
			missing.Add(item);
		}
		if (missing.Count == 0)
		{
			return;
		}
		try
		{
			List<string> ids = missing.Select((AccountCard c) => c.UserId).ToList();
			await Task.Run(delegate
			{
				Dictionary<string, string> headshotUrls = RobloxApi.GetHeadshotUrls(ids);
				foreach (KeyValuePair<string, string> item2 in headshotUrls)
				{
					try
					{
						File.WriteAllBytes(System.IO.Path.Combine(AppPaths.AvatarDir, item2.Key + ".png"), RobloxApi.Download(item2.Value));
					}
					catch
					{
					}
				}
			});
			foreach (AccountCard item3 in missing)
			{
				string path2 = System.IO.Path.Combine(AppPaths.AvatarDir, item3.UserId + ".png");
				if (File.Exists(path2))
				{
					item3.Avatar = LoadImage(path2);
				}
			}
		}
		catch
		{
		}
	}

	private static ImageSource LoadImage(string path)
	{
		try
		{
			BitmapImage bitmapImage = new BitmapImage();
			bitmapImage.BeginInit();
			bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
			bitmapImage.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
			bitmapImage.DecodePixelWidth = 96;
			bitmapImage.UriSource = new Uri(path);
			bitmapImage.EndInit();
			bitmapImage.Freeze();
			return bitmapImage;
		}
		catch
		{
			return null;
		}
	}

	private void WireSchedule()
	{
		scheduleLoading = true;
		foreach (TimeZoneInfo systemTimeZone in TimeZoneInfo.GetSystemTimeZones())
		{
			zoneBox.Items.Add(new ZoneItem
			{
				Id = systemTimeZone.Id,
				Name = systemTimeZone.DisplayName
			});
		}
		TimeZoneInfo current2 = ScheduleMath.FindZone(config.schedule.timeZone);
		zoneBox.SelectedItem = zoneBox.Items.Cast<ZoneItem>().FirstOrDefault((ZoneItem z) => z.Id == current2.Id);
		scheduleSwitch.IsChecked = config.schedule.enabled;
		if (!ScheduleMath.TryParseTime(config.schedule.time, out var hour, out var minute))
		{
			hour = 9;
			minute = 51;
		}
		hourBox.Text = hour.ToString("00");
		minuteBox.Text = minute.ToString("00");
		actionReboot.IsChecked = config.schedule.action == "reboot";
		actionRejoin.IsChecked = config.schedule.action != "reboot";
		scheduleBody.IsEnabled = config.schedule.enabled;
		scheduleLoading = false;
		scheduleSwitch.Checked += delegate
		{
			config.schedule.enabled = true;
			scheduleBody.IsEnabled = true;
			ScheduleChanged();
		};
		scheduleSwitch.Unchecked += delegate
		{
			config.schedule.enabled = false;
			scheduleBody.IsEnabled = false;
			ScheduleChanged();
		};
		zoneBox.SelectionChanged += delegate
		{
			if (zoneBox.SelectedItem is ZoneItem zoneItem)
			{
				config.schedule.timeZone = zoneItem.Id;
				ScheduleChanged();
			}
		};
		actionReboot.Checked += delegate
		{
			config.schedule.action = "reboot";
			ScheduleChanged();
		};
		actionRejoin.Checked += delegate
		{
			config.schedule.action = "rejoin";
			ScheduleChanged();
		};
		WireTimeBox(hourBox, Find<Button>("HourUp"), Find<Button>("HourDown"), 23);
		WireTimeBox(minuteBox, Find<Button>("MinuteUp"), Find<Button>("MinuteDown"), 59);
		Find<Button>("ScheduleRunNow").Click += delegate
		{
			RunScheduleNow();
		};
		scheduleWarnFix.Click += delegate
		{
			if (!engine.IsRunning && !scheduleWarnFix.Content.ToString().StartsWith("Включить"))
			{
				StartEngine(0);
			}
			else
			{
				SetAutostart(on: true);
				settingsPage.Build();
			}
		};
	}

	private void WireTimeBox(TextBox box, Button up, Button down, int max)
	{
		Action<int> step = delegate(int delta)
		{
			if (!int.TryParse(box.Text, out var result))
			{
				result = 0;
			}
			result = ((result + delta) % (max + 1) + max + 1) % (max + 1);
			box.Text = result.ToString("00");
		};
		up.Click += delegate
		{
			step(1);
		};
		down.Click += delegate
		{
			step(-1);
		};
		box.PreviewMouseWheel += delegate(object s, MouseWheelEventArgs e)
		{
			step((e.Delta > 0) ? 1 : (-1));
			e.Handled = true;
		};
		box.PreviewKeyDown += delegate(object s, KeyEventArgs e)
		{
			if (e.Key == Key.Up)
			{
				step(1);
				e.Handled = true;
			}
			else if (e.Key == Key.Down)
			{
				step(-1);
				e.Handled = true;
			}
		};
		box.PreviewTextInput += delegate(object s, TextCompositionEventArgs e)
		{
			e.Handled = !Regex.IsMatch(e.Text, "^\\d$");
		};
		box.TextChanged += delegate
		{
			if (!scheduleLoading && int.TryParse(hourBox.Text, out var result) && result >= 0 && result <= 23 && int.TryParse(minuteBox.Text, out var result2) && result2 >= 0 && result2 <= 59)
			{
				string text = result.ToString("00") + ":" + result2.ToString("00");
				if (!(text == config.schedule.time))
				{
					config.schedule.time = text;
					ScheduleChanged();
				}
			}
		};
		box.LostKeyboardFocus += delegate
		{
			if (!int.TryParse(box.Text, out var result) || result < 0 || result > max)
			{
				ScheduleMath.TryParseTime(config.schedule.time, out var hour, out var minute);
				result = ((box == hourBox) ? hour : minute);
			}
			box.Text = result.ToString("00");
		};
	}

	private void ScheduleChanged()
	{
		if (!scheduleLoading)
		{
			ConfigChanged();
		}
	}

	private DateTime NextScheduleUtc()
	{
		if (!ScheduleMath.TryParseTime(config.schedule.time, out var hour, out var minute))
		{
			hour = 9;
			minute = 51;
		}
		return ScheduleMath.NextUtc(DateTime.UtcNow, hour, minute, ScheduleMath.FindZone(config.schedule.timeZone));
	}

	private void UpdateSchedulePreview()
	{
		TimeZoneInfo timeZoneInfo = ScheduleMath.FindZone(config.schedule.timeZone);
		bool flag = timeZoneInfo.Id != TimeZoneInfo.Local.Id && timeZoneInfo.BaseUtcOffset != TimeZoneInfo.Local.BaseUtcOffset;
		zoneHint.Text = (flag ? "На часах этого компьютера — другой пояс: время пересчитается само." : "Совпадает с часовым поясом этого компьютера.");
		string text = ((config.schedule.action == "reboot") ? "Перезагрузка компьютера" : "Перезаход всеми аккаунтами");
		if (!config.schedule.enabled)
		{
			scheduleNext.Text = "Плановый перезапуск выключен";
			scheduleNextSub.Text = "Включи переключатель вверху, чтобы задать время.";
		}
		else
		{
			DateTime dateTime = NextScheduleUtc();
			scheduleNext.Text = "Следующий: " + ScheduleMath.DescribeLocal(dateTime);
			string text2 = text + " · " + ScheduleMath.DescribeIn(dateTime - DateTime.UtcNow);
			if (flag)
			{
				string text3 = text2;
				text2 = text3 + " · по поясу «" + ScheduleMath.ShortZoneName(timeZoneInfo) + "» это " + config.schedule.time;
			}
			DateTime? dateTime2 = Store.Time(Store.Obj(lastStatus, "schedule"), "last");
			if (dateTime2.HasValue)
			{
				text2 = text2 + " · прошлый — " + dateTime2.Value.ToLocalTime().ToString("dd.MM HH:mm", CultureInfo.InvariantCulture);
			}
			scheduleNextSub.Text = text2;
		}
		if (config.schedule.enabled && config.schedule.action == "reboot" && !SystemTools.IsAutostartOn())
		{
			scheduleWarn.Visibility = Visibility.Visible;
			scheduleWarnText.Text = "Автозапуск выключен: после перезагрузки AutoRejoin не запустится сам и аккаунты не зайдут.";
			scheduleWarnFix.Content = "Включить автозапуск";
			scheduleWarnFix.Visibility = Visibility.Visible;
		}
		else if (config.schedule.enabled && !Running)
		{
			scheduleWarn.Visibility = Visibility.Visible;
			scheduleWarnText.Text = "AutoRejoin сейчас остановлен — расписание срабатывает, только пока он запущен.";
			scheduleWarnFix.Content = "Запустить";
			scheduleWarnFix.Visibility = Visibility.Visible;
		}
		else
		{
			scheduleWarn.Visibility = Visibility.Collapsed;
		}
	}

	private void RunScheduleNow()
	{
		if (!engine.IsRunning || phase != "running")
		{
			MessageDialog.Show(window, "Сначала запусти AutoRejoin", "Плановый перезапуск выполняет работающий AutoRejoin.", MessageDialog.Kind.Info);
			return;
		}
		bool flag = config.schedule.action == "reboot";
		if (MessageDialog.Ask(window, flag ? "Перезагрузить компьютер сейчас?" : "Перезайти всеми аккаунтами сейчас?", flag ? "Windows перезагрузится сразу, окна Роблокса закроются. После перезагрузки AutoRejoin запустится сам (если включён автозапуск) и первым заведёт мейна." : "Окна всех аккаунтов закроются разом и через 10 сек зайдут заново по одному.", flag ? "Перезагрузить" : "Перезайти", (!flag) ? MessageDialog.Kind.Warning : MessageDialog.Kind.Danger, "Отмена"))
		{
			engine.Send("schedule-now");
		}
	}

	private void WireLog()
	{
		logList.ItemsSource = logShown;
		logFilter.SelectionChanged += delegate
		{
			RebuildLog();
		};
		RefreshLogFilter();
		Find<Button>("LogCopy").Click += delegate
		{
			try
			{
				Clipboard.SetText(string.Join(Environment.NewLine, logShown.Select((LogLine l) => l.ToString())));
			}
			catch
			{
			}
		};
		Find<Button>("LogOpen").Click += delegate
		{
			SystemTools.OpenFile(AppPaths.LogFile);
		};
		Find<Button>("LogClear").Click += delegate
		{
			logAll.Clear();
			logShown.Clear();
		};
		logList.Loaded += delegate
		{
			ScrollLogToEnd();
		};
	}

	private void RefreshLogFilter()
	{
		ChoiceItem choiceItem = logFilter.SelectedItem as ChoiceItem;
		string value = ((choiceItem == null) ? "" : choiceItem.Value);
		logFilter.Items.Clear();
		logFilter.Items.Add(new ChoiceItem
		{
			Value = "",
			Name = "Все события"
		});
		logFilter.Items.Add(new ChoiceItem
		{
			Value = "!",
			Name = "Только важное"
		});
		foreach (AccountCard card in cards)
		{
			logFilter.Items.Add(new ChoiceItem
			{
				Value = card.Name,
				Name = card.Name
			});
		}
		logFilter.SelectedItem = logFilter.Items.Cast<ChoiceItem>().FirstOrDefault((ChoiceItem i) => i.Value == value) ?? logFilter.Items[0];
	}

	private bool PassesFilter(LogLine line)
	{
		string text = ((!(logFilter.SelectedItem is ChoiceItem choiceItem)) ? "" : choiceItem.Value);
		if (text == "")
		{
			return true;
		}
		if (text == "!")
		{
			return line.Important;
		}
		return string.Equals(line.Account, text, StringComparison.OrdinalIgnoreCase);
	}

	private void RebuildLog()
	{
		logShown.Clear();
		foreach (LogLine item in logAll.Where(PassesFilter).Skip(Math.Max(0, logAll.Count(PassesFilter) - 1500)))
		{
			logShown.Add(item);
		}
		ScrollLogToEnd();
	}

	private void AddLog(LogLine line)
	{
		if (line.Message == null)
		{
			return;
		}
		ScrollViewer scrollViewer = LogScroll;
		bool flag = scrollViewer == null || scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 4.0;
		logAll.Add(line);
		if (logAll.Count > 6000)
		{
			logAll.RemoveRange(0, 1000);
		}
		if (PassesFilter(line))
		{
			logShown.Add(line);
			if (logShown.Count > 1500)
			{
				logShown.RemoveAt(0);
			}
			if (flag)
			{
				ScrollLogToEnd();
			}
		}
	}

	private void ScrollLogToEnd()
	{
		if (logShown.Count == 0)
		{
			return;
		}
		window.Dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)delegate
		{
			ScrollViewer scrollViewer = LogScroll;
			if (scrollViewer != null)
			{
				scrollViewer.ScrollToEnd();
			}
			else if (logShown.Count > 0)
			{
				logList.ScrollIntoView(logShown[logShown.Count - 1]);
			}
		});
	}

	private void LoadLogHistory()
	{
		try
		{
			if (!File.Exists(AppPaths.LogFile))
			{
				return;
			}
			List<string> list = new List<string>();
			using (FileStream fileStream = new FileStream(AppPaths.LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
			{
				long num = Math.Max(0L, fileStream.Length - 120000);
				fileStream.Seek(num, SeekOrigin.Begin);
				using StreamReader streamReader = new StreamReader(fileStream, Encoding.UTF8);
				if (num > 0)
				{
					streamReader.ReadLine();
				}
				string item;
				while ((item = streamReader.ReadLine()) != null)
				{
					list.Add(item);
				}
			}
			Regex regex = new Regex("^\\[(\\d\\d:\\d\\d:\\d\\d)\\] (?:\\[([^\\]]+)\\] )?(.*)$");
			foreach (string item2 in list.Skip(Math.Max(0, list.Count - 400)))
			{
				Match match = regex.Match(item2);
				if (match.Success)
				{
					string value = match.Groups[3].Value;
					AddLog(new LogLine
					{
						Time = match.Groups[1].Value,
						Account = match.Groups[2].Value,
						Message = value,
						Color = GuessColor(value)
					});
				}
			}
			if (list.Count > 0)
			{
				AddLog(new LogLine
				{
					Time = DateTime.Now.ToString("HH:mm:ss"),
					Message = "— выше: прошлые записи из rejoin.log —",
					Color = "DarkGray"
				});
			}
		}
		catch
		{
		}
	}

	private static string GuessColor(string message)
	{
		if (Regex.IsMatch(message, "перезахожу|закрылось|не получилось|не зашёл|отключ", RegexOptions.IgnoreCase))
		{
			return "Yellow";
		}
		if (Regex.IsMatch(message, "в игре|запустился|подключен|вошёл|Вошёл", RegexOptions.IgnoreCase))
		{
			return "Green";
		}
		if (Regex.IsMatch(message, "не пускает|недействительна|умирают|не смог", RegexOptions.IgnoreCase))
		{
			return "Red";
		}
		return "Gray";
	}

	private static T FindChild<T>(DependencyObject parent) where T : DependencyObject
	{
		for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(parent, i);
			T val = (child as T) ?? FindChild<T>(child);
			if (val != null)
			{
				return val;
			}
		}
		return null;
	}

	public void FeedFake(string statusFile, string logFile)
	{
		if (logFile != null && File.Exists(logFile))
		{
			string[] array = File.ReadAllLines(logFile, Encoding.UTF8);
			foreach (string text in array)
			{
				if (text.StartsWith("{"))
				{
					OnEngineMessage(Store.Json().DeserializeObject(text) as IDictionary<string, object>);
				}
			}
		}
		if (statusFile != null && File.Exists(statusFile))
		{
			phase = "running";
			IDictionary<string, object> status = Store.Json().DeserializeObject(File.ReadAllText(statusFile, Encoding.UTF8)) as IDictionary<string, object>;
			fakeRunning = true;
			ApplyStatus(status);
		}
	}

	public void DisposeTray()
	{
		tray.Dispose();
	}
}
