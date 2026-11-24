using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace AutoRejoinApp;

// Modal window for the AFK top account: sign in (nickname or email + password) or sign up
// (email, nickname, password twice → a 6-digit code from the email → the account is created and signed in).
public class AuthDialog
{
	private static readonly Regex NickRe = new Regex("^[A-Za-z0-9_]{3,20}$");

	private static readonly Regex EmailRe = new Regex("^[^\\s@]+@[^\\s@]+\\.[^\\s@]{2,}$");

	private readonly string baseUrl;

	private readonly Window window;

	private readonly RadioButton tabLogin;

	private readonly RadioButton tabRegister;

	private readonly StackPanel loginPanel;

	private readonly StackPanel registerPanel;

	private readonly StackPanel codePanel;

	private readonly Border segment;

	private readonly TextBox loginName;

	private readonly PasswordBox loginPass;

	private readonly TextBox regEmail;

	private readonly TextBox regNick;

	private readonly PasswordBox regPass;

	private readonly PasswordBox regPass2;

	private readonly TextBox codeBox;

	private readonly TextBlock codeHint;

	private readonly TextBlock error;

	private readonly TextBlock title;

	private readonly Button primary;

	private readonly Button cancel;

	private bool busy;

	private bool codeStep;

	private string pendingEmail;

	public string Token { get; private set; }

	public string Nickname { get; private set; }

	public bool Registered { get; private set; }

	public AuthDialog(string baseUrl, bool register)
	{
		this.baseUrl = baseUrl;
		window = new Window
		{
			Title = "AutoRejoin",
			Width = 460.0,
			SizeToContent = SizeToContent.Height,
			WindowStyle = WindowStyle.None,
			AllowsTransparency = true,
			Background = Brushes.Transparent,
			ResizeMode = ResizeMode.NoResize,
			ShowInTaskbar = false,
			FontFamily = new FontFamily("Segoe UI"),
			FontSize = 13.0,
			UseLayoutRounding = true
		};
		window.SetResourceReference(Control.ForegroundProperty, "Text");
		TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);

