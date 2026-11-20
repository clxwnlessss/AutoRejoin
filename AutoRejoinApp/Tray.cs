using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AutoRejoinApp;

public class Tray : IDisposable
{
	private class DarkMenuColors : ProfessionalColorTable
	{
		private static readonly Color Back = Color.FromArgb(26, 30, 41);

		private static readonly Color Hover = Color.FromArgb(42, 48, 64);

		private static readonly Color Border = Color.FromArgb(53, 60, 78);

		public override Color ToolStripDropDownBackground => Back;

		public override Color MenuBorder => Border;

		public override Color MenuItemBorder => Hover;

		public override Color MenuItemSelected => Hover;

		public override Color MenuItemSelectedGradientBegin => Hover;

		public override Color MenuItemSelectedGradientEnd => Hover;

		public override Color MenuItemPressedGradientBegin => Hover;

		public override Color MenuItemPressedGradientEnd => Hover;

		public override Color ImageMarginGradientBegin => Back;

		public override Color ImageMarginGradientMiddle => Back;

		public override Color ImageMarginGradientEnd => Back;

		public override Color SeparatorDark => Border;

		public override Color SeparatorLight => Back;
	}

	private readonly NotifyIcon icon;

	private readonly ToolStripMenuItem startStopItem;

	public event Action OpenRequested;

	public event Action StartStopRequested;

	public event Action RejoinAllRequested;

	public event Action ExitRequested;

	public Tray()
	{
		Icon icon;
		using (Stream stream = Xaml.AppIconStream())
		{
			icon = ((stream != null) ? new Icon(stream, SystemInformation.SmallIconSize) : SystemIcons.Application);
		}
		ContextMenuStrip contextMenuStrip = new ContextMenuStrip
		{
			Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors()),
			ShowImageMargin = false,
			Font = new Font("Segoe UI", 9.5f),
			BackColor = Color.FromArgb(26, 30, 41),
			ForeColor = Color.FromArgb(236, 238, 245)
		};
		ToolStripMenuItem toolStripMenuItem = new ToolStripMenuItem(L.T("Открыть AutoRejoin"), null, delegate
		{
			Raise(this.OpenRequested);
		})
		{
			Font = new Font(contextMenuStrip.Font, FontStyle.Bold)
		};
		startStopItem = new ToolStripMenuItem(L.T("Запустить"), null, delegate
		{
			Raise(this.StartStopRequested);
		});
		ToolStripMenuItem toolStripMenuItem2 = new ToolStripMenuItem(L.T("Перезайти всеми аккаунтами"), null, delegate
		{
			Raise(this.RejoinAllRequested);
		});
		ToolStripMenuItem toolStripMenuItem3 = new ToolStripMenuItem(L.T("Выход"), null, delegate
		{
			Raise(this.ExitRequested);
		});
		contextMenuStrip.Items.AddRange(new ToolStripItem[6]
		{
			toolStripMenuItem,
			new ToolStripSeparator(),
			startStopItem,
			toolStripMenuItem2,
			new ToolStripSeparator(),
			toolStripMenuItem3
		});
		foreach (ToolStripItem item in contextMenuStrip.Items)
		{
			item.ForeColor = contextMenuStrip.ForeColor;
			item.Padding = new Padding(4, 3, 4, 3);
		}
		this.icon = new NotifyIcon();
		this.icon.Icon = icon;
		this.icon.Text = "AutoRejoin";
		this.icon.ContextMenuStrip = contextMenuStrip;
		this.icon.MouseClick += delegate(object s, MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				Raise(this.OpenRequested);
			}
		};
		this.icon.BalloonTipClicked += delegate
		{
			Raise(this.OpenRequested);
		};
		this.icon.Visible = true;
	}

	private static void Raise(Action handler)
	{
		handler?.Invoke();
	}

	public void SetRunning(bool running)
	{
		startStopItem.Text = (running ? L.T("Остановить") : L.T("Запустить"));
	}

	public void SetText(string text)
	{
		icon.Text = ((text.Length > 63) ? (text.Substring(0, 62) + "…") : text);
	}

	public void Balloon(string title, string text, bool warning)
	{
		icon.ShowBalloonTip(8000, title, text, (!warning) ? ToolTipIcon.Info : ToolTipIcon.Warning);
	}

	public void Dispose()
	{
		icon.Visible = false;
		icon.Dispose();
	}
}
