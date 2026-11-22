using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace AutoRejoinApp;

public static class App
{
	public const string Version = "1.2";

	private static Mutex instanceMutex;

	// Restart after an automatic update: start the engine again (--resume) and stay in the tray (--hidden).
	public static bool Resume;

	public static bool StartHidden;

	[STAThread]
	public static int Main(string[] args)
	{
		string dataOverride = null;
		string text = null;
		string fakeStatus = null;
		string fakeLog = null;
		bool flag = false;
		for (int i = 0; i < args.Length; i++)
		{
			string text2 = args[i].ToLowerInvariant();
			string text3 = ((i + 1 < args.Length) ? args[i + 1] : null);
			if (text2 == "--autostart")
			{
				flag = true;
			}
			else if (text2 == "--resume")
			{
				Resume = true;
			}
			else if (text2 == "--hidden")
			{
				StartHidden = true;
			}
			else if (text2 == "--data" && text3 != null)
			{
				dataOverride = text3;
				i++;
			}
			else if (text2 == "--screenshot" && text3 != null)
			{
				text = text3;
				i++;
			}
			else if (text2 == "--fake-status" && text3 != null)
			{
				fakeStatus = text3;
				i++;
			}
			else if (text2 == "--fake-log" && text3 != null)
			{
				fakeLog = text3;
				i++;
			}
		}
		AppPaths.Init(dataOverride);
		try
		{
			AppConfig startConfig = Store.LoadConfig();
			L.Init(startConfig.language);
			ThemeInfo.Neon = startConfig.theme == "neon";
		}
		catch
		{
			L.Init("");
		}
		string text4 = InstanceId(AppPaths.DataDir);
		instanceMutex = new Mutex(initiallyOwned: true, "Local\\AutoRejoinApp_" + text4, out var createdNew);
		if (!createdNew && text == null)
		{
			if (!flag)
			{
				try
				{
					using EventWaitHandle eventWaitHandle = EventWaitHandle.OpenExisting("Local\\AutoRejoinApp_Show_" + text4);
					eventWaitHandle.Set();
				}
				catch
				{
				}
			}
			return 0;
		}
		EventWaitHandle showEvent = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, "Local\\AutoRejoinApp_Show_" + text4);
		Application app = new Application
		{
			ShutdownMode = ShutdownMode.OnExplicitShutdown
		};
		app.DispatcherUnhandledException += delegate(object s, DispatcherUnhandledExceptionEventArgs e)
		{
			LogError(L.T("ошибка в окне"), e.Exception);
			e.Handled = true;
		};
		AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
		{
			LogError(L.T("необработанная ошибка"), e.ExceptionObject as Exception);
		};
		TaskScheduler.UnobservedTaskException += delegate(object s, UnobservedTaskExceptionEventArgs e)
		{
			LogError(L.T("ошибка фоновой задачи"), e.Exception);
			e.SetObserved();
		};
		app.Resources.MergedDictionaries.Add(Xaml.Load<ResourceDictionary>(ThemeInfo.Neon ? "Theme.xaml" : "Theme.Mono.xaml"));
		Window window = Xaml.Load<Window>("MainWindow.xaml");
		MainController controller = new MainController(window, flag);
		Thread thread = new Thread((ThreadStart)delegate
		{
			while (showEvent.WaitOne())
			{
				app.Dispatcher.BeginInvoke(new Action(controller.ShowWindow));
			}
		});
		thread.IsBackground = true;
		thread.Start();
		if (text != null)
		{
			Screenshots.Run(app, window, controller, text, fakeStatus, fakeLog);
			return 0;
		}
		if (!flag && !StartHidden)
		{
			window.Show();
		}
		controller.Begin();
		app.Run();
		GC.KeepAlive(instanceMutex);
		return 0;
	}

	private static string InstanceId(string dataDir)
	{
		using SHA1 sHA = SHA1.Create();
		byte[] source = sHA.ComputeHash(Encoding.UTF8.GetBytes(dataDir.ToLowerInvariant()));
		return string.Concat(from b in source.Take(6)
			select b.ToString("x2"));
	}

	public static void LogError(string what, Exception error)
	{
		try
		{
			File.AppendAllText(AppPaths.ErrorLog, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + what + ": " + ((error == null) ? "?" : error.ToString()) + Environment.NewLine, Encoding.UTF8);
		}
		catch
		{
		}
	}
}
