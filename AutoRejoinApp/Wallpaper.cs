using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace AutoRejoinApp;

// Custom wallpaper behind the whole window. The picture is copied into the data folder (wallpaper.<ext>), so it keeps
// working if the original file is moved. Dim and blur apply at once; semi-transparent cards and sidebar — from the
// next start (the theme styles are built when the window loads).
public static class Wallpaper
{
	public static readonly string[] Extensions = new string[5] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };

	public static string FilePath(AppConfig c)
	{
		if (c == null || string.IsNullOrEmpty(c.wallpaper))
		{
			return null;
		}
		string path = Path.Combine(AppPaths.DataDir, Path.GetFileName(c.wallpaper));
		return File.Exists(path) ? path : null;
	}

	/// Copies the picture into the data folder and returns its file name there.
	public static string Import(string source)
	{
		string ext = Path.GetExtension(source).ToLowerInvariant();
		if (!Extensions.Contains(ext))
		{
			throw new Exception(L.T("нужна картинка: jpg, png, bmp или gif"));
		}
		if (new FileInfo(source).Length > 40L * 1024 * 1024)
		{
			throw new Exception(L.T("картинка больше 40 МБ"));
		}
		// Check that it really is a picture before replacing the old one.
		BitmapImage test = Load(source, 64);
		if (test.PixelWidth == 0)
		{
			throw new Exception(L.T("не получилось открыть картинку"));
		}
		string name = "wallpaper-" + DateTime.Now.ToString("yyyyMMddHHmmss") + ext;
		File.Copy(source, Path.Combine(AppPaths.DataDir, name), overwrite: true);
		RemoveOld(name);
		return name;
	}

	public static void RemoveOld(string keep)
	{
		foreach (string file in Directory.GetFiles(AppPaths.DataDir, "wallpaper-*"))
		{
			if (!string.Equals(Path.GetFileName(file), keep, StringComparison.OrdinalIgnoreCase))
			{
				try
				{
					File.Delete(file);
				}
				catch
				{
				}
			}
		}
	}

	private static BitmapImage Load(string path, int decodeWidth)
	{
		BitmapImage image = new BitmapImage();
		image.BeginInit();
		image.CacheOption = BitmapCacheOption.OnLoad;
		image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
		image.UriSource = new Uri(path);
		if (decodeWidth > 0)
		{
			image.DecodePixelWidth = decodeWidth;
		}
		image.EndInit();
		image.Freeze();
		return image;
	}

	/// Before the main window loads: with a wallpaper, cards and the sidebar get see-through backgrounds.
	public static void PrepareTheme(ResourceDictionary theme)
	{
		try
		{
			AppConfig c = Store.LoadConfig();
			if (FilePath(c) == null)
			{
				return;
			}
			byte alpha = (byte)Math.Round(255.0 * Math.Max(20, Math.Min(100, c.wallpaperCards)) / 100.0);
			Color cardColor = theme["CardBg"] is SolidColorBrush card ? card.Color : Colors.Black;
			SolidColorBrush cardBrush = new SolidColorBrush(Color.FromArgb(alpha, cardColor.R, cardColor.G, cardColor.B));
			cardBrush.Freeze();
			if (theme["Card"] is Style old)
			{
				Style style = new Style(typeof(Border), old);
				style.Setters.Add(new Setter(Border.BackgroundProperty, cardBrush));
				theme["Card"] = style;
			}
			Color side = theme["SidebarBg"] is SolidColorBrush sb ? sb.Color : Colors.Black;
			SolidColorBrush sideBrush = new SolidColorBrush(Color.FromArgb((byte)Math.Min(alpha, side.A), side.R, side.G, side.B));
			sideBrush.Freeze();
			theme["SidebarBg"] = sideBrush;
			foreach (string key in new string[2] { "StatTileBg", "FieldBg" })
			{
				if (theme[key] is SolidColorBrush b)
				{
					SolidColorBrush nb = new SolidColorBrush(Color.FromArgb((byte)Math.Min(255, alpha + 20), b.Color.R, b.Color.G, b.Color.B));
					nb.Freeze();
					theme[key] = nb;
				}
			}
		}
		catch
		{
		}
	}

	/// Shows (or hides) the wallpaper with the current dim and blur.
	public static void Apply(Window window, AppConfig c)
	{
		if (!(window.FindName("WallpaperImage") is Image image) || !(window.FindName("WallpaperDim") is Border dim))
		{
			return;
		}
		string path = FilePath(c);
		if (path == null)
		{
			image.Source = null;
			image.Visibility = Visibility.Collapsed;
			dim.Visibility = Visibility.Collapsed;
			return;
		}
		try
		{
			image.Source = Load(path, 2560);
		}
		catch (Exception ex)
		{
			App.LogError(L.T("обои"), ex);
			image.Visibility = Visibility.Collapsed;
			dim.Visibility = Visibility.Collapsed;
			return;
		}
		int blur = Math.Max(0, Math.Min(40, c.wallpaperBlur));
		image.Effect = blur > 0 ? new BlurEffect
		{
			Radius = blur,
			KernelType = KernelType.Gaussian
		} : null;
		image.Margin = new Thickness(-blur * 2);
		image.Visibility = Visibility.Visible;
		dim.Opacity = Math.Max(0, Math.Min(95, c.wallpaperDim)) / 100.0;
		dim.Visibility = Visibility.Visible;
	}
}
