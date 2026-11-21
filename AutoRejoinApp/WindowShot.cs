using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace AutoRejoinApp;

// Screenshot of one Roblox window by process id (PrintWindow with PW_RENDERFULLCONTENT, works for DirectX windows).
// A minimized helper is shown for a moment without stealing focus, captured and minimized again.
// Also builds the "storyboard" picture for a drop: the last frames before it, side by side, with times.
public static class WindowShot
{
	[DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
	[DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
	[DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
	[DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
	[DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
	[DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
	[DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
	[DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
	[DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

	private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

	[StructLayout(LayoutKind.Sequential)]
	private struct RECT { public int Left, Top, Right, Bottom; }

	private const int SW_SHOWNOACTIVATE = 4;
	private const int SW_SHOWMINNOACTIVE = 7;
	private const uint PW_RENDERFULLCONTENT = 2;

	public static IntPtr FindWindow(int pid)
	{
		try
		{
			IntPtr main = Process.GetProcessById(pid).MainWindowHandle;
			if (main != IntPtr.Zero && IsWindow(main))
			{
				return main;
			}
		}
		catch
		{
		}
		// Fallback: the biggest top-level window of that process.
		IntPtr best = IntPtr.Zero;
		long bestArea = 0;
		EnumWindows(delegate(IntPtr h, IntPtr l)
		{
			GetWindowThreadProcessId(h, out uint owner);
			if (owner == pid && IsWindowVisible(h) && GetWindowRect(h, out RECT r))
			{
				long area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
				if (area > bestArea)
				{
					bestArea = area;
					best = h;
				}
			}
			return true;
		}, IntPtr.Zero);
		return best;
	}

	public static bool IsMinimized(int pid)
	{
		IntPtr h = FindWindow(pid);
		return h == IntPtr.Zero || IsIconic(h);
	}

	// Captures the window; maxWidth scales it down. restoreMinimized: briefly show a minimized window to capture it.
	public static Bitmap Capture(int pid, int maxWidth, bool restoreMinimized, out string error)
	{
		error = null;
		IntPtr h = FindWindow(pid);
		if (h == IntPtr.Zero)
		{
			error = L.T("окно не найдено");
			return null;
		}
		bool wasMinimized = IsIconic(h);
		if (wasMinimized)
		{
			if (!restoreMinimized)
			{
				error = L.T("окно свёрнуто");
				return null;
			}
			ShowWindow(h, SW_SHOWNOACTIVATE);
			Thread.Sleep(450);
		}
		try
		{
			GetClientRect(h, out RECT rc);
			int w = rc.Right - rc.Left, hgt = rc.Bottom - rc.Top;
			if (w < 50 || hgt < 50)
			{
				GetWindowRect(h, out RECT wr);
				w = wr.Right - wr.Left;
				hgt = wr.Bottom - wr.Top;
			}
			if (w < 50 || hgt < 50)
			{
				error = L.T("окно слишком маленькое");
				return null;
			}
			using Bitmap full = new Bitmap(w, hgt, PixelFormat.Format32bppArgb);
			using (Graphics g = Graphics.FromImage(full))
			{
				IntPtr hdc = g.GetHdc();
				bool ok;
				try
				{
					ok = PrintWindow(h, hdc, 1 | PW_RENDERFULLCONTENT);
				}
				finally
				{
					g.ReleaseHdc(hdc);
				}
				if (!ok)
				{
					error = L.T("Windows не дала снять окно");
					return null;
				}
			}
			return Scale(full, maxWidth);
		}
		finally
		{
			if (wasMinimized)
			{
				ShowWindow(h, SW_SHOWMINNOACTIVE);
			}
		}
	}

	private static Bitmap Scale(Bitmap src, int maxWidth)
	{
		double k = Math.Min(1.0, maxWidth / (double)src.Width);
		int w = Math.Max(1, (int)(src.Width * k)), h = Math.Max(1, (int)(src.Height * k));
		Bitmap dst = new Bitmap(w, h, PixelFormat.Format24bppRgb);
		using Graphics g = Graphics.FromImage(dst);
		g.InterpolationMode = InterpolationMode.HighQualityBicubic;
		g.DrawImage(src, 0, 0, w, h);
		return dst;
	}

	public static byte[] Jpeg(Image img, long quality = 82)
	{
		ImageCodecInfo codec = ImageCodecInfo.GetImageEncoders().First((ImageCodecInfo c) => c.FormatID == ImageFormat.Jpeg.Guid);
		using EncoderParameters p = new EncoderParameters(1);
		p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
		using MemoryStream ms = new MemoryStream();
		img.Save(ms, codec, p);
		return ms.ToArray();
	}

	public static Bitmap FromJpeg(byte[] data)
	{
		using MemoryStream ms = new MemoryStream(data);
		using Image img = Image.FromStream(ms);
		return new Bitmap(img);
	}

	// Storyboard: up to 6 frames in a grid (3 per row), each with its time label; the last one is marked red.
	public static byte[] Storyboard(IList<KeyValuePair<string, Bitmap>> frames, string title)
	{
		if (frames.Count == 0)
		{
			return null;
		}
		int cols = Math.Min(3, frames.Count), rows = (frames.Count + cols - 1) / cols;
		int cw = 480, ch = (int)(cw * frames.Max((KeyValuePair<string, Bitmap> f) => f.Value.Height / (double)f.Value.Width));
		int pad = 10, head = 46, label = 28;
		using Bitmap sheet = new Bitmap(pad + cols * (cw + pad), head + rows * (ch + label + pad) + pad, PixelFormat.Format24bppRgb);
		using (Graphics g = Graphics.FromImage(sheet))
		{
			g.Clear(Color.FromArgb(10, 8, 20));
			g.InterpolationMode = InterpolationMode.HighQualityBicubic;
			g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
			using Font titleFont = new Font("Segoe UI Semibold", 15f);
			using Font labelFont = new Font("Segoe UI Semibold", 11f);
			using SolidBrush white = new SolidBrush(Color.FromArgb(245, 243, 255));
			using SolidBrush violet = new SolidBrush(Color.FromArgb(192, 132, 252));
			using SolidBrush red = new SolidBrush(Color.FromArgb(255, 92, 122));
			g.DrawString(title, titleFont, white, pad, 10);
			for (int i = 0; i < frames.Count; i++)
			{
				int x = pad + i % cols * (cw + pad), y = head + i / cols * (ch + label + pad);
				Bitmap f = frames[i].Value;
				int fh = (int)(cw * f.Height / (double)f.Width);
				g.DrawImage(f, x, y + label, cw, fh);
				bool last = i == frames.Count - 1;
				using Pen border = new Pen(last ? Color.FromArgb(255, 92, 122) : Color.FromArgb(59, 46, 107), last ? 3 : 1);
				g.DrawRectangle(border, x, y + label, cw - 1, fh - 1);
				g.DrawString(frames[i].Key, labelFont, last ? red : violet, x, y + 2);
			}
		}
		return Jpeg(sheet, 80);
	}
}
