using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace AutoRejoinApp;

public class SettingsPage
{
	private readonly MainController main;

	private readonly StackPanel host;

	private Button pagefileButton;

	private AppConfig Cfg => main.Config;

	private TextBlock discordState;

	public SettingsPage(MainController main, StackPanel host)
	{
		this.main = main;
		this.host = host;
		Build();
		main.Discord.StateChanged += UpdateDiscordState;
	}

	private void UpdateDiscordState()
	{
		if (discordState != null)
		{
			discordState.Text = "Сейчас: " + main.Discord.State;
			discordState.Foreground = main.Discord.Healthy ? Palette.Green : Palette.Text2;
		}
	}

	private void BuildDiscord()
	{
		StackPanel section = Section("", "Discord");
		AddRow(section, "Отчёт в Discord", "Раз в заданное время вебхук присылает в канал сообщение: какие аккаунты в игре (🟢), а какие нет (🔴). Только отчёт — управлять программой из Discord нельзя.", Switch(Cfg.discordOn, delegate(bool v)
		{
			Cfg.discordOn = v;
			main.ConfigChanged();
		}));
		PasswordBox urlBox = new PasswordBox
		{
			Style = Res("PasswordField"),
			Password = (main.Vault.discordWebhook ?? "")
		};
		TextBlock urlError = HintBlock("Это не ссылка вебхука Discord — она начинается с https://discord.com/api/webhooks/");
		urlError.Foreground = Palette.Red;
		urlError.Visibility = Visibility.Collapsed;
		urlError.Margin = new Thickness(0.0, 6.0, 0.0, 0.0);
		urlBox.LostKeyboardFocus += delegate
		{
			string text = (urlBox.Password ?? "").Trim();
			bool bad = text.Length > 0 && !DiscordWebhook.IsValidUrl(text);
			urlError.Visibility = (bad ? Visibility.Visible : Visibility.Collapsed);
			if (!bad)
			{
				main.SetDiscordWebhook(text);
			}
		};
		StackPanel urlPanel = new StackPanel();
		urlPanel.Children.Add(urlBox);
		urlPanel.Children.Add(urlError);
		AddStacked(section, "Ссылка вебхука", "В Discord: настройки канала → Интеграции → Вебхуки → Новый вебхук → «Копировать URL вебхука». Хранится зашифрованной вместе с аккаунтами — никому её не показывай: по ней любой может писать в канал.", urlPanel);
		AddRow(section, "Присылать отчёт раз в", "Первый отчёт — через минуту после запуска программы.", WithSuffix(NumberBox(Cfg.discordInterval, 1.0, 1440.0, integer: true, delegate(double v)
		{
			Cfg.discordInterval = (int)v;
			main.ConfigChanged();
		}), "мин"));
		discordState = HintBlock("");
		discordState.Margin = new Thickness(0.0, 3.0, 0.0, 0.0);
		FrameworkElement row = AddRow(section, "Проверка", "Отправить отчёт прямо сейчас, не дожидаясь срока.", Button("Отправить сейчас", delegate
		{
			main.Discord.SendNow();
		}));
		((StackPanel)((Grid)row).Children[0]).Children.Add(discordState);
		UpdateDiscordState();
	}

	private Style Res(string key)
	{
		return (Style)host.FindResource(key);
	}

	public void Build()
	{
		host.Children.Clear();
		BuildServer();
		BuildWindows();
		BuildMemory();
		BuildPotassium();
		BuildAccounts();
		BuildStartup();
		BuildDiscord();
		BuildData();
	}

	private void BuildAccounts()
	{
		StackPanel section = Section("\ue716", "Вход в аккаунты");
		AddRow(section, "Принимать предупреждения Роблокса", "Ошибка 403 «User is moderated»: на аккаунте предупреждение, и Роблокс не пускает его в игру, пока его не примут на roblox.com. Включено — AutoRejoin принимает предупреждение сам (как кнопка «I agree / Reactivate») и пишет его текст в журнал. Выключено — при входе по логину и паролю откроется окно с предупреждением, принять его нужно тебе. Баны со сроком не снимаются — аккаунт ждёт конца срока.", Switch(Cfg.autoAcceptWarnings, delegate(bool v)
		{
			Cfg.autoAcceptWarnings = v;
			main.ConfigChanged();
		}));
	}

