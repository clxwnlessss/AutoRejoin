using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AutoRejoinApp;

// Страница «Топ АФК»: вход / регистрация / гость и топ стендеров за месяц с сервера AutoRejoin.
// Пока ты вошёл в аккаунт, раз в минуту уходит пульс со счётом аккаунтов в игре — сервер копит твоё АФК-время.
public class TopPage
{
	private readonly MainController main;

	private readonly StackPanel host;

	private readonly DispatcherTimer timer;

	private TextBlock meStats;

	private TextBlock meState;

	private StackPanel topList;

	private TextBlock topState;

	private TextBlock monthText;

	private Leaderboard.Me lastMe;

	private List<Leaderboard.Entry> lastTop;

	private string lastMonth;

	private DateTime topLoadedAt = DateTime.MinValue;

	private bool busy;

	private string Token => main.Vault.leaderboardToken;

	private string Nick => main.Vault.leaderboardNick;

	private bool LoggedIn => !string.IsNullOrEmpty(Token);

	private string BaseUrl => Leaderboard.BaseUrl(main.Config);

	public TopPage(MainController main, StackPanel host)
	{
		this.main = main;
		this.host = host;
		Build();
		timer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(60.0)
		};
		timer.Tick += async delegate
		{
			await Beat();
		};
		timer.Start();
		DispatcherTimer first = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(5.0)
		};
		first.Tick += async delegate
		{
			first.Stop();
			await Beat();
		};
		first.Start();
	}

	private Style Res(string key)
	{
		return (Style)host.FindResource(key);
	}

	// Страницу открыли — обновить топ, если он старше минуты.
	public async void OnShown()
	{
		ShowMe();
		if (LoggedIn && lastMe == null)
		{
			try
			{
				lastMe = await Leaderboard.GetMe(BaseUrl, Token);
				ShowMe();
			}
			catch
			{
			}
		}
		if ((DateTime.UtcNow - topLoadedAt).TotalSeconds > 60.0)
		{
			await LoadTop();
		}
	}

	public void Build()
	{
		host.Children.Clear();
		meStats = null;
		meState = null;
		if (LoggedIn)
		{
			BuildProfile();
		}
		else if (main.Config.leaderboardGuest)
		{
			BuildGuest();
		}
		else
		{
			BuildLogin();
		}
		BuildTop();
	}

	// ---------- аккаунт ----------

	private void BuildLogin()
	{
		Border card = Card();
		StackPanel panel = new StackPanel();
		panel.Children.Add(new TextBlock
		{
			Text = L.T("Войди и попади в топ"),
			Style = (Style)host.FindResource("H2"),
			FontSize = 17.0
		});
		panel.Children.Add(Hint(L.T("Пока аккаунты в игре, сервер копит твоё АФК-время.")));
		TextBox nick = new TextBox
		{
			Style = Res("Field"),
			Tag = L.T("Ник (3–20: латиница, цифры, _)"),
			MaxLength = 20,
			Margin = new Thickness(0.0, 16.0, 0.0, 0.0)
		};
		PasswordBox password = new PasswordBox
		{
			Style = Res("PasswordField"),
			MaxLength = 100,
			Margin = new Thickness(0.0, 10.0, 0.0, 0.0)
		};
		TextBlock passLabel = Hint(L.T("Пароль (от 6 символов)"));
		passLabel.Margin = new Thickness(2.0, 10.0, 0.0, -6.0);
		TextBlock error = Hint("");
		error.Foreground = Palette.Red;
		error.Margin = new Thickness(2.0, 10.0, 0.0, 0.0);
		error.Visibility = Visibility.Collapsed;
		panel.Children.Add(nick);
		panel.Children.Add(passLabel);
		panel.Children.Add(password);
		panel.Children.Add(error);
		StackPanel buttons = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Margin = new Thickness(0.0, 16.0, 0.0, 0.0)
		};
		Button login = Btn(L.T("Войти"), "BtnPrimary");
		Button register = Btn(L.T("Создать аккаунт"), "Btn");
		Button guest = Btn(L.T("Как гость"), "BtnGhost");
		register.Margin = new Thickness(10.0, 0.0, 0.0, 0.0);
		guest.Margin = new Thickness(10.0, 0.0, 0.0, 0.0);
		buttons.Children.Add(login);
		buttons.Children.Add(register);
		buttons.Children.Add(guest);
		panel.Children.Add(buttons);
		Action<bool> submit = async delegate(bool create)
		{
			if (busy)
			{
				return;
			}
			string n = (nick.Text ?? "").Trim();
			string p = password.Password ?? "";
			error.Visibility = Visibility.Collapsed;
			busy = true;
			login.IsEnabled = register.IsEnabled = guest.IsEnabled = false;
			Button pressed = create ? register : login;
			object label = pressed.Content;
			pressed.Content = L.T("Подключаюсь…");
			try
			{
				Tuple<string, string> session = create ? (await Leaderboard.Register(BaseUrl, n, p)) : (await Leaderboard.Login(BaseUrl, n, p));
				main.SetLeaderboardSession(session.Item1, session.Item2);
				main.AppLog(null, (create ? L.T("Создан аккаунт топа АФК: ") : L.T("Вход в топ АФК: ")) + session.Item2, "Cyan");
				busy = false;
				Build();
				Anim.SlideIn(host, 12.0, 0.3);
				await Beat();
				await LoadTop();
			}
			catch (Leaderboard.ApiException ex)
			{
				error.Text = ex.Message;
				error.Visibility = Visibility.Visible;
			}
			finally
			{
				busy = false;
				login.IsEnabled = register.IsEnabled = guest.IsEnabled = true;
				pressed.Content = label;
			}
		};
		login.Click += delegate
		{
			submit(obj: false);
		};
		register.Click += delegate
		{
			submit(obj: true);
		};
		password.KeyDown += delegate(object s, KeyEventArgs e)
		{
			if (e.Key == Key.Return)
			{
				submit(obj: false);
			}
		};
		guest.Click += delegate
		{
			main.Config.leaderboardGuest = true;
			main.ConfigChanged();
			Build();
			Anim.SlideIn(host, 12.0, 0.3);
		};
		card.Child = panel;
		host.Children.Add(card);
	}

	private void BuildGuest()
	{
		Border card = Card();
		Grid grid = new Grid();
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		StackPanel text = new StackPanel
		{
			VerticalAlignment = VerticalAlignment.Center
		};
		text.Children.Add(new TextBlock
		{
			Text = L.T("Ты гость"),
			Style = (Style)host.FindResource("H2")
		});
		text.Children.Add(Hint(L.T("Топ видно, но твоё время не считается.")));
		Button enter = Btn(L.T("Войти / создать аккаунт"), "BtnPrimary");
		enter.VerticalAlignment = VerticalAlignment.Center;
		enter.Click += delegate
		{
			main.Config.leaderboardGuest = false;
			main.ConfigChanged();
			Build();
			Anim.SlideIn(host, 12.0, 0.3);
		};
		Grid.SetColumn(enter, 1);
		grid.Children.Add(text);
		grid.Children.Add(enter);
		card.Child = grid;
		host.Children.Add(card);
	}

	private void BuildProfile()
	{
		Border card = new Border
		{
			CornerRadius = new CornerRadius(16.0),
			Padding = new Thickness(22.0, 18.0, 22.0, 18.0),
			BorderBrush = Palette.Make("#3D2A6E"),
			BorderThickness = new Thickness(1.0),
			Background = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#26143F"), (Color)ColorConverter.ConvertFromString("#072530"), 45.0)
		};
		Grid grid = new Grid();
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		Grid avatar = new Grid
		{
			Width = 52.0,
			Height = 52.0
		};
		avatar.Children.Add(new System.Windows.Shapes.Ellipse
		{
			Fill = (Brush)host.FindResource("BrandGradient")
		});
		avatar.Children.Add(new TextBlock
		{
			Text = string.IsNullOrEmpty(Nick) ? "?" : Nick.Substring(0, 1).ToUpperInvariant(),
			FontSize = 22.0,
			FontWeight = FontWeights.Bold,
			Foreground = Brushes.White,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		});
		StackPanel info = new StackPanel
		{
			Margin = new Thickness(16.0, 0.0, 16.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center
		};
		info.Children.Add(new TextBlock
		{
			Text = Nick,
			FontSize = 20.0,
			FontWeight = FontWeights.Bold,
			Foreground = (Brush)host.FindResource("BrandText")
		});
		meStats = new TextBlock
		{
			Text = L.T("Загружаю…"),
			FontSize = 13.5,
			FontWeight = FontWeights.SemiBold,
			Margin = new Thickness(0.0, 3.0, 0.0, 0.0)
		};
		meState = Hint("");
		meState.Margin = new Thickness(0.0, 2.0, 0.0, 0.0);
		info.Children.Add(meStats);
		info.Children.Add(meState);
		Button logout = Btn(L.T("Выйти"), "Btn");
		logout.VerticalAlignment = VerticalAlignment.Center;
		logout.Click += async delegate
		{
			string token = Token;
			main.SetLeaderboardSession(null, null);
			Build();
			Anim.SlideIn(host, 12.0, 0.3);
			try
			{
				await Leaderboard.Logout(BaseUrl, token);
			}
			catch
			{
			}
		};
		Grid.SetColumn(info, 1);
		Grid.SetColumn(logout, 2);
		grid.Children.Add(avatar);
		grid.Children.Add(info);
		grid.Children.Add(logout);
		card.Child = grid;
		host.Children.Add(card);
		ShowMe();
	}

	private void ShowMe()
	{
		if (meStats == null)
		{
			return;
		}
		if (lastMe != null)
		{
			meStats.Text = L.T("За месяц: ") + Leaderboard.FormatHours(lastMe.Seconds) + (lastMe.Rank.HasValue ? (L.T(" · место #") + lastMe.Rank.Value) : L.T(" · пока не в топе"));
		}
		int inGame = main.InGameCount;
		meState.Text = (inGame > 0) ? (L.T("● Время идёт — в игре ") + inGame + L.T(" акк.")) : L.T("Время не идёт — нет аккаунтов в игре.");
		meState.Foreground = (inGame > 0) ? Palette.Green : Palette.Text3;
	}

	// ---------- пульс ----------

	private async System.Threading.Tasks.Task Beat()
	{
		if (!LoggedIn)
		{
			ShowMe();
			return;
		}
		try
		{
			lastMe = await Leaderboard.Heartbeat(BaseUrl, Token, main.InGameCount);
			ShowMe();
		}
		catch (Leaderboard.ApiException ex)
		{
			if (ex.Status == 401)
			{
				main.SetLeaderboardSession(null, null);
				main.AppLog(null, L.T("Топ АФК: сессия устарела — войди заново."), "DarkYellow");
				Build();
				return;
			}
			if (meState != null)
			{
				meState.Text = ex.Message;
				meState.Foreground = Palette.Yellow;
			}
		}
		if ((DateTime.UtcNow - topLoadedAt).TotalMinutes >= 5.0)
		{
			await LoadTop();
		}
	}

	// ---------- топ ----------

	private void BuildTop()
	{
		Border card = Card();
		card.Margin = new Thickness(0.0, 14.0, 0.0, 0.0);
		StackPanel panel = new StackPanel();
		Grid head = new Grid();
		StackPanel title = new StackPanel();
		title.Children.Add(new TextBlock
		{
			Text = L.T("Топ стендеров за месяц"),
			Style = (Style)host.FindResource("H2")
		});
		monthText = Hint("");
		title.Children.Add(monthText);
		Button refresh = new Button
		{
			Style = Res("IconBtn"),
			Content = "",
			ToolTip = L.T("Обновить"),
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Center
		};
		refresh.Click += async delegate
		{
			await LoadTop();
		};
		head.Children.Add(title);
		head.Children.Add(refresh);
		panel.Children.Add(head);
		topState = Hint(L.T("Загружаю топ…"));
		topState.Margin = new Thickness(0.0, 14.0, 0.0, 4.0);
		panel.Children.Add(topState);
		topList = new StackPanel
		{
			Margin = new Thickness(0.0, 10.0, 0.0, 0.0)
		};
		panel.Children.Add(topList);
		card.Child = panel;
		host.Children.Add(card);
		if (lastTop != null)
		{
			ShowTop(lastMonth, lastTop);
		}
	}

	private async System.Threading.Tasks.Task LoadTop()
	{
		if (topState != null && lastTop == null)
		{
			topState.Text = L.T("Загружаю топ… (сервер может просыпаться до минуты)");
		}
		try
		{
			Tuple<string, List<Leaderboard.Entry>> top = await Leaderboard.Top(BaseUrl);
			lastMonth = top.Item1;
			lastTop = top.Item2;
			topLoadedAt = DateTime.UtcNow;
			ShowTop(lastMonth, lastTop);
		}
		catch (Leaderboard.ApiException ex)
		{
			if (topState != null)
			{
				topState.Text = ex.Message;
				topState.Visibility = Visibility.Visible;
			}
		}
	}

	private void ShowTop(string month, List<Leaderboard.Entry> entries)
	{
		if (topList == null)
		{
			return;
		}
		monthText.Text = MonthName(month) + L.T(" · обновлено ") + DateTime.Now.ToString("HH:mm");
		topList.Children.Clear();
		topState.Visibility = (entries.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
		topState.Text = L.T("В этом месяце пока никто не стоял. Стань первым!");
		int i = 0;
		foreach (Leaderboard.Entry entry in entries)
		{
			FrameworkElement row = Row(entry, string.Equals(entry.Nickname, Nick, StringComparison.OrdinalIgnoreCase));
			topList.Children.Add(row);
			Anim.SlideIn(row, 10.0, 0.3, Math.Min(i, 12) * 0.03);
			i++;
		}
	}

	private FrameworkElement Row(Leaderboard.Entry entry, bool me)
	{
		Border row = new Border
		{
			CornerRadius = new CornerRadius(12.0),
			Padding = new Thickness(14.0, 10.0, 14.0, 10.0),
			Margin = new Thickness(0.0, 0.0, 0.0, 6.0),
			Background = me ? Palette.Make("#26143D") : Palette.Make("#0C0A18"),
			BorderBrush = me ? Palette.Make("#5B3FA6") : Palette.Make("#171329"),
			BorderThickness = new Thickness(1.0)
		};
		Grid grid = new Grid();
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(46.0)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		Brush medal = (entry.Rank == 1) ? Palette.Make("#FFD23F") : ((entry.Rank == 2) ? Palette.Make("#D7D0F5") : ((entry.Rank == 3) ? Palette.Make("#FFB020") : null));
		FrameworkElement rank;
		if (medal != null)
		{
			Grid badge = new Grid
			{
				Width = 28.0,
				Height = 28.0,
				HorizontalAlignment = HorizontalAlignment.Left
			};
			badge.Children.Add(new System.Windows.Shapes.Ellipse
			{
				Fill = medal,
				Opacity = 0.18
			});
			badge.Children.Add(new TextBlock
			{
				Text = entry.Rank.ToString(CultureInfo.InvariantCulture),
				Foreground = medal,
				FontWeight = FontWeights.Bold,
				FontSize = 13.0,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			});
			rank = badge;
		}
		else
		{
			rank = new TextBlock
			{
				Text = "#" + entry.Rank,
				Foreground = Palette.Text3,
				FontWeight = FontWeights.Bold,
				FontSize = 13.0,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(4.0, 0.0, 0.0, 0.0)
			};
		}
		StackPanel name = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			VerticalAlignment = VerticalAlignment.Center
		};
		name.Children.Add(new TextBlock
		{
			Text = entry.Nickname,
			FontSize = 14.0,
			FontWeight = FontWeights.SemiBold,
			Foreground = me ? ((Brush)host.FindResource("BrandText")) : Palette.Text
		});
		if (entry.Online)
		{
			name.Children.Add(new TextBlock
			{
				Text = L.T("● стоит"),
				Foreground = Palette.Green,
				FontSize = 11.5,
				FontWeight = FontWeights.SemiBold,
				Margin = new Thickness(10.0, 1.0, 0.0, 0.0),
				VerticalAlignment = VerticalAlignment.Center
			});
		}
		if (me)
		{
			name.Children.Add(new TextBlock
			{
				Text = L.T("это ты"),
				Foreground = Palette.Make("#C084FC"),
				FontSize = 11.5,
				Margin = new Thickness(10.0, 1.0, 0.0, 0.0),
				VerticalAlignment = VerticalAlignment.Center
			});
		}
		TextBlock hours = new TextBlock
		{
			Text = Leaderboard.FormatHours(entry.Seconds),
			FontSize = 14.0,
			FontWeight = FontWeights.Bold,
			Foreground = (medal ?? Palette.Text2),
			VerticalAlignment = VerticalAlignment.Center
		};
		Grid.SetColumn(name, 1);
		Grid.SetColumn(hours, 2);
		grid.Children.Add(rank);
		grid.Children.Add(name);
		grid.Children.Add(hours);
		row.Child = grid;
		return row;
	}

	// ---------- утилиты ----------

	private Border Card()
	{
		return new Border
		{
			Style = Res("Card")
		};
	}

	private TextBlock Hint(string text)
	{
		return new TextBlock
		{
			Text = text,
			Style = (Style)host.FindResource("Hint"),
			Margin = new Thickness(0.0, 3.0, 0.0, 0.0)
		};
	}

	private Button Btn(string text, string style)
	{
		return new Button
		{
			Content = text,
			Style = Res(style)
		};
	}

	private static string MonthName(string month)
	{
		if (DateTime.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
		{
			string name = date.ToString("MMMM yyyy", CultureInfo.GetCultureInfo(L.IsEnglish ? "en-US" : "ru-RU"));
			return char.ToUpper(name[0]) + name.Substring(1);
		}
		return month;
	}
}
