using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace AutoRejoinApp;

// A VIP server for one game (Settings -> Server -> VIP servers per game): accounts that go to this game without their
// own server link join this VIP instead of public servers.
public class VipLink
{
	public string placeId { get; set; }

	public string gameName { get; set; }

	public long universeId { get; set; }

	public string link { get; set; }

	public VipLink()
	{
		placeId = "";
		gameName = "";
		link = "";
	}
}

// A script that runs only in one game (Settings -> Executor -> Scripts per game). Our autoexec marker loads
// workspace/autorejoin_scripts/<universeId>.lua when the window is in that game (lobby and matches alike).
public class GameScript
{
	public string placeId { get; set; }

	public string gameName { get; set; }

	public long universeId { get; set; }

	public bool enabled { get; set; }

	public string script { get; set; }

	public GameScript()
	{
		placeId = "";
		gameName = "";
		enabled = true;
		script = "";
	}
}

public static class GameScripts
{
	public const string Folder = "autorejoin_scripts";

	// Writes the enabled scripts into the chosen executor's workspace and removes the rest. The folder is ours only.
	public static void Sync(IEnumerable<GameScript> scripts)
	{
		try
		{
			string exec = HoneyStats.ExecDir;
			if (string.IsNullOrEmpty(exec) || !Directory.Exists(exec))
			{
				return;
			}
			string dir = Path.Combine(exec, "workspace", Folder);
			Dictionary<string, string> wanted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (GameScript g in scripts ?? Enumerable.Empty<GameScript>())
			{
				if (g != null && g.enabled && g.universeId > 0 && !string.IsNullOrWhiteSpace(g.script))
				{
					wanted[g.universeId + ".lua"] = g.script;
				}
			}
			if (wanted.Count == 0 && !Directory.Exists(dir))
			{
				return;
			}
			Directory.CreateDirectory(dir);
			UTF8Encoding utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
			foreach (KeyValuePair<string, string> pair in wanted)
			{
				string path = Path.Combine(dir, pair.Key);
				if (!File.Exists(path) || File.ReadAllText(path, Encoding.UTF8) != pair.Value)
				{
					File.WriteAllText(path, pair.Value, utf8);
				}
			}
			foreach (string file in Directory.GetFiles(dir, "*.lua"))
			{
				if (!wanted.ContainsKey(Path.GetFileName(file)))
				{
					File.Delete(file);
				}
			}
		}
		catch (Exception ex)
		{
			App.LogError(L.T("скрипты по играм"), ex);
		}
	}
}

// "Script for <game>" window: a big monospace box to paste the script into.
public class ScriptEditor
{
	private readonly Window window;

	private readonly TextBox box;

	public string Result { get; private set; }

	public Window WindowForTest => window;

	public ScriptEditor(string gameName, string script)
	{
		window = new Window
		{
			Title = "AutoRejoin",
			Width = 760.0,
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
		StackPanel root = new StackPanel { Margin = new Thickness(26.0, 20.0, 26.0, 20.0) };
		root.Children.Add(new TextBlock { Text = L.T("Скрипт для ") + gameName, FontSize = 20.0, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
		root.Children.Add(Hint(L.T("Запускается через автоэкзекут экзекьютора только в этой игре — в лобби и в матчах. Вставь скрипт целиком, например loadstring(game:HttpGet(\"…\"))().")));
		box = new TextBox
		{
			Text = script ?? "",
			Height = 360.0,
			Margin = new Thickness(0.0, 14.0, 0.0, 0.0),
			AcceptsReturn = true,
			AcceptsTab = true,
			TextWrapping = TextWrapping.NoWrap,
			VerticalContentAlignment = VerticalAlignment.Top,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			FontFamily = new FontFamily("Consolas"),
			FontSize = 12.5,
			Tag = "-- loadstring(game:HttpGet(\"https://...\"))()"
		};
		box.SetResourceReference(FrameworkElement.StyleProperty, "Field");
		root.Children.Add(box);
		StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0.0, 16.0, 0.0, 0.0) };
		Button cancel = new Button { Content = L.T("Отмена"), MinWidth = 104.0, Margin = new Thickness(0.0, 0.0, 10.0, 0.0) };
		cancel.SetResourceReference(FrameworkElement.StyleProperty, "BtnGhost");
		cancel.Click += delegate
		{
			window.Close();
		};
		Button save = new Button { Content = L.T("Сохранить"), MinWidth = 130.0 };
		save.SetResourceReference(FrameworkElement.StyleProperty, "BtnPrimary");
		save.Click += delegate
		{
			Result = box.Text ?? "";
			window.Close();
		};
		buttons.Children.Add(cancel);
		buttons.Children.Add(save);
		root.Children.Add(buttons);
		frame.Child = root;
		window.Content = frame;
		window.KeyDown += delegate(object s, KeyEventArgs e)
		{
			if (e.Key == Key.Escape)
			{
				window.Close();
			}
		};
		window.Loaded += delegate
		{
			box.Focus();
			box.CaretIndex = box.Text.Length;
		};
	}

	private static TextBlock Hint(string text)
	{
		TextBlock t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0.0, 4.0, 0.0, 0.0) };
		t.SetResourceReference(FrameworkElement.StyleProperty, "Hint");
		return t;
	}

	// The new script text, or null when cancelled.
	public string ShowDialog(Window owner)
	{
		MessageDialog.ShowModal(window, owner);
		return Result;
	}
}
