using System;

namespace AutoRejoinApp;

public class ScheduleConfig
{
	public bool enabled { get; set; }

	public string time { get; set; }

	public string timeZone { get; set; }

	public string action { get; set; }

	public ScheduleConfig()
	{
		enabled = false;
		time = "09:51";
		timeZone = TimeZoneInfo.Local.Id;
		action = "rejoin";
	}
}
