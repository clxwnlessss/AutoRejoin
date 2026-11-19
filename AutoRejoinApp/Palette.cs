using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace AutoRejoinApp;

public static class Palette
{
	public static readonly SolidColorBrush Text = Make("#F2F1F8");

	public static readonly SolidColorBrush Text2 = Make("#A3A2B8");

	public static readonly SolidColorBrush Text3 = Make("#67667D");

	public static readonly SolidColorBrush Line = Make("#1D1D2A");

	public static readonly SolidColorBrush Accent = Make("#8B5CF6");

	public static readonly SolidColorBrush AccentSoft = Make("#1B1533");

	public static readonly SolidColorBrush Green = Make("#34D399");

	public static readonly SolidColorBrush GreenSoft = Make("#0D1F1A");

	public static readonly SolidColorBrush Blue = Make("#22D3EE");

	public static readonly SolidColorBrush BlueSoft = Make("#0B1B22");

	public static readonly SolidColorBrush Yellow = Make("#FBBF24");

	public static readonly SolidColorBrush YellowSoft = Make("#211B0E");

	public static readonly SolidColorBrush Red = Make("#F87171");

	public static readonly SolidColorBrush RedSoft = Make("#231216");

	public static readonly SolidColorBrush BlueLine = Make("#1A3B47");

	public static readonly SolidColorBrush YellowLine = Make("#4A3812");

	public static readonly SolidColorBrush Gray = Make("#8E8DA4");

	public static readonly SolidColorBrush GraySoft = Make("#17171F");

	public static readonly Color BgColor = Color.FromRgb(8, 8, 13);

	private static readonly Dictionary<string, SolidColorBrush> LogColors = new Dictionary<string, SolidColorBrush>(StringComparer.OrdinalIgnoreCase)
	{
		{
			"Green",
			Make("#4ADE80")
		},
		{
			"Red",
			Make("#F87171")
		},
		{
			"Yellow",
			Make("#FACC15")
		},
		{
			"DarkYellow",
			Make("#E5A84B")
		},
		{
			"Cyan",
			Make("#67E8F9")
		},
		{
			"DarkGray",
			Make("#6B7387")
		},
		{
			"Gray",
			Make("#C5CAD6")
		},
		{
			"White",
			Make("#F2F1F8")
		}
	};

	private static readonly SolidColorBrush[] AvatarColors = new SolidColorBrush[7]
	{
		Make("#A78BFA"),
		Make("#34D399"),
		Make("#22D3EE"),
		Make("#F472B6"),
		Make("#A78BFA"),
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