	private void BuildServer()
	{
		StackPanel section = Section("\ue774", "Сервер");
		TextBox linkBox = Field("https://www.roblox.com/share?code=…&type=Server", Cfg.link);
		TextBlock error = HintBlock("");
		error.Foreground = Palette.Red;
		error.Visibility = Visibility.Collapsed;
		error.Margin = new Thickness(0.0, 6.0, 0.0, 0.0);
		linkBox.LostKeyboardFocus += delegate
		{
			string text = (linkBox.Text ?? "").Trim();
			if (!RobloxApi.IsValidLink(text, out var problem))
			{
				error.Text = problem;
				error.Visibility = Visibility.Visible;
			}
			else
			{
				error.Visibility = Visibility.Collapsed;
				if (!(text == Cfg.link) || !string.IsNullOrEmpty(Cfg.linkFile))
				{
					Cfg.link = text;
					Cfg.linkFile = "";
					main.ConfigChanged();
				}
			}
		};
		StackPanel stackPanel = new StackPanel();
		stackPanel.Children.Add(linkBox);
		stackPanel.Children.Add(error);
		if (!string.IsNullOrEmpty(Cfg.linkFile))
		{
			TextBlock textBlock = HintBlock("Сейчас ссылка берётся из файла " + Cfg.linkFile + " (меняется на лету). Впишешь ссылку здесь — файл больше не читается.");
			textBlock.Margin = new Thickness(0.0, 8.0, 0.0, 0.0);
			textBlock.Foreground = Palette.Yellow;
			stackPanel.Children.Add(textBlock);
		}
		AddStacked(section, "Ссылка на сервер", "VIP-ссылка (приглашение на приватный сервер или ссылка с privateServerLinkCode), ссылка на игру или ID плейса. Пусто — обычный сервер Bee Swarm Simulator. Применяется при следующих перезаходах. У аккаунта может быть своя ссылка (кнопка ✎).", stackPanel);
	}

