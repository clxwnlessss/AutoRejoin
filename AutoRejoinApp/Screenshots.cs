using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AutoRejoinApp;

internal static class Screenshots
{
	public static void Run(Application app, Window window, MainController controller, string dir, string fakeStatus, string fakeLog)
	{
		Directory.CreateDirectory(dir);
		window.ShowActivated = false;
		window.ShowInTaskbar = false;
		window.WindowStartupLocation = WindowStartupLocation.Manual;
		window.Left = -20000.0;
		window.Top = -20000.0;
		window.ContentRendered += async delegate
		{
			try
			{
				controller.FeedFake(fakeStatus, fakeLog);
				foreach (string page2 in new string[6] { "accounts", "top", "schedule", "settings", "log", "help" })
				{
					controller.ShowPage(page2);
					await Task.Delay((page2 == "top") ? 2500 : 500);
					Save((FrameworkElement)window.Content, Path.Combine(dir, page2 + ".png"));
				}
				foreach (string page in new string[3] { "settings", "schedule", "help" })
				{
					controller.ShowPage(page);
					await Task.Delay(300);
					if (window.FindName("Page" + char.ToUpper(page[0]) + page.Substring(1)) is ScrollViewer scroll && scroll.Content is FrameworkElement)
					{
						Save((FrameworkElement)scroll.Content, Path.Combine(dir, page + "_full.png"));
					}
				}
				controller.ShowPage("accounts");
				AccountDialog dialog = new AccountDialog(null, (string a, string b) => (string)null, suggestMain: true, autoAccept: false);
				Window dialogWindow = dialog.WindowForTest;
				dialogWindow.WindowStartupLocation = WindowStartupLocation.Manual;
				dialogWindow.Left = -20000.0;
				dialogWindow.Top = -20000.0;
				dialogWindow.ShowActivated = false;
				dialogWindow.Show();
				await Task.Delay(500);
				Save((FrameworkElement)dialogWindow.Content, Path.Combine(dir, "dialog_add.png"));
				dialogWindow.Close();
			}
			catch (Exception ex)
			{
				App.LogError("снимки", ex);
				File.WriteAllText(Path.Combine(dir, "error.txt"), ex.ToString());
			}
			controller.DisposeTray();
			app.Shutdown();
		};
		window.Show();
		app.Run();
	}

	private static void Save(FrameworkElement element, string path)
	{
		element.UpdateLayout();
		int num = (int)Math.Ceiling(element.ActualWidth);
		int num2 = (int)Math.Ceiling(element.ActualHeight);
		RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(num, num2, 96.0, 96.0, PixelFormats.Pbgra32);
		DrawingVisual drawingVisual = new DrawingVisual();
		using (DrawingContext drawingContext = drawingVisual.RenderOpen())
		{
			drawingContext.DrawRectangle(new SolidColorBrush(Palette.BgColor), null, new Rect(0.0, 0.0, num, num2));
			drawingContext.DrawRectangle(new VisualBrush(element), null, new Rect(0.0, 0.0, num, num2));
		}
		renderTargetBitmap.Render(drawingVisual);
		PngBitmapEncoder pngBitmapEncoder = new PngBitmapEncoder();
		pngBitmapEncoder.Frames.Add(BitmapFrame.Create(renderTargetBitmap));
		using FileStream stream = File.Create(path);
		pngBitmapEncoder.Save(stream);
	}
}
