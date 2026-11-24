using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AutoRejoinApp;

// Vector icons (3.0 neon redesign): line icons on a 24×24 grid, drawn with round caps in the current text color.
// Replaces the old Segoe MDL2 font glyphs. Glyph accepts an icon name ("rejoin") or an old glyph code (""),
// so markup and code that still pass the old codes keep working.
public class Ico : FrameworkElement
{
	public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(nameof(Glyph), typeof(object), typeof(Ico),
		new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, e) => ((Ico)d).UpdateSpin()));

	public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(Ico),
		new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(nameof(Size), typeof(double), typeof(Ico),
		new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(nameof(Thickness), typeof(double), typeof(Ico),
		new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender));

	public object Glyph { get => GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
	public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
	public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
	public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }

	// Old Segoe MDL2 codes → icon names.
	private static readonly Dictionary<string, string> Legacy = new Dictionary<string, string>
	{
		[""] = "rejoin", [""] = "sync", [""] = "warn", [""] = "info", [""] = "close", [""] = "check",
		[""] = "memory", [""] = "play", [""] = "stop", [""] = "users", [""] = "user", [""] = "down",
		[""] = "up", [""] = "code", [""] = "max", [""] = "restore", [""] = "min", [""] = "chat",
		[""] = "clock", [""] = "power", [""] = "calendar", [""] = "trash", [""] = "lock", [""] = "edit",
		[""] = "cookie", [""] = "copy", [""] = "folder", [""] = "open", [""] = "log", [""] = "gamepad",
		[""] = "monitor", [""] = "globe", [""] = "trophy", [""] = "gear", [""] = "plus", [""] = "bot",
		[""] = "bot", [""] = "lang", [" "] = "globe"
	};

	private const string Circle = "M2 12 A10 10 0 1 0 22 12 A10 10 0 1 0 2 12 Z ";

	private static readonly Dictionary<string, string> Paths = new Dictionary<string, string>
	{
		["rejoin"] = "M21 12 A9 9 0 1 1 12 3 C14.5 3 16.9 4 18.7 5.7 L21 8 M21 3 V8 H16",
		["sync"] = "M21 12 A9 9 0 1 1 12 3",
		["warn"] = "M10.3 3.9 L1.8 18 A2 2 0 0 0 3.5 21 H20.5 A2 2 0 0 0 22.2 18 L13.7 3.9 A2 2 0 0 0 10.3 3.9 Z M12 9 V13 M12 17 H12.01",
		["info"] = Circle + "M12 16 V12 M12 8 H12.01",
		["close"] = "M18 6 L6 18 M6 6 L18 18",
		["check"] = "M20 6 L9 17 L4 12",
		["search"] = "M3 11 A8 8 0 1 0 19 11 A8 8 0 1 0 3 11 Z M21 21 L16.65 16.65",
		["memory"] = "M7 5 H17 A2 2 0 0 1 19 7 V17 A2 2 0 0 1 17 19 H7 A2 2 0 0 1 5 17 V7 A2 2 0 0 1 7 5 Z M9.5 9.5 H14.5 V14.5 H9.5 Z M9 2 V5 M15 2 V5 M9 19 V22 M15 19 V22 M2 9 H5 M2 15 H5 M19 9 H22 M19 15 H22",
		["play"] = "M7 4.5 L19 12 L7 19.5 Z",
		["stop"] = "M7 6 H17 A1 1 0 0 1 18 7 V17 A1 1 0 0 1 17 18 H7 A1 1 0 0 1 6 17 V7 A1 1 0 0 1 7 6 Z",
		["users"] = "M16 21 V19 A4 4 0 0 0 12 15 H6 A4 4 0 0 0 2 19 V21 M5 7 A4 4 0 1 0 13 7 A4 4 0 1 0 5 7 Z M22 21 V19 A4 4 0 0 0 19 15.1 M16 3.1 A4 4 0 0 1 16 10.9",
		["user"] = "M19 21 V19 A4 4 0 0 0 15 15 H9 A4 4 0 0 0 5 19 V21 M8 7 A4 4 0 1 0 16 7 A4 4 0 1 0 8 7 Z",
		["down"] = "M6 9 L12 15 L18 9",
		["up"] = "M18 15 L12 9 L6 15",
		["code"] = "M8 3 H7 A2 2 0 0 0 5 5 V10 A2 2 0 0 1 3 12 A2 2 0 0 1 5 14 V19 A2 2 0 0 0 7 21 H8 M16 21 H17 A2 2 0 0 0 19 19 V14 A2 2 0 0 1 21 12 A2 2 0 0 1 19 10 V5 A2 2 0 0 0 17 3 H16",
		["max"] = "M5.5 5.5 H18.5 V18.5 H5.5 Z",
		["restore"] = "M8.5 8.5 H18.5 V18.5 H8.5 Z M5.5 15.5 V5.5 H15.5",
		["min"] = "M5 12 H19",
		["chat"] = "M21 11.5 A8.4 8.4 0 0 1 20.1 15.3 A8.5 8.5 0 0 1 12.5 20 A8.4 8.4 0 0 1 8.7 19.1 L3 21 L4.9 15.3 A8.4 8.4 0 0 1 4 11.5 A8.5 8.5 0 0 1 8.7 3.9 A8.4 8.4 0 0 1 12.5 3 H13 A8.5 8.5 0 0 1 21 11 Z",
		["clock"] = Circle + "M12 6 V12 L16 14",
		["power"] = "M12 2 V12 M18.4 6.6 A9 9 0 1 1 5.6 6.6",
		["calendar"] = "M5 4 H19 A2 2 0 0 1 21 6 V20 A2 2 0 0 1 19 22 H5 A2 2 0 0 1 3 20 V6 A2 2 0 0 1 5 4 Z M16 2 V6 M8 2 V6 M3 10 H21",
		["trash"] = "M3 6 H21 M19 6 V20 A2 2 0 0 1 17 22 H7 A2 2 0 0 1 5 20 V6 M8 6 V4 A2 2 0 0 1 10 2 H14 A2 2 0 0 1 16 4 V6",
		["mail"] = "M4 5 H20 A2 2 0 0 1 22 7 V17 A2 2 0 0 1 20 19 H4 A2 2 0 0 1 2 17 V7 A2 2 0 0 1 4 5 Z M2 7 L12 13 L22 7",
		["lock"] = "M5 11 H19 A2 2 0 0 1 21 13 V20 A2 2 0 0 1 19 22 H5 A2 2 0 0 1 3 20 V13 A2 2 0 0 1 5 11 Z M7 11 V7 A5 5 0 0 1 17 7 V11",
		["edit"] = "M17 3 A2.8 2.8 0 1 1 21 7 L7.5 20.5 L2 22 L3.5 16.5 Z",
		["cookie"] = "M12 2 A10 10 0 1 0 22 12 A4 4 0 0 1 18 8 A4 4 0 0 1 14 4 A2 2 0 0 1 12 2 Z M8.5 8.5 H8.51 M16 15.5 H16.01 M12 12 H12.01 M11 17 H11.01 M7 14 H7.01",
		["copy"] = "M10 8 H20 A2 2 0 0 1 22 10 V20 A2 2 0 0 1 20 22 H10 A2 2 0 0 1 8 20 V10 A2 2 0 0 1 10 8 Z M4 16 A2 2 0 0 1 2 14 V4 A2 2 0 0 1 4 2 H14 A2 2 0 0 1 16 4",
		["folder"] = "M4 20 H20 A2 2 0 0 0 22 18 V8 A2 2 0 0 0 20 6 H12.1 A2 2 0 0 1 10.4 5.1 L9.6 3.9 A2 2 0 0 0 7.9 3 H4 A2 2 0 0 0 2 5 V18 A2 2 0 0 0 4 20 Z",
		["open"] = "M15 3 H21 V9 M10 14 L21 3 M18 13 V19 A2 2 0 0 1 16 21 H5 A2 2 0 0 1 3 19 V8 A2 2 0 0 1 5 6 H11",
		["log"] = "M14 2 H6 A2 2 0 0 0 4 4 V20 A2 2 0 0 0 6 22 H18 A2 2 0 0 0 20 20 V8 Z M14 2 V8 H20 M16 13 H8 M16 17 H8 M10 9 H8",
		["gamepad"] = "M6 12 H10 M8 10 V14 M15 13 H15.01 M18 11 H18.01 M17.3 5 H6.7 A4 4 0 0 0 2.7 8.6 L2 16.5 A2.5 2.5 0 0 0 4.5 19 C5.5 19 6 18.5 6.5 18 L7.9 16.6 A2 2 0 0 1 9.3 16 H14.7 A2 2 0 0 1 16.1 16.6 L17.5 18 C18 18.5 18.5 19 19.5 19 A2.5 2.5 0 0 0 22 16.5 L21.3 8.6 A4 4 0 0 0 17.3 5 Z",
		["monitor"] = "M4 3 H20 A2 2 0 0 1 22 5 V15 A2 2 0 0 1 20 17 H4 A2 2 0 0 1 2 15 V5 A2 2 0 0 1 4 3 Z M8 21 H16 M12 17 V21",
		["globe"] = Circle + "M2 12 H22 M12 2 A15.3 15.3 0 0 1 16 12 A15.3 15.3 0 0 1 12 22 A15.3 15.3 0 0 1 8 12 A15.3 15.3 0 0 1 12 2 Z",
		["lang"] = "M5 8 L11 14 M4 14 L10 8 L12 5 M2 5 H14 M7 2 H8 M22 22 L17 12 L12 22 M14 18 H20",
		["trophy"] = "M6 9 H4.5 A2.5 2.5 0 0 1 4.5 4 H6 M18 9 H19.5 A2.5 2.5 0 0 0 19.5 4 H18 M4 22 H20 M10 14.7 V17 C10 17.6 9.5 18 9 18.2 C7.8 18.8 7 20.2 7 22 M14 14.7 V17 C14 17.6 14.5 18 15 18.2 C16.2 18.8 17 20.2 17 22 M18 2 H6 V9 A6 6 0 0 0 18 9 Z",
		["gear"] = "M9 12 A3 3 0 1 0 15 12 A3 3 0 1 0 9 12 Z M19.4 15 A1.65 1.65 0 0 0 19.7 16.8 L19.8 16.9 A2 2 0 1 1 17 19.7 L16.9 19.6 A1.65 1.65 0 0 0 15.1 19.3 A1.65 1.65 0 0 0 14.1 20.8 V21 A2 2 0 1 1 10.1 21 V20.9 A1.65 1.65 0 0 0 9 19.4 A1.65 1.65 0 0 0 7.2 19.7 L7.1 19.8 A2 2 0 1 1 4.3 17 L4.4 16.9 A1.65 1.65 0 0 0 4.7 15.1 A1.65 1.65 0 0 0 3.2 14.1 H3 A2 2 0 1 1 3 10.1 H3.1 A1.65 1.65 0 0 0 4.6 9 A1.65 1.65 0 0 0 4.3 7.2 L4.2 7.1 A2 2 0 1 1 7 4.3 L7.1 4.4 A1.65 1.65 0 0 0 8.9 4.7 H9 A1.65 1.65 0 0 0 10 3.2 V3 A2 2 0 1 1 14 3 V3.1 A1.65 1.65 0 0 0 15 4.6 A1.65 1.65 0 0 0 16.8 4.3 L16.9 4.2 A2 2 0 1 1 19.7 7 L19.6 7.1 A1.65 1.65 0 0 0 19.3 8.9 V9 A1.65 1.65 0 0 0 20.8 10 H21 A2 2 0 1 1 21 14 H20.9 A1.65 1.65 0 0 0 19.4 15 Z",
		["plus"] = "M12 5 V19 M5 12 H19",
		["bot"] = "M12 8 V4 H8 M6 8 H18 A2 2 0 0 1 20 10 V18 A2 2 0 0 1 18 20 H6 A2 2 0 0 1 4 18 V10 A2 2 0 0 1 6 8 Z M2 14 H4 M20 14 H22 M15 13 V15 M9 13 V15",
		["bell"] = "M6 8 A6 6 0 0 1 18 8 C18 15 21 17 21 17 H3 C3 17 6 15 6 8 M10.3 21 A1.94 1.94 0 0 0 13.7 21",
		["bolt"] = "M13 2 L3 14 H12 L11 22 L21 10 H12 Z",
		["shield"] = "M12 22 C12 22 20 18 20 12 V5 L12 2 L4 5 V12 C4 18 12 22 12 22 Z",
		["star"] = "M12 2 L15.1 8.3 L22 9.3 L17 14.1 L18.2 21 L12 17.8 L5.8 21 L7 14.1 L2 9.3 L8.9 8.3 Z",
		["hex"] = "M12 2 L21 7 V17 L12 22 L3 17 V7 Z",
		["dot"] = "M11 12 A1 1 0 1 0 13 12 A1 1 0 1 0 11 12 Z"
	};

	// Icons drawn filled instead of stroked.
	private static readonly HashSet<string> Filled = new HashSet<string> { "play", "stop", "dot" };

	private static readonly Dictionary<string, Geometry> Cache = new Dictionary<string, Geometry>();

	private RotateTransform spin;

	public static string Resolve(object glyph)
	{
		string key = glyph as string ?? glyph?.ToString() ?? "";
		if (Legacy.TryGetValue(key, out var name))
		{
			return name;
		}
		key = key.Trim().ToLowerInvariant();
		return Paths.ContainsKey(key) ? key : "dot";
	}

	private static Geometry GeometryFor(string name)
	{
		if (!Cache.TryGetValue(name, out var geometry))
		{
			try
			{
				geometry = Geometry.Parse(Paths[name]);
			}
			catch
			{
				geometry = Geometry.Parse(Paths["dot"]);
			}
			geometry.Freeze();
			Cache[name] = geometry;
		}
		return geometry;
	}

	public Ico()
	{
		SnapsToDevicePixels = false;
		VerticalAlignment = VerticalAlignment.Center;
		HorizontalAlignment = HorizontalAlignment.Center;
		IsHitTestVisible = false;
	}

	public static Ico Make(object glyph, double size, Brush brush = null)
	{
		Ico ico = new Ico { Glyph = glyph, Size = size };
		if (brush != null)
		{
			ico.Foreground = brush;
		}
		return ico;
	}

	// "sync" spins on its own (status: something is in progress).
	private void UpdateSpin()
	{
		bool spinning = Resolve(Glyph) == "sync";
		if (spinning && spin == null)
		{
			spin = new RotateTransform();
			RenderTransformOrigin = new Point(0.5, 0.5);
			RenderTransform = spin;
			spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever });
		}
		else if (!spinning && spin != null)
		{
			spin.BeginAnimation(RotateTransform.AngleProperty, null);
			spin = null;
			RenderTransform = Transform.Identity;
		}
	}

	protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize)
	{
		return new System.Windows.Size(Size, Size);
	}

	protected override void OnRender(DrawingContext dc)
	{
		string name = Resolve(Glyph);
		Geometry geometry = GeometryFor(name);
		double s = Size / 24.0;
		dc.PushTransform(new TranslateTransform((ActualWidth - Size) / 2, (ActualHeight - Size) / 2));
		dc.PushTransform(new ScaleTransform(s, s));
		if (Filled.Contains(name))
		{
			dc.DrawGeometry(Foreground, new Pen(Foreground, 1.5) { LineJoin = PenLineJoin.Round }, geometry);
		}
		else
		{
			// Keep strokes ~1.6px on screen even for tiny icons.
			double thickness = Math.Max(Thickness, 1.55 / s);
			dc.DrawGeometry(null, new Pen(Foreground, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, geometry);
		}
		dc.Pop();
		dc.Pop();
	}
}
