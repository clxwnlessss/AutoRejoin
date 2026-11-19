using System.Windows;
using System.Windows.Media;

namespace AutoRejoinApp;

public class LogLine
{
	public string Time { get; set; }

	public string Account { get; set; }

	public string Key { get; set; }

	public string Message { get; set; }

	public string Color { get; set; }

	public Brush Brush => Palette.ForLog(Color);

	public Visibility AccountVisibility
	{
		get
		{
			if (!string.IsNullOrEmpty(Account))
			{
				return Visibility.Visible;
			}
			return Visibility.Collapsed;
		}
	}

	public bool Important
	{
		get
		{
			if (!(Color == "Red") && !(Color == "Yellow"))
			{
				return Color == "DarkYellow";
			}
			return true;
		}
	}

	public override string ToString()
	{
		return Time + "  " + (string.IsNullOrEmpty(Account) ? "" : ("[" + Account + "] ")) + Message;
	}
}