	private void BuildWindows()
	{
		StackPanel section = Section("\ue7f4", "Окна");
		AddRow(section, "Сворачивать окна хелперов", "После захода окно уменьшается и сворачивается — так Роблокс почти не грузит видеокарту. Окно, которое ты развернул 3 раза за 10 сек, больше не сворачивается.", Switch(Cfg.minimizeWindows, delegate(bool v)
		{
			Cfg.minimizeWindows = v;
			main.ConfigChanged();
		}));
		TextBox element = NumberBox(Cfg.windowWidth, 320.0, 3840.0, integer: true, delegate(double v)
		{
			Cfg.windowWidth = (int)v;
			main.ConfigChanged();
		});
		TextBox element2 = NumberBox(Cfg.windowHeight, 240.0, 2160.0, integer: true, delegate(double v)
		{
			Cfg.windowHeight = (int)v;
			main.ConfigChanged();
		});
		StackPanel stackPanel = new StackPanel();
		stackPanel.Orientation = Orientation.Horizontal;
		StackPanel stackPanel2 = stackPanel;
		stackPanel2.Children.Add(element);
		stackPanel2.Children.Add(new TextBlock
		{
			Text = "×",
			Foreground = Palette.Text2,
			Margin = new Thickness(8.0, 0.0, 8.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center
		});
		stackPanel2.Children.Add(element2);
		AddRow(section, "Размер окна хелпера", "До какого размера уменьшать окно перед тем, как свернуть.", stackPanel2);
		AddRow(section, "Мейн на весь экран", "Окно мейна открывается на весь экран (F11). Мейн никогда не сворачивается. Мейна выбирают в аккаунте (кнопка ✎).", Switch(Cfg.mainFullscreen, delegate(bool v)
		{
			Cfg.mainFullscreen = v;
			main.ConfigChanged();
		}));
		AddRow(section, "Лимит FPS мейна", "0 — не трогать (как в настройках Роблокса).", WithSuffix(NumberBox(Cfg.mainFps, 0.0, 1000.0, integer: true, delegate(double v)
		{
			Cfg.mainFps = (int)v;
			main.ConfigChanged();
		}), "FPS"));
		AddRow(section, "Лимит FPS хелперов", "Меньше FPS — меньше нагрузка на процессор и видеокарту. 0 — не трогать.", WithSuffix(NumberBox(Cfg.helperFps, 0.0, 1000.0, integer: true, delegate(double v)
		{
			Cfg.helperFps = (int)v;
			main.ConfigChanged();
		}), "FPS"));
		AddRow(section, "Графика хелперов", "Уровень графики Роблокса (1 — самый низкий, 10 — самый высокий) для окон хелперов: меньше текстур и деталей в памяти. Ставится при запуске окна, как лимит FPS. Графика мейна не меняется — ему при запуске возвращается его собственная.", NumberWithSwitch(Cfg.helperQualityOn, Cfg.helperQuality, 1.0, 10.0, integer: true, "ур.", delegate(bool on)
		{
			Cfg.helperQualityOn = on;
			main.ConfigChanged();
		}, delegate(double v)
		{
			Cfg.helperQuality = (int)v;
			main.ConfigChanged();
		}));
		ChoiceItem[] items = new ChoiceItem[4]
		{
			new ChoiceItem
			{
				Value = "",
				Name = "Не трогать"
			},
			new ChoiceItem
			{
				Value = "Normal",
				Name = "Обычный"
			},
			new ChoiceItem
			{
				Value = "AboveNormal",
				Name = "Выше среднего"
			},
			new ChoiceItem
			{
				Value = "High",
				Name = "Высокий"
			}
		};
		AddRow(section, "Приоритет процессора для мейна", "Роблокс сам опускает приоритет своих окон — AutoRejoin возвращает его каждые 100 мс.", Choice(items, Cfg.mainPriority ?? "", delegate(string v)
		{
			Cfg.mainPriority = v;
			main.ConfigChanged();
		}, 180.0));
	}

	private void BuildMemory()
	{
		StackPanel section = Section("\ue9d9", "Память");
		AddRow(section, "Перезапуск раздувшегося окна", "Роблокс со временем раздувается в памяти, а когда она кончается, Windows роняет все окна разом. Окно больше лимита перезаходит (не раньше 2 мин после старта).", NumberWithSwitch(Cfg.memoryLimitOn, Cfg.memoryLimitGB, 1.0, 256.0, integer: false, "ГБ", delegate(bool on)
		{
			Cfg.memoryLimitOn = on;
			main.ConfigChanged();
		}, delegate(double v)
		{
			Cfg.memoryLimitGB = v;
			main.ConfigChanged();
		}));
		AddRow(section, "Не перезапускать мейна", "Только для перезапуска раздувшегося окна: мейн из-за лимита памяти не перезаходит, хелперы — как обычно. Спасение при нехватке памяти по-прежнему может перезапустить и мейна, чтобы Windows не уронила все окна.", Switch(Cfg.memoryLimitSkipMain, delegate(bool v)
		{
			Cfg.memoryLimitSkipMain = v;
			main.ConfigChanged();
		}));
		AddRow(section, "Ждать память перед запуском окна", "Новое окно открывается, только когда свободно столько памяти (вместе с файлом подкачки): окно без памяти не заходит или роняет соседей.", NumberWithSwitch(Cfg.windowMemoryOn, Cfg.windowMemoryGB, 0.5, 64.0, integer: false, "ГБ", delegate(bool on)
		{
			Cfg.windowMemoryOn = on;
			main.ConfigChanged();
		}, delegate(double v)
		{
			Cfg.windowMemoryGB = v;
			main.ConfigChanged();
		}));
		AddRow(section, "Спасение при нехватке памяти", "Свободной памяти осталось меньше — перезаходит самое большое окно, пока Windows не уронила все.", NumberWithSwitch(Cfg.minFreeMemoryOn, Cfg.minFreeMemoryGB, 0.2, 64.0, integer: false, "ГБ", delegate(bool on)
		{
			Cfg.minFreeMemoryOn = on;
			main.ConfigChanged();
		}, delegate(double v)
		{
			Cfg.minFreeMemoryGB = v;
			main.ConfigChanged();
		}));
		AddRow(section, "Потолок RAM хелперов", "Через минуту после захода окно хелпера держит в оперативной памяти не больше этого — остальное Windows выгружает в подкачку, начиная с самого давно не нужного. Окну тесно (часто подгружает страницы обратно) — потолок поднимается сам, до 3×. Мейна не трогает. Не чистит память всем подряд, как Mem Reduct, — его автоочистку тогда лучше выключить.", NumberWithSwitch(Cfg.helperMemCapOn, Cfg.helperMemCapMB, 300.0, 4096.0, integer: true, "МБ", delegate(bool on)
		{
			Cfg.helperMemCapOn = on;
			main.ConfigChanged();
		}, delegate(double v)
		{
			Cfg.helperMemCapMB = (int)v;
			main.ConfigChanged();
		}));
		pagefileButton = Button("Настроить…", delegate
		{
			main.SetupPagefile(pagefileButton);
		});
		AddRow(section, "Файл подкачки", "Windows держит файл подкачки маленьким, и несколько окон Роблокса в него не влезают — окна вылетают с «Out of Virtual Memory». Настрой один раз под число аккаунтов (попросит права администратора) и перезагрузи компьютер.", pagefileButton);
	}

	private void BuildPotassium()
	{
		StackPanel section = Section("\ue943", "Потассиум");
		List<UIElement> dependent = new List<UIElement>();
		StackPanel credentials = null;
		Action refresh = delegate
		{
			foreach (UIElement item in dependent)
			{
				item.IsEnabled = Cfg.potassium && (item != credentials || Cfg.potassiumAutoLogin);
			}
		};
		CheckBox control = Switch(Cfg.potassium, delegate(bool v)
		{
			Cfg.potassium = v;
			refresh();
			main.ConfigChanged();
		});
		AddRow(section, "Работать с Потассиумом", "Открывать Потассиум перед запуском окон, следить, что он жив, и перезапускать, если он закрылся или его окно зависло.", control);
		TextBox pathBox = Field("Пусто — найдётся сам", Cfg.potassiumPath);
		pathBox.LostKeyboardFocus += delegate
		{
			string text = (pathBox.Text ?? "").Trim().Trim('"');
			if (!(text == Cfg.potassiumPath))
			{
				Cfg.potassiumPath = text;
				main.ConfigChanged();
			}
		};
		Button button = Button("Обзор…", delegate
		{
			OpenFileDialog openFileDialog = new OpenFileDialog
			{
				Filter = "Potassium.exe|Potassium.exe|Программы (*.exe)|*.exe",
				Title = "Где лежит Potassium.exe"
			};
			if (openFileDialog.ShowDialog(main.Window) == true)
			{
				pathBox.Text = openFileDialog.FileName;
				Cfg.potassiumPath = openFileDialog.FileName;
				main.ConfigChanged();
			}
		});
		button.Margin = new Thickness(10.0, 0.0, 0.0, 0.0);
		Grid grid = new Grid();
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		grid.Children.Add(pathBox);
		Grid.SetColumn(button, 1);
		grid.Children.Add(button);
		dependent.Add(grid);
		AddStacked(section, "Путь к Potassium.exe", "Пусто — AutoRejoin найдёт сам (Рабочий стол, Загрузки или где он был открыт в прошлый раз).", grid);
		CheckBox checkBox = Switch(Cfg.keepAutoAttach, delegate(bool v)
		{
			Cfg.keepAutoAttach = v;
			main.ConfigChanged();
		});
		dependent.Add(checkBox);
		AddRow(section, "Держать автоаттач включённым", "Скрипт в окна подключает автоаттач Потассиума (Settings → Client → Auto attach). Выключится — AutoRejoin включит его обратно.", checkBox);
		TextBox loginBox = Field("Логин", main.Vault.potassium.user);
		loginBox.Width = 200.0;
		PasswordBox passwordBox = new PasswordBox
		{
			Style = Res("PasswordField"),
			Width = 200.0,
			Margin = new Thickness(10.0, 0.0, 0.0, 0.0),
			Password = (main.Vault.potassium.password ?? "")
		};
		Action saveLogin = delegate
		{
			main.SetPotassiumLogin((loginBox.Text ?? "").Trim(), passwordBox.Password ?? "");
		};
		loginBox.LostKeyboardFocus += delegate
		{
			saveLogin();
		};
		passwordBox.LostKeyboardFocus += delegate
		{
			saveLogin();
		};
		credentials = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Margin = new Thickness(0.0, 12.0, 0.0, 0.0)
		};
		credentials.Children.Add(loginBox);
		credentials.Children.Add(passwordBox);
		CheckBox checkBox2 = Switch(Cfg.potassiumAutoLogin, delegate(bool v)
		{
			Cfg.potassiumAutoLogin = v;
			refresh();
			main.ConfigChanged();
		});
		dependent.Add(checkBox2);
		FrameworkElement frameworkElement = AddRow(section, "Автовход", "Потассиум разлогинился (экран Sign in) — AutoRejoin войдёт сам, до 3 попыток. Логин и пароль хранятся зашифрованными, в журнал не пишутся.", checkBox2);
		((StackPanel)((Grid)frameworkElement).Children[0]).Children.Add(credentials);
		dependent.Add(credentials);
		AddRow(section, "Проверять запуск скрипта", "Нужен загрузчик javad1khive.txt в автоэкзекуте Потассиума — он сообщает, что скрипт в окне запустился. Не запустился за это время после захода — окно перезаходит; следующее окно ждёт скрипт в предыдущем. Без загрузчика выключи.", Track(dependent, NumberWithSwitch(Cfg.scriptCheck, Cfg.scriptStartTimeout, 15.0, 600.0, integer: true, "сек", delegate(bool on)
		{
			Cfg.scriptCheck = on;
			main.ConfigChanged();
		}, delegate(double v)
		{
			Cfg.scriptStartTimeout = (int)v;
			main.ConfigChanged();
		})));
		AddRow(section, "Проверять пульс скрипта", "Тот же загрузчик пишет «пульс» раз в 10 сек. Пульса нет столько времени — окно зависло в игре или скрипт упал: окно перезаходит.", Track(dependent, NumberWithSwitch(Cfg.pulseCheck, Cfg.scriptTimeout, 30.0, 1800.0, integer: true, "сек", delegate(bool on)
		{
			Cfg.pulseCheck = on;
			main.ConfigChanged();
		}, delegate(double v)
		{
			Cfg.scriptTimeout = (int)v;
			main.ConfigChanged();
		})));
		AddRow(section, "Перезаход всех по Balloon Blessing мейна", "Atlas пишет в лог данных каждое продление Balloon Blessing (шар в улье сдан). После стольких продлений у МЕЙНА перезаходят ВСЕ окна — сразу, шар только что сдан. Счёт — с запуска AutoRejoin (после утренней перезагрузки) или с прошлого перезахода всех; кик мейна его не сбрасывает. Шары хелперов не считаются.", Track(dependent, NumberWithSwitch(Cfg.balloonRejoin, Cfg.balloonRejoinCount, 1.0, 50.0, integer: true, "продл.", delegate(bool on)
		{
			Cfg.balloonRejoin = on;
			main.ConfigChanged();
		}, delegate(double v)
		{
			Cfg.balloonRejoinCount = (int)v;
			main.ConfigChanged();
		})));
		AddRow(section, "Не перезаходить перед плановым перезапуском", "Если до перезагрузки или перезахода по расписанию осталось меньше этого, перезаход по Balloon Blessing пропускается — расписание всё обновит само, без лишнего перезапуска.", Track(dependent, WithSuffix(NumberBox(Cfg.balloonQuietMinutes, 0.0, 720.0, integer: true, delegate(double v)
		{
			Cfg.balloonQuietMinutes = (int)v;
			main.ConfigChanged();
		}), "мин")));
		refresh();
	}

