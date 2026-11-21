using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AutoRejoinApp;

public static class MessageDialog
{
	public enum Kind
	{
		Info,
		Warning,
		Danger,
		Success
	}

	public static bool Ask(Window owner, string title, string message, string okText, Kind kind, string cancelText)
	{
		Window window = Xaml.Load<Window>("MessageDialog.xaml");
		Button button = Xaml.Find<Button>(window, "OkButton");
		Button button2 = Xaml.Find<Button>(window, "CancelButton");
		Xaml.Find<TextBlock>(window, "TitleText").Text = title;
		Xaml.Find<TextBlock>(window, "MessageText").Text = message;
		button.Content = okText;
		if (cancelText == null)
		{
			button2.Visibility = Visibility.Collapsed;
		}
		else
		{
			button2.Content = cancelText;
		}
		Ico textBlock = Xaml.Find<Ico>(window, "IconText");
		Border border = Xaml.Find<Border>(window, "IconBox");
		switch (kind)
		{
		case Kind.Warning:
			textBlock.Glyph = "\ue7ba";
			textBlock.Foreground = Palette.Yellow;
			border.Background = Palette.YellowSoft;
			break;
		case Kind.Danger:
			textBlock.Glyph = "\ue7ba";
			textBlock.Foreground = Palette.Red;
			border.Background = Palette.RedSoft;
			button.Style = (Style)window.FindResource("BtnDanger");
			break;
		case Kind.Success:
			textBlock.Glyph = "\ue73e";
			textBlock.Foreground = Palette.Green;
			border.Background = Palette.GreenSoft;
			break;
		}
		bool result = false;
		button.Click += delegate
		{
			result = true;
			window.Close();
		};
		button2.Click += delegate
		{
			window.Close();
		};
		window.KeyDown += delegate(object s, KeyEventArgs e)
		{
			if (e.Key == Key.Escape)
			{
				window.Close();
			}
			else if (e.Key == Key.Return)
			{
				result = true;
				window.Close();
			}
		};
		Xaml.Find<Border>(window, "Frame").MouseLeftButtonDown += delegate
		{
			try
			{
				window.DragMove();
			}
			catch
			{
			}
		};
		ShowModal(window, owner);
		return result;
	}

	public static void Show(Window owner, string title, string message, Kind kind)
	{
		Ask(owner, title, message, "OK", kind, null);
	}

	public static void ShowModal(Window window, Window owner)
	{
		if (owner != null && owner.IsVisible)
		{
			window.Owner = owner;
			window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		}
		else
		{
			window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
			window.Topmost = true;
		}
		window.ShowDialog();
	}
}
