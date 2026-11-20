using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AutoRejoinApp;

public static class ScheduleMath
{
	private static readonly CultureInfo Russian = new CultureInfo("ru-RU");

	public static TimeZoneInfo FindZone(string id)
	{
		if (!string.IsNullOrEmpty(id))
		{
			try
			{
				return TimeZoneInfo.FindSystemTimeZoneById(id);
			}
			catch
			{
			}
		}
		return TimeZoneInfo.Local;
	}

	public static bool TryParseTime(string text, out int hour, out int minute)
	{
		hour = (minute = 0);
		Match match = Regex.Match(text ?? "", "^\\s*([01]?\\d|2[0-3]):([0-5]\\d)\\s*$");
		if (!match.Success)
		{
			return false;
		}
		hour = int.Parse(match.Groups[1].Value);
		minute = int.Parse(match.Groups[2].Value);
		return true;
	}

	public static DateTime NextUtc(DateTime fromUtc, int hour, int minute, TimeZoneInfo zone)
	{
		DateTime dateTime = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
		DateTime date = TimeZoneInfo.ConvertTimeFromUtc(dateTime, zone).Date;
		for (int i = -1; i <= 2; i++)
		{
			DateTime dateTime2 = DateTime.SpecifyKind(date.AddDays(i).AddHours(hour).AddMinutes(minute), DateTimeKind.Unspecified);
			if (zone.IsInvalidTime(dateTime2))
			{
				dateTime2 = dateTime2.AddHours(1.0);
			}
			DateTime dateTime3 = TimeZoneInfo.ConvertTimeToUtc(dateTime2, zone);
			if (dateTime3 >= dateTime)
			{
				return dateTime3;
			}
		}
		return dateTime.AddDays(1.0);
	}

	public static string DescribeLocal(DateTime utc)
	{
		DateTime dateTime = utc.ToLocalTime();
		DateTime date = DateTime.Now.Date;
		string text = dateTime.ToString("HH:mm", CultureInfo.InvariantCulture);
		if (dateTime.Date == date)
		{
			return L.T("сегодня в ") + text;
		}
		if (dateTime.Date == date.AddDays(1.0))
		{
			return L.T("завтра в ") + text;
		}
		return dateTime.ToString("ddd, dd.MM", Russian) + L.T(" в ") + text;
	}

	public static string DescribeIn(TimeSpan span)
	{
		if (span.TotalSeconds < 60.0)
		{
			return L.T("меньше чем через минуту");
		}
		int num = (int)span.TotalHours;
		int minutes = span.Minutes;
		if (num == 0)
		{
			return L.T("через ") + minutes + L.T(" мин");
		}
		return L.T("через ") + num + L.T(" ч ") + minutes + L.T(" мин");
	}

	public static string ShortZoneName(TimeZoneInfo zone)
	{
		string text = zone.DisplayName;
		int num = text.IndexOf(')');
		if (text.StartsWith("(UTC") && num > 0 && num + 1 < text.Length)
		{
			text = text.Substring(num + 1).Trim();
		}
		return text;
	}
}