	private void BuildStartup()
	{
		StackPanel section = Section("\ue7e8", "Запуск");
		AddRow(section, "Запускать вместе с Windows", "AutoRejoin стартует свёрнутым в трей и сам заводит аккаунты — например, после отключения света или перезагрузки по расписанию. Windows должна входить без пароля.", Switch(SystemTools.IsAutostartOn(), delegate(bool v)
		{
			main.SetAutostart(v);
		}));
		AddRow(section, "Пауза при автозапуске", "Дать Windows и интернету подняться перед стартом.", WithSuffix(NumberBox(Cfg.startupDelay, 0.0, 600.0, integer: true, delegate(double v)
		{
			Cfg.startupDelay = (int)v;
			main.ConfigChanged();
		}), "сек"));
		AddRow(section, "Запускать аккаунты при открытии приложения", "Иначе — кнопкой «Запустить».", Switch(Cfg.autoStartEngine, delegate(bool v)
		{
			Cfg.autoStartEngine = v;
			main.ConfigChanged();
		}));
		AddRow(section, "Пауза между аккаунтами", "Аккаунты заходят строго по очереди: следующий запускается, только когда предыдущий зашёл на сервер (а при включённой проверке скрипта — ещё и скрипт в нём запустился). Мейн всегда первый. Это пауза после такой проверки перед запуском следующего аккаунта.", WithSuffix(NumberBox(Cfg.advanced.launchCooldown, 0.0, 600.0, integer: true, delegate(double v)
		{
			Cfg.advanced.launchCooldown = (int)v;
			main.ConfigChanged();
		}), "сек"));
		AddRow(section, "Запуск через Froststrap / Bloxstrap", "Обычно Роблокс запускается напрямую — на 2–3 сек быстрее. Включи, только если прямой запуск не работает. Новую версию Роблокса AutoRejoin и так скачивает через них.", Switch(Cfg.useBootstrapper, delegate(bool v)
		{
			Cfg.useBootstrapper = v;
			main.ConfigChanged();
		}));
		AddRow(section, "Автоперезагрузка, если окна умирают на старте", "Бывает, что Роблокс убивает каждое новое окно сразу после запуска, — это лечится только перезагрузкой. AutoRejoin перезагрузит компьютер сам, не чаще раза в 30 мин.", Switch(Cfg.autoReboot, delegate(bool v)
		{
			Cfg.autoReboot = v;
			main.ConfigChanged();
		}));
	}

