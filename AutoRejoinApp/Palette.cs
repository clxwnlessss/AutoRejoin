using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace AutoRejoinApp;

public static class Palette
{
	public static readonly SolidColorBrush Text = Make("#F5F3FF");

	public static readonly SolidColorBrush Text2 = Make("#B6ABD9");

	public static readonly SolidColorBrush Text3 = Make("#706694");

	public static readonly SolidColorBrush Line = Make("#251D42");

	public static readonly SolidColorBrush Accent = Make("#A855F7");

	public static readonly SolidColorBrush AccentSoft = Make("#26143D");

	public static readonly SolidColorBrush Green = Make("#2EF2A6");

	public static readonly SolidColorBrush GreenSoft = Make("#0A2A20");

	public static readonly SolidColorBrush Blue = Make("#22D3EE");

	public static readonly SolidColorBrush BlueSoft = Make("#062A33");

	public static readonly SolidColorBrush Yellow = Make("#FFD23F");

	public static readonly SolidColorBrush YellowSoft = Make("#2A2108");

	public static readonly SolidColorBrush Red = Make("#FF5C7A");

	public static readonly SolidColorBrush RedSoft = Make("#2E0F1A");

	public static readonly SolidColorBrush BlueLine = Make("#0E4552");

	public static readonly SolidColorBrush YellowLine = Make("#5C4710");

	public static readonly SolidColorBrush Gray = Make("#9A8FC2");

	public static readonly SolidColorBrush GraySoft = Make("#15122A");

	public static readonly Color BgColor = Color.FromRgb(8, 8, 13);

	private static readonly Dictionary<string, SolidColorBrush> LogColors = new Dictionary<string, SolidColorBrush>(StringComparer.OrdinalIgnoreCase)
	{
		{
			"Green",
			Make("#4BFFB4")
		},
		{
			"Red",
			Make("#FF5C7A")
		},
		{
			"Yellow",
			Make("#FFE14D")
		},
		{
			"DarkYellow",
			Make("#F5B94F")
		},
		{
			"Cyan",
			Make("#67E8F9")
		},
		{
			"DarkGray",
			Make("#7A70A3")
		},
		{
			"Gray",
			Make("#CEC6EE")
		},
		{
			"White",
			Make("#F5F3FF")
		}
	};

	private static readonly SolidColorBrush[] AvatarColors = new SolidColorBrush[7]
	{
		Make("#C084FC"),
		Make("#2EF2A6"),
		Make("#22D3EE"),
		Make("#F472B6"),
		Make("#C084FC"),
		Make("#FB923C"),
		Make("#22D3EE")
	};

	public static SolidColorBrush ForLog(string color)
	{
		if (color == null || !LogColors.TryGetValue(color, out var value))
		{
			return LogColors["Gray"];
		}
		return value;
	}

	public static SolidColorBrush AvatarFor(string name)
	{
		int num = 0;
		string text = name ?? "";
		foreach (char c in text)
		{
			num = num * 31 + c;
		}
		return AvatarColors[Math.Abs(num % AvatarColors.Length)];
	}

	public static SolidColorBrush Make(string hex)
	{
		SolidColorBrush solidColorBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
		solidColorBrush.Freeze();
		return solidColorBrush;
	}
}