		Border frame = new Border
		{
			Margin = new Thickness(18.0),
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(16.0),
			Effect = new DropShadowEffect { BlurRadius = 26.0, ShadowDepth = 0.0, Opacity = 0.6, Color = Colors.Black }
		};
		frame.SetResourceReference(Border.BackgroundProperty, "CardBg");
		frame.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
		frame.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
		{
			if (e.OriginalSource is Border || e.OriginalSource is StackPanel || e.OriginalSource is TextBlock)
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
		StackPanel root = new StackPanel { Margin = new Thickness(26.0, 22.0, 26.0, 22.0) };

		// header: icon, title, close
		Grid head = new Grid();
		StackPanel headLeft = new StackPanel { Orientation = Orientation.Horizontal };
		Border iconBox = new Border { Width = 40.0, Height = 40.0, CornerRadius = new CornerRadius(11.0) };
		iconBox.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
		Ico icon = new Ico { Glyph = "trophy", Size = 20.0, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
		icon.SetResourceReference(Ico.ForegroundProperty, "Accent");
		iconBox.Child = icon;
		headLeft.Children.Add(iconBox);
		StackPanel titles = new StackPanel { Margin = new Thickness(14.0, 0.0, 0.0, 0.0), VerticalAlignment = VerticalAlignment.Center };
		title = new TextBlock { FontSize = 17.0, FontWeight = FontWeights.SemiBold };
		titles.Children.Add(title);
		titles.Children.Add(Hint(L.T("Один аккаунт для программы и сайта (Топ АФК)."), 0.0));
		headLeft.Children.Add(titles);
		head.Children.Add(headLeft);
		Button close = new Button { Content = "", FontSize = 10.0, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
		close.SetResourceReference(FrameworkElement.StyleProperty, "IconBtn");
		close.Click += delegate
		{
			window.Close();
		};
		head.Children.Add(close);
		root.Children.Add(head);

		// tabs
		segment = new Border { BorderThickness = new Thickness(1.0), CornerRadius = new CornerRadius(11.0), Padding = new Thickness(4.0), Margin = new Thickness(0.0, 20.0, 0.0, 0.0) };
		segment.SetResourceReference(Border.BackgroundProperty, "FieldBg");
		segment.SetResourceReference(Border.BorderBrushProperty, "Line");
		UniformGrid tabs = new UniformGrid { Columns = 2 };
		tabLogin = new RadioButton { GroupName = "AuthTab", Tag = "user", Content = L.T("Вход") };
		tabRegister = new RadioButton { GroupName = "AuthTab", Tag = "plus", Content = L.T("Регистрация") };
		tabLogin.SetResourceReference(FrameworkElement.StyleProperty, "Segment");
		tabRegister.SetResourceReference(FrameworkElement.StyleProperty, "Segment");
		tabs.Children.Add(tabLogin);
		tabs.Children.Add(tabRegister);
		segment.Child = tabs;
		root.Children.Add(segment);

		// sign in
		loginPanel = new StackPanel();
		loginPanel.Children.Add(Label(L.T("НИК ИЛИ ПОЧТА")));
		loginName = Field(L.T("Ник или почта"), 254);
		loginPanel.Children.Add(loginName);
		Grid passHead = new Grid();
		passHead.Children.Add(Label(L.T("ПАРОЛЬ")));
		Button forgot = new Button { Content = L.T("Забыл пароль?"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0.0, 0.0, 0.0, 7.0) };
		forgot.SetResourceReference(FrameworkElement.StyleProperty, "LinkBtn");
		forgot.Click += delegate
		{
			OpenUrl("https://clxwnlessss.github.io/AutoRejoin/forgot");
		};
		passHead.Children.Add(forgot);
		loginPanel.Children.Add(passHead);
		loginPass = Pass();
		loginPanel.Children.Add(loginPass);
		root.Children.Add(loginPanel);

		// sign up
		registerPanel = new StackPanel();
		registerPanel.Children.Add(Label(L.T("ПОЧТА")));
		regEmail = Field("name@gmail.com", 254);
		registerPanel.Children.Add(regEmail);
		registerPanel.Children.Add(Hint(L.T("На неё придёт код подтверждения. Нужна, чтобы восстановить пароль."), 6.0));
		registerPanel.Children.Add(Label(L.T("НИК")));
		regNick = Field(L.T("3–20: латиница, цифры, _"), 20);
		registerPanel.Children.Add(regNick);
		registerPanel.Children.Add(Hint(L.T("Виден в Топе АФК."), 6.0));
		registerPanel.Children.Add(Label(L.T("ПАРОЛЬ")));
		regPass = Pass();
		registerPanel.Children.Add(regPass);
		registerPanel.Children.Add(Label(L.T("ПОВТОРИ ПАРОЛЬ")));
		regPass2 = Pass();
		registerPanel.Children.Add(regPass2);
		root.Children.Add(registerPanel);

		// email code
		codePanel = new StackPanel { Visibility = Visibility.Collapsed };
		codeHint = Hint("", 16.0);
		codeHint.FontSize = 13.0;
		codePanel.Children.Add(codeHint);
		codePanel.Children.Add(Label(L.T("КОД ИЗ ПИСЬМА")));
		codeBox = Field("••••••", 6);
		codeBox.FontSize = 22.0;
		codeBox.FontWeight = FontWeights.Bold;
		codeBox.HorizontalContentAlignment = HorizontalAlignment.Center;
		codeBox.PreviewTextInput += delegate(object s, TextCompositionEventArgs e)
		{
			e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");
		};
		codePanel.Children.Add(codeBox);
		StackPanel codeLinks = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2.0, 10.0, 0.0, 0.0) };
		Button resend = new Button { Content = L.T("Прислать ещё раз") };
		resend.SetResourceReference(FrameworkElement.StyleProperty, "LinkBtn");
		resend.Click += async delegate
		{
			await SendSignupCode();
		};
		Button back = new Button { Content = L.T("Изменить данные"), Margin = new Thickness(16.0, 0.0, 0.0, 0.0) };
		back.SetResourceReference(FrameworkElement.StyleProperty, "LinkBtn");
		back.Click += delegate
		{
			SetCodeStep(on: false);
		};
		codeLinks.Children.Add(resend);
		codeLinks.Children.Add(back);
		codePanel.Children.Add(codeLinks);
		root.Children.Add(codePanel);

		error = Hint("", 14.0);
		error.Foreground = Palette.Red;
		error.Visibility = Visibility.Collapsed;
		root.Children.Add(error);

		StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0.0, 22.0, 0.0, 0.0) };
		cancel = new Button { Content = L.T("Отмена") };
		cancel.SetResourceReference(FrameworkElement.StyleProperty, "Btn");
		cancel.Click += delegate
		{
			window.Close();
		};
		primary = new Button { Margin = new Thickness(10.0, 0.0, 0.0, 0.0), MinWidth = 150.0 };
		primary.SetResourceReference(FrameworkElement.StyleProperty, "BtnPrimary");
		primary.Click += async delegate
		{
			await Submit();
		};
		buttons.Children.Add(cancel);
		buttons.Children.Add(primary);
		root.Children.Add(buttons);

		frame.Child = root;
		window.Content = frame;

		tabLogin.Checked += delegate
		{
			ShowTab(register: false);
		};
		tabRegister.Checked += delegate
		{
			ShowTab(register: true);
		};
		window.PreviewKeyDown += async delegate(object s, KeyEventArgs e)
		{
			if (e.Key == Key.Escape)
			{
				window.Close();
			}
			else if (e.Key == Key.Return)
			{
				e.Handled = true;
				await Submit();
			}
		};
		if (register)
		{
			tabRegister.IsChecked = true;
		}
		else
		{
			tabLogin.IsChecked = true;
		}
		ShowTab(register);
	}

	public Window WindowForTest => window;

	// Screenshot mode: show the email-code step without talking to the server.
	internal void ShowCodeStepForTest(string email)
	{
		tabRegister.IsChecked = true;
		pendingEmail = email;
		SetCodeStep(on: true);
	}

	public bool ShowDialog(Window owner)
	{
		window.Loaded += delegate
		{
			Focus();
		};
		MessageDialog.ShowModal(window, owner);
		return Token != null;
	}

	private bool IsRegister => tabRegister.IsChecked == true;

	private void ShowTab(bool register)
	{
		if (codeStep)
		{
			SetCodeStep(on: false);
		}
		loginPanel.Visibility = register ? Visibility.Collapsed : Visibility.Visible;
		registerPanel.Visibility = register ? Visibility.Visible : Visibility.Collapsed;
		title.Text = register ? L.T("Создать аккаунт") : L.T("Вход в аккаунт");
		primary.Content = register ? L.T("Продолжить") : L.T("Войти");
		HideError();
		Focus();
	}

	private void SetCodeStep(bool on)
	{
		codeStep = on;
		registerPanel.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
		segment.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
		codePanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
		title.Text = on ? L.T("Подтверди почту") : L.T("Создать аккаунт");
		primary.Content = on ? L.T("Подтвердить") : L.T("Продолжить");
		if (on)
		{
			codeBox.Text = "";
			codeHint.Text = string.Format(L.T("Мы отправили 6-значный код на {0}. Проверь и «Спам»."), pendingEmail);
		}
		HideError();
		Focus();
	}

	private void Focus()
	{
		window.Dispatcher.BeginInvoke((Action)delegate
		{
			Control target = codeStep ? codeBox : (IsRegister ? (string.IsNullOrEmpty(regEmail.Text) ? regEmail : regNick) : (string.IsNullOrEmpty(loginName.Text) ? loginName : loginPass));
			target.Focus();
		}, System.Windows.Threading.DispatcherPriority.Input);
	}

	private async Task Submit()
	{
		if (busy)
		{
			return;
		}
		HideError();
		if (codeStep)
		{
			await ConfirmCode();
		}
		else if (IsRegister)
		{
			await SendSignupCode();
		}
		else
		{
			await Login();
		}
	}

	private async Task Login()
	{
		string name = (loginName.Text ?? "").Trim();
		if (name.Length == 0 || loginPass.Password.Length == 0)
		{
			ShowError(L.T("Введи ник (или почту) и пароль."));
			return;
		}
		await Run(L.T("Вхожу…"), async delegate
		{
			Tuple<string, string> session = await Leaderboard.Login(baseUrl, name, loginPass.Password);
			Done(session.Item1, session.Item2, registered: false);
		});
	}

	private async Task SendSignupCode()
	{
		string email = (regEmail.Text ?? "").Trim();
		string nick = (regNick.Text ?? "").Trim();
		if (!EmailRe.IsMatch(email))
		{
			ShowError(L.T("Похоже, это не почта."));
			return;
		}
		if (!NickRe.IsMatch(nick))
		{
			ShowError(L.T("Ник: 3–20 символов, латиница, цифры и _."));
			return;
		}
		if (regPass.Password.Length < 6)
		{
			ShowError(L.T("Пароль: от 6 символов."));
			return;
		}
		if (regPass.Password != regPass2.Password)
		{
			ShowError(L.T("Пароли не совпадают."));
			return;
		}
		await Run(L.T("Отправляю код…"), async delegate
		{
			pendingEmail = await Leaderboard.RegisterStart(baseUrl, nick, email, regPass.Password) ?? email;
			if (!codeStep)
			{
				SetCodeStep(on: true);
			}
			else
			{
				codeHint.Text = string.Format(L.T("Новый код отправлен на {0}."), pendingEmail);
			}
		});
	}

	private async Task ConfirmCode()
	{
		string code = (codeBox.Text ?? "").Trim();
		if (!Regex.IsMatch(code, "^[0-9]{6}$"))
		{
			ShowError(L.T("Код — 6 цифр из письма."));
			return;
		}
		await Run(L.T("Проверяю…"), async delegate
		{
			Tuple<string, string> session = await Leaderboard.RegisterConfirm(baseUrl, pendingEmail, code);
			Done(session.Item1, session.Item2, registered: true);
		});
	}

	private async Task Run(string working, Func<Task> action)
	{
		busy = true;
		object label = primary.Content;
		primary.Content = working;
		primary.IsEnabled = cancel.IsEnabled = tabLogin.IsEnabled = tabRegister.IsEnabled = false;
		try
		{
			await action();
		}
		catch (Leaderboard.ApiException ex)
		{
			ShowError(ex.Message);
		}
		catch (Exception ex2)
		{
			ShowError(ex2.Message);
		}
		finally
		{
			busy = false;
			if (Token == null)
			{
				primary.Content = codeStep ? L.T("Подтвердить") : label;
				primary.IsEnabled = cancel.IsEnabled = tabLogin.IsEnabled = tabRegister.IsEnabled = true;
			}
		}
	}

	private void Done(string token, string nickname, bool registered)
	{
		Token = token;
		Nickname = nickname;
		Registered = registered;
		window.Close();
	}

	private void ShowError(string text)
	{
		error.Text = text;
		error.Visibility = Visibility.Visible;
	}

	private void HideError()
	{
		error.Visibility = Visibility.Collapsed;
	}

	private static void OpenUrl(string url)
	{
		try
		{
			Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
		}
		catch
		{
		}
	}

	private static TextBlock Label(string text)
	{
		TextBlock t = new TextBlock { Text = text, Margin = new Thickness(0.0, 16.0, 0.0, 7.0) };
		t.SetResourceReference(FrameworkElement.StyleProperty, "Label");
		return t;
	}

	private static TextBlock Hint(string text, double top)
	{
		TextBlock t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2.0, top, 0.0, 0.0) };
		t.SetResourceReference(FrameworkElement.StyleProperty, "Hint");
		return t;
	}

	private static TextBox Field(string placeholder, int max)
	{
		TextBox t = new TextBox { Tag = placeholder, MaxLength = max };
		t.SetResourceReference(FrameworkElement.StyleProperty, "Field");
		return t;
	}

	private static PasswordBox Pass()
	{
		PasswordBox p = new PasswordBox { MaxLength = 100 };
		p.SetResourceReference(FrameworkElement.StyleProperty, "PasswordField");
		return p;
	}
}
