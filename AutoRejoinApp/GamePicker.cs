using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace AutoRejoinApp;

// "Choose a game" window: tiles with icons and online counts, search, a big preview of the selected game and
// "your own game" by link or place ID. Result: the chosen game (accounts join its public servers).
public class GamePicker
{
	private readonly Window window;

	private readonly WrapPanel tiles;

	private readonly TextBox search;

	private readonly Border previewImage;

	private readonly TextBlock previewName;

	private readonly TextBlock previewInfo;

	private readonly TextBlock previewNote;

	private readonly Button choose;

	private readonly TextBlock customError;

	private readonly List<GameCatalog.Game> games;

	private readonly Dictionary<GameCatalog.Game, Border> tileOf = new Dictionary<GameCatalog.Game, Border>();

	private GameCatalog.Game selected;

	public GameCatalog.Game Result { get; private set; }

	public Window WindowForTest => window;

	public GamePicker(string currentPlaceId, GameCatalog.Game custom)
	{
		games = new List<GameCatalog.Game>(GameCatalog.Games);
		if (custom != null && GameCatalog.Find(custom.PlaceId) == null)
		{
			games.Insert(0, custom);
		}
		window = new Window
		{
			Title = "AutoRejoin",
			Width = 980.0,
			Height = 760.0,
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
			if (e.OriginalSource == frame || e.OriginalSource is TextBlock || e.OriginalSource is StackPanel)
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
		Grid root = new Grid { Margin = new Thickness(26.0, 20.0, 26.0, 20.0) };
		root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
		root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

		// header
		Grid head = new Grid();
		StackPanel titles = new StackPanel();
		titles.Children.Add(new TextBlock { Text = L.T("Выбери игру"), FontSize = 20.0, FontWeight = FontWeights.SemiBold });
		titles.Children.Add(Hint(L.T("Аккаунты будут заходить на публичные сервера этой игры. Ссылка на сервер в настройках, если задана, важнее.")));
		head.Children.Add(titles);
		Button close = new Button { Content = "", FontSize = 10.0, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
		close.SetResourceReference(FrameworkElement.StyleProperty, "IconBtn");
		close.Click += delegate
		{
			window.Close();
		};
		head.Children.Add(close);
		root.Children.Add(head);

		// preview of the selected game
		Grid preview = new Grid { Margin = new Thickness(0.0, 16.0, 0.0, 0.0) };
		preview.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(352.0) });
		preview.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
		previewImage = new Border { Width = 352.0, Height = 198.0, CornerRadius = new CornerRadius(12.0), ClipToBounds = true };
		previewImage.SetResourceReference(Border.BackgroundProperty, "FieldBg");
		preview.Children.Add(previewImage);
		StackPanel info = new StackPanel { Margin = new Thickness(22.0, 4.0, 0.0, 0.0), VerticalAlignment = VerticalAlignment.Center };
		previewName = new TextBlock { FontSize = 22.0, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
		previewInfo = Hint("");
		previewInfo.FontSize = 13.0;
		previewNote = Hint("");
		previewNote.Margin = new Thickness(0.0, 10.0, 0.0, 0.0);
		info.Children.Add(previewName);
		info.Children.Add(previewInfo);
		info.Children.Add(previewNote);
		StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0.0, 16.0, 0.0, 0.0) };
		choose = new Button { Content = L.T("Играть в эту игру"), MinWidth = 170.0 };
		choose.SetResourceReference(FrameworkElement.StyleProperty, "BtnPrimary");
		choose.Click += delegate
		{
			if (selected != null)
			{
				Result = selected;
				window.Close();
			}
		};
		Button open = new Button { Content = L.T("Открыть на roblox.com"), Margin = new Thickness(16.0, 0.0, 0.0, 0.0), VerticalAlignment = VerticalAlignment.Center };
		open.SetResourceReference(FrameworkElement.StyleProperty, "LinkBtn");
		open.Click += delegate
		{
			if (selected != null)
			{
				try
				{
					Process.Start(new ProcessStartInfo("https://www.roblox.com/games/" + selected.PlaceId) { UseShellExecute = true });
				}
				catch
				{
				}
			}
		};
		actions.Children.Add(choose);
		actions.Children.Add(open);
		info.Children.Add(actions);
		Grid.SetColumn(info, 1);
		preview.Children.Add(info);
		Grid.SetRow(preview, 1);
		root.Children.Add(preview);