	private void BuildData()
	{
		StackPanel section = Section("\ue8b7", "Данные");
		AddRow(section, "Папка данных", "Настройки, зашифрованные куки, журнал. Её никому не отправляй. " + AppPaths.DataDir, Button("Открыть", delegate
		{
			SystemTools.OpenFolder(AppPaths.DataDir);
		}));
		TextBlock updateState = HintBlock("");
		updateState.Margin = new Thickness(0.0, 3.0, 0.0, 0.0);
		Button updateButton = null;
		updateButton = Button("Проверить обновления", async delegate
		{
			updateButton.IsEnabled = false;
			updateState.Text = "Проверяю…";
			string result = await main.CheckForUpdates(interactive: true);
			updateState.Text = result ?? "";
			updateButton.IsEnabled = true;
		});
		FrameworkElement updateRow = AddRow(section, "Обновления", "Сейчас версия " + Updater.CurrentText + ". Новые версии берутся из релизов на GitHub (" + Updater.RepoUrl + ") и ставятся сами — аккаунты и настройки сохраняются.", updateButton);
		((StackPanel)((Grid)updateRow).Children[0]).Children.Add(updateState);
		AddRow(section, "Импорт из accounts.txt", "Куки и ссылки из старой версии AutoRejoin (по одной куке на строку, ссылка — через пробел после куки).", Button("Импорт…", delegate
		{
			main.ImportAccountsTxt();
		}));
	}

