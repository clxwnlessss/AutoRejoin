using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace AutoRejoinApp;

public static class Palette
{
	public static readonly SolidColorBrush Text = Make(ThemeInfo.Neon ? "#F5F3FF" : "#EDEDED");

	public static readonly SolidColorBrush Text2 = Make(ThemeInfo.Neon ? "#B6ABD9" : "#A1A1A1");

	public static readonly SolidColorBrush Text3 = Make(ThemeInfo.Neon ? "#706694" : "#6B6B6B");

	public static readonly SolidColorBrush Line = Make(ThemeInfo.Neon ? "#251D42" : "#1F1F1F");

	public static readonly SolidColorBrush Accent = Make(ThemeInfo.Neon ? "#A855F7" : "#E5E5E5");

	public static readonly SolidColorBrush AccentSoft = Make(ThemeInfo.Neon ? "#26143D" : "#1A1A1A");

	public static readonly SolidColorBrush Green = Make(ThemeInfo.Neon ? "#2EF2A6" : "#3DD68C");

	public static readonly SolidColorBrush GreenSoft = Make(ThemeInfo.Neon ? "#0A2A20" : "#0D2219");

	public static readonly SolidColorBrush Blue = Make(ThemeInfo.Neon ? "#22D3EE" : "#5CC8F5");

	public static readonly SolidColorBrush BlueSoft = Make(ThemeInfo.Neon ? "#062A33" : "#0C1E26");

	public static readonly SolidColorBrush Yellow = Make(ThemeInfo.Neon ? "#FFD23F" : "#F5C451");

	public static readonly SolidColorBrush YellowSoft = Make(ThemeInfo.Neon ? "#2A2108" : "#251E0C");

	public static readonly SolidColorBrush Red = Make(ThemeInfo.Neon ? "#FF5C7A" : "#F2555A");

	public static readonly SolidColorBrush RedSoft = Make(ThemeInfo.Neon ? "#2E0F1A" : "#2A1012");

	public static readonly SolidColorBrush BlueLine = Make(ThemeInfo.Neon ? "#0E4552" : "#173847");

	public static readonly SolidColorBrush YellowLine = Make(ThemeInfo.Neon ? "#5C4710" : "#4A3B16");

	public static readonly SolidColorBrush Gray = Make(ThemeInfo.Neon ? "#9A8FC2" : "#8A8A8A");

	public static readonly SolidColorBrush GraySoft = Make(ThemeInfo.Neon ? "#15122A" : "#151515");

	public static readonly Color BgColor = ThemeInfo.Neon ? Color.FromRgb(6, 5, 11) : Color.FromRgb(0, 0, 0);

	private static readonly Dictionary<string, SolidColorBrush> LogColors = new Dictionary<string, SolidColorBrush>(StringComparer.OrdinalIgnoreCase)
	{
		{
			"Green",
			Make(ThemeInfo.Neon ? "#4BFFB4" : "#4ADE80")
		},
		{
			"Red",
			Make(ThemeInfo.Neon ? "#FF5C7A" : "#F2555A")
		},
		{
			"Yellow",
			Make(ThemeInfo.Neon ? "#FFE14D" : "#FACC15")
		},
		{
			"DarkYellow",
			Make(ThemeInfo.Neon ? "#F5B94F" : "#E5A84B")
		},
		{
			"Cyan",
			Make(ThemeInfo.Neon ? "#67E8F9" : "#67D4F0")
		},
		{
			"DarkGray",
			Make(ThemeInfo.Neon ? "#7A70A3" : "#6B6B6B")
		},
		{
			"Gray",
			Make(ThemeInfo.Neon ? "#CEC6EE" : "#C8C8C8")
		},
		{
			"White",
			Make(ThemeInfo.Neon ? "#F5F3FF" : "#EDEDED")
		}
	};

	private static readonly SolidColorBrush[] AvatarColors = new SolidColorBrush[7]
	{
		Make(ThemeInfo.Neon ? "#C084FC" : "#C4B5FD"),
		Make(ThemeInfo.Neon ? "#2EF2A6" : "#3DD68C"),
		Make(ThemeInfo.Neon ? "#22D3EE" : "#5CC8F5"),
		Make(ThemeInfo.Neon ? "#F472B6" : "#F472B6"),
		Make(ThemeInfo.Neon ? "#C084FC" : "#C4B5FD"),
		Make(ThemeInfo.Neon ? "#FB923C" : "#FB923C"),
		Make(ThemeInfo.Neon ? "#22D3EE" : "#5CC8F5")
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
