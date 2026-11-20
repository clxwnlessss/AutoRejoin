using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media.Imaging;

namespace AutoRejoinApp;

public static class Xaml
{
	public static T Load<T>(string name) where T : class
	{
		using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AutoRejoinApp." + name);
		if (stream == null)
		{
			throw new InvalidOperationException(L.T("Нет ресурса ") + name);
		}
		T loaded = (T)XamlReader.Load(stream);
		L.Apply(loaded);
		return loaded;
	}

	public static T Find<T>(FrameworkElement root, string name) where T : class
	{
		if (!(root.FindName(name) is T result))
		{
			throw new InvalidOperationException(L.T("В разметке нет элемента ") + name);
		}
		return result;
	}

	public static BitmapFrame AppIcon()
	{
		using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AutoRejoinApp.app.ico");
		if (stream == null)
		{
			return null;
		}
		MemoryStream memoryStream = new MemoryStream();
		stream.CopyTo(memoryStream);
		memoryStream.Position = 0L;
		return BitmapFrame.Create(memoryStream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
	}

	public static Stream AppIconStream()
	{
		return Assembly.GetExecutingAssembly().GetManifestResourceStream("AutoRejoinApp.app.ico");
	}
}