	private StackPanel Section(string glyph, string title)
	{
		Border border = new Border();
		border.Style = Res("Card");
		border.Margin = new Thickness(0.0, 0.0, 0.0, 14.0);
		border.Padding = new Thickness(24.0, 18.0, 24.0, 8.0);
		Border border2 = border;
		StackPanel stackPanel = new StackPanel();
		StackPanel stackPanel2 = new StackPanel();
		stackPanel2.Orientation = Orientation.Horizontal;
		stackPanel2.Margin = new Thickness(0.0, 0.0, 0.0, 6.0);
		StackPanel stackPanel3 = stackPanel2;
		stackPanel3.Children.Add(new TextBlock
		{
			Text = glyph,
			FontFamily = new FontFamily("Segoe MDL2 Assets"),
			FontSize = 15.0,
			Foreground = Palette.Accent,
			VerticalAlignment = VerticalAlignment.Center
		});
		stackPanel3.Children.Add(new TextBlock
		{
			Text = title,
			Style = (Style)host.FindResource("H2"),
			Margin = new Thickness(11.0, 0.0, 0.0, 0.0)
		});
		stackPanel.Children.Add(stackPanel3);
		border2.Child = stackPanel;
		host.Children.Add(border2);
		return stackPanel;
	}

	private FrameworkElement AddRow(StackPanel section, string title, string hint, FrameworkElement control)
	{
		if (section.Children.Count > 1)
		{
			section.Children.Add(new Border
			{
				Height = 1.0,
				Background = Palette.Line,
				Margin = new Thickness(0.0, 2.0, 0.0, 2.0)
			});
		}
		Grid grid = new Grid();
		grid.Margin = new Thickness(0.0, 12.0, 0.0, 12.0);
		Grid grid2 = grid;
		grid2.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid2.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		StackPanel stackPanel = new StackPanel();
		stackPanel.Margin = new Thickness(0.0, 0.0, 28.0, 0.0);
		stackPanel.VerticalAlignment = VerticalAlignment.Center;
		StackPanel stackPanel2 = stackPanel;
		stackPanel2.Children.Add(new TextBlock
		{
			Text = title,
			FontSize = 14.0,
			FontWeight = FontWeights.SemiBold,
			TextWrapping = TextWrapping.Wrap
		});
		if (!string.IsNullOrEmpty(hint))
		{
			TextBlock textBlock = HintBlock(hint);
			textBlock.Margin = new Thickness(0.0, 3.0, 0.0, 0.0);
			stackPanel2.Children.Add(textBlock);
		}
		grid2.Children.Add(stackPanel2);
		control.VerticalAlignment = VerticalAlignment.Center;
		Grid.SetColumn(control, 1);
		grid2.Children.Add(control);
		section.Children.Add(grid2);
		return grid2;
	}

