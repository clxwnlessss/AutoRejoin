using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace AutoRejoinApp;

public static class Palette
{
	public static readonly SolidColorBrush Text = Make("#ECEEF5");

	public static readonly SolidColorBrush Text2 = Make("#A2A9BC");

	public static readonly SolidColorBrush Text3 = Make("#646B82");

	public static readonly SolidColorBrush Line = Make("#1B2030");

	public static readonly SolidColorBrush Accent = Make("#3B82F6");

	public static readonly SolidColorBrush AccentSoft = Make("#0F1A2E");

	public static readonly SolidColorBrush Green = Make("#34D399");

	public static readonly SolidColorBrush GreenSoft = Make("#0E1F1A");

	public static readonly SolidColorBrush Blue = Make("#60A5FA");

	public static readonly SolidColorBrush BlueSoft = Make("#0F1B30");

	public static readonly SolidColorBrush Yellow = Make("#FBBF24");

	public static readonly SolidColorBrush YellowSoft = Make("#211B0E");

	public static readonly SolidColorBrush Red = Make("#F87171");

	public static readonly SolidColorBrush RedSoft = Make("#231216");

	public static readonly SolidColorBrush Gray = Make("#8B93A7");

	public static readonly SolidColorBrush GraySoft = Make("#161A23");

	public static readonly Color BgColor = Color.FromRgb(7, 8, 12);

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
			Make("#38BDF8")
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
			Make("#ECEEF5")
		}
	};

	private static readonly SolidColorBrush[] AvatarColors = new SolidColorBrush[7]
	{
		Make("#3B82F6"),
		Make("#34D399"),
		Make("#60A5FA"),
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