		// search
		search = new TextBox { Tag = L.T("Поиск игры…"), Margin = new Thickness(0.0, 18.0, 0.0, 10.0) };
		search.SetResourceReference(FrameworkElement.StyleProperty, "Field");
		search.TextChanged += delegate
		{
			Filter();
		};
		Grid.SetRow(search, 2);
		root.Children.Add(search);

		// tiles
		tiles = new WrapPanel();
		ScrollViewer scroll = new ScrollViewer { Content = tiles, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
		Grid.SetRow(scroll, 3);
		root.Children.Add(scroll);

		// own game
		Grid own = new Grid { Margin = new Thickness(0.0, 12.0, 0.0, 0.0) };
		own.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
		own.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		TextBox ownBox = new TextBox { Tag = L.T("Своя игра: ссылка на неё или ID плейса") };
		ownBox.SetResourceReference(FrameworkElement.StyleProperty, "Field");
		Button add = new Button { Content = L.T("Найти"), Margin = new Thickness(10.0, 0.0, 0.0, 0.0), MinWidth = 110.0 };
		add.SetResourceReference(FrameworkElement.StyleProperty, "Btn");
		add.Click += async delegate
		{
			customError.Visibility = Visibility.Collapsed;
			add.IsEnabled = false;
			try
			{
				GameCatalog.Game g = await GameCatalog.ResolveAsync(ownBox.Text);
				if (!games.Contains(g))
				{
					games.Insert(0, g);
				}
				BuildTiles();
				Select(g);
			}
			catch (Exception ex)
			{
				customError.Text = ex.Message;
				customError.Visibility = Visibility.Visible;
			}
			finally
			{
				add.IsEnabled = true;
			}
		};
		own.Children.Add(ownBox);
		Grid.SetColumn(add, 1);
		own.Children.Add(add);
		StackPanel bottom = new StackPanel();
		bottom.Children.Add(own);
		customError = Hint("");
		customError.Foreground = Palette.Red;
		customError.Visibility = Visibility.Collapsed;
		customError.Margin = new Thickness(2.0, 6.0, 0.0, 0.0);
		bottom.Children.Add(customError);
		Grid.SetRow(bottom, 4);
		root.Children.Add(bottom);

		frame.Child = root;
		window.Content = frame;
		window.KeyDown += delegate(object s, KeyEventArgs e)
		{
			if (e.Key == Key.Escape)
			{
				window.Close();
			}
		};
		BuildTiles();
		Select(games.FirstOrDefault((GameCatalog.Game g) => g.PlaceId == currentPlaceId) ?? games.First((GameCatalog.Game g) => g.PlaceId == GameCatalog.BeeSwarm));
		window.Loaded += async delegate
		{
			await GameCatalog.LoadLiveAsync(games);
			BuildTiles();
			Select(selected);
		};
	}

	public GameCatalog.Game ShowDialog(Window owner)
	{
		MessageDialog.ShowModal(window, owner);
		return Result;
	}

