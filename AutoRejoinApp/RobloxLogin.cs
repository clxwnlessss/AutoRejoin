using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace AutoRejoinApp;

public static class RobloxLogin
{
	private const string NoRuntimeText = "Для входа по логину и паролю нужен Microsoft Edge WebView2 Runtime — скачай его с сайта Microsoft («WebView2 Runtime», Evergreen) и попробуй снова.";

	private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);

	private static bool loaderSet;

	public static bool Busy => Gate.CurrentCount == 0;

	public static event Action<string, string> NeedsUser;

	public static string RuntimeProblem()
	{
		try
		{
			SetLoaderPath();
			string availableBrowserVersionString = CoreWebView2Environment.GetAvailableBrowserVersionString();
			return string.IsNullOrEmpty(availableBrowserVersionString) ? "Для входа по логину и паролю нужен Microsoft Edge WebView2 Runtime — скачай его с сайта Microsoft («WebView2 Runtime», Evergreen) и попробуй снова." : null;
		}
		catch (WebView2RuntimeNotFoundException)
		{
			return "Для входа по логину и паролю нужен Microsoft Edge WebView2 Runtime — скачай его с сайта Microsoft («WebView2 Runtime», Evergreen) и попробуй снова.";
		}
		catch (Exception ex2)
		{
			return "Встроенный браузер не запускается: " + ex2.Message;
		}
	}

	private static void SetLoaderPath()
	{
		if (!loaderSet)
		{
			string text = Path.Combine(AppPaths.AppDir, "webview2", Environment.Is64BitProcess ? "x64" : "x86");
			if (File.Exists(Path.Combine(text, "WebView2Loader.dll")))
			{
				CoreWebView2Environment.SetLoaderDllFolderPath(text);
			}
			loaderSet = true;
		}
	}

	public static async Task<LoginResult> Run(LoginRequest request, Window owner)
	{
		string problem = RuntimeProblem();
		if (problem != null)
		{
			return new LoginResult
			{
				Error = problem
			};
		}
		await Gate.WaitAsync();
		try
		{
			LoginSession session = new LoginSession(request, owner);
			return await session.RunAsync();
		}
		finally
		{
			Gate.Release();
		}
	}

	internal static void RaiseNeedsUser(string name, string what)
	{
		RobloxLogin.NeedsUser?.Invoke(name, what);
	}
}