	private void AddStacked(StackPanel section, string title, string hint, FrameworkElement control)
	{
		if (section.Children.Count > 1)
		{
			section.Children.Add(new Border
			{
				Height = 1.0,
				Background = Palette.Line,
				Margin = new Thickness(0.0, 2.0, 0.0, 2.0)
			});
		}
		StackPanel stackPanel = new StackPanel();
		stackPanel.Margin = new Thickness(0.0, 12.0, 0.0, 14.0);
		StackPanel stackPanel2 = stackPanel;
		stackPanel2.Children.Add(new TextBlock
		{
			Text = title,
			FontSize = 14.0,
			FontWeight = FontWeights.SemiBold
		});
		TextBlock textBlock = HintBlock(hint);
		textBlock.Margin = new Thickness(0.0, 3.0, 0.0, 10.0);
		stackPanel2.Children.Add(textBlock);
		stackPanel2.Children.Add(control);
		section.Children.Add(stackPanel2);
	}

	private TextBlock HintBlock(string text)
	{
		TextBlock textBlock = new TextBlock();
		textBlock.Text = text;
		textBlock.Style = (Style)host.FindResource("Hint");
		return textBlock;
	}

	private TextBox Field(string placeholder, string value)
	{
		TextBox textBox = new TextBox();
		textBox.Style = Res("Field");
		textBox.Tag = placeholder;
		textBox.Text = value ?? "";
		return textBox;
	}