	private void BuildTiles()
	{
		tiles.Children.Clear();
		tileOf.Clear();
		foreach (GameCatalog.Game g in games)
		{
			Border tile = new Border
			{
				Width = 134.0,
				Margin = new Thickness(0.0, 0.0, 10.0, 10.0),
				Padding = new Thickness(8.0),
				CornerRadius = new CornerRadius(12.0),
				BorderThickness = new Thickness(1.5),
				Cursor = Cursors.Hand,
				Background = Brushes.Transparent,
				Tag = g
			};
			tile.SetResourceReference(Border.BorderBrushProperty, "Line");
			StackPanel p = new StackPanel();
			Border icon = new Border { Width = 116.0, Height = 116.0, CornerRadius = new CornerRadius(10.0) };
			icon.SetResourceReference(Border.BackgroundProperty, "FieldBg");
			if (!string.IsNullOrEmpty(g.IconUrl))
			{
				icon.Background = new ImageBrush(Load(g.IconUrl, 244)) { Stretch = Stretch.UniformToFill };
			}
			else
			{
				icon.Child = new TextBlock { Text = g.Name.Substring(0, 1), FontSize = 40.0, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Palette.Text3 };
			}
			p.Children.Add(icon);
			p.Children.Add(new TextBlock { Text = g.Name, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxHeight = 36.0, Margin = new Thickness(2.0, 8.0, 0.0, 0.0), TextTrimming = TextTrimming.CharacterEllipsis });
			TextBlock online = Hint(g.Playing >= 0 ? GameCatalog.Online(g.Playing) : L.T(g.Genre));
			online.FontSize = 11.5;
			online.Margin = new Thickness(2.0, 2.0, 0.0, 0.0);
			p.Children.Add(online);
			tile.Child = p;
			tile.MouseLeftButtonUp += delegate
			{
				Select(g);
			};
			tile.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
			{
				if (e.ClickCount == 2)
				{
					Select(g);
					Result = g;
					window.Close();
				}
			};
			tile.MouseEnter += delegate
			{
				if (selected != g)
				{
					tile.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
				}
			};
			tile.MouseLeave += delegate
			{
				if (selected != g)
				{
					tile.SetResourceReference(Border.BorderBrushProperty, "Line");
				}
			};
			tileOf[g] = tile;
			tiles.Children.Add(tile);
		}
		Filter();
	}

	private void Filter()
	{
		string q = (search?.Text ?? "").Trim();
		foreach (KeyValuePair<GameCatalog.Game, Border> pair in tileOf)
		{
			bool show = q.Length == 0 || pair.Key.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || (pair.Key.LiveName ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || (pair.Key.Genre ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
			pair.Value.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
		}
	}

	private async void Select(GameCatalog.Game g)
	{
		if (g == null)
		{
			return;
		}
		selected = g;
		foreach (KeyValuePair<GameCatalog.Game, Border> pair in tileOf)
		{
			pair.Value.SetResourceReference(Border.BorderBrushProperty, pair.Key == g ? "Accent" : "Line");
		}
		previewName.Text = g.Name;
		List<string> parts = new List<string>();
		if (!string.IsNullOrEmpty(g.Genre))
		{
			parts.Add(L.T(g.Genre));
		}
		if (g.Playing >= 0)
		{
			parts.Add(GameCatalog.Online(g.Playing));
		}
		parts.Add("ID " + g.PlaceId);
		previewInfo.Text = string.Join(" · ", parts);
		previewNote.Text = g.PlaceId == GameCatalog.BeeSwarm
			? L.T("Все функции: проверка скрипта, статистика мёда, Atlas, Balloon-перезаход.")
			: L.T("Перезаход, вылеты, макросы, Discord и Telegram работают в любой игре. Функции для Bee Swarm (мёд, Atlas, Balloon) здесь не нужны.");
		previewImage.Child = null;
		previewImage.SetResourceReference(Border.BackgroundProperty, "FieldBg");
		if (!string.IsNullOrEmpty(g.IconUrl))
		{
			previewImage.Background = new ImageBrush(Load(g.IconUrl, 384)) { Stretch = Stretch.UniformToFill, Opacity = 0.35 };
		}
		await GameCatalog.LoadPreviewAsync(g);
		if (selected == g && !string.IsNullOrEmpty(g.PreviewUrl))
		{
			previewImage.Background = new ImageBrush(Load(g.PreviewUrl, 768)) { Stretch = Stretch.UniformToFill };
		}
	}

	private static BitmapImage Load(string url, int width)
	{
		BitmapImage image = new BitmapImage();
		image.BeginInit();
		image.UriSource = new Uri(url);
		image.DecodePixelWidth = width;
		image.CacheOption = BitmapCacheOption.OnLoad;
		image.EndInit();
		return image;
	}

	private static TextBlock Hint(string text)
	{
		TextBlock t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0.0, 3.0, 0.0, 0.0) };
		t.SetResourceReference(FrameworkElement.StyleProperty, "Hint");
		return t;
	}
}