	private Button Button(string text, RoutedEventHandler click)
	{
		Button button = new Button();
		button.Style = Res("Btn");
		button.Content = text;
		button.MinWidth = 104.0;
		Button button2 = button;
		button2.Click += click;
		return button2;
	}

	private CheckBox Switch(bool value, Action<bool> changed)
	{
		CheckBox checkBox = new CheckBox();
		checkBox.Style = Res("Switch");
		checkBox.IsChecked = value;
		CheckBox checkBox2 = checkBox;
		checkBox2.Checked += delegate
		{
			changed(obj: true);
		};
		checkBox2.Unchecked += delegate
		{
			changed(obj: false);
		};
		return checkBox2;
	}

	private ComboBox Choice(IEnumerable<ChoiceItem> items, string value, Action<string> changed, double width)
	{
		ComboBox box = new ComboBox
		{
			Style = Res("Combo"),
			Width = width,
			Height = 38.0
		};
		foreach (ChoiceItem item in items)
		{
			box.Items.Add(item);
		}
		box.SelectedItem = box.Items.Cast<ChoiceItem>().FirstOrDefault((ChoiceItem i) => i.Value == value) ?? box.Items[0];
		box.SelectionChanged += delegate
		{
			if (box.SelectedItem is ChoiceItem choiceItem)
			{
				changed(choiceItem.Value);
			}
		};
		return box;
	}

	private TextBox NumberBox(double value, double min, double max, bool integer, Action<double> changed)
	{
		double current = value;
		Func<double, string> format = (double v) => (!integer) ? v.ToString("0.##", CultureInfo.InvariantCulture) : ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture);
		TextBox box = new TextBox
		{
			Style = Res("Field"),
			Width = 78.0,
			TextAlignment = TextAlignment.Right,
			Text = format(value)
		};
		Action commit = delegate
		{
			string s = (box.Text ?? "").Trim().Replace(',', '.');
			if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
			{
				box.Text = format(current);
			}
			else
			{
				result = Math.Max(min, Math.Min(max, integer ? Math.Round(result) : result));
				box.Text = format(result);
				if (result != current)
				{
					current = result;
					changed(result);
				}
			}
		};
		box.LostKeyboardFocus += delegate
		{
			commit();
		};
		box.KeyDown += delegate(object s, KeyEventArgs e)
		{
			if (e.Key == Key.Return)
			{
				commit();
				Keyboard.ClearFocus();
			}
		};
		box.PreviewTextInput += delegate(object s, TextCompositionEventArgs e)
		{
			e.Handled = !Regex.IsMatch(e.Text, integer ? "^\\d$" : "^[\\d.,]$");
		};
		return box;
	}

	private FrameworkElement WithSuffix(FrameworkElement control, string suffix)
	{
		StackPanel stackPanel = new StackPanel();
		stackPanel.Orientation = Orientation.Horizontal;
		StackPanel stackPanel2 = stackPanel;
		stackPanel2.Children.Add(control);
		stackPanel2.Children.Add(new TextBlock
		{
			Text = suffix,
			Foreground = Palette.Text2,
			Margin = new Thickness(9.0, 0.0, 0.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center,
			Width = 30.0
		});
		return stackPanel2;
	}

	private FrameworkElement NumberWithSwitch(bool on, double value, double min, double max, bool integer, string suffix, Action<bool> toggled, Action<double> changed)
	{
		TextBox number = NumberBox(value, min, max, integer, changed);
		number.IsEnabled = on;
		StackPanel stackPanel = new StackPanel();
		stackPanel.Orientation = Orientation.Horizontal;
		StackPanel stackPanel2 = stackPanel;
		stackPanel2.Children.Add(WithSuffix(number, suffix));
		stackPanel2.Children.Add(Switch(on, delegate(bool v)
		{
			number.IsEnabled = v;
			toggled(v);
		}));
		((FrameworkElement)stackPanel2.Children[1]).Margin = new Thickness(14.0, 0.0, 0.0, 0.0);
		return stackPanel2;
	}

	private static FrameworkElement Track(List<UIElement> list, FrameworkElement element)
	{
		list.Add(element);
		return element;
	}
}
