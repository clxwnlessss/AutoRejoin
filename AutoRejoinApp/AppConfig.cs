namespace AutoRejoinApp;

public class AppConfig
{
	public int version { get; set; }

	public string link { get; set; }

	public string linkFile { get; set; }

	public bool autoStartEngine { get; set; }

	public int startupDelay { get; set; }

	public bool minimizeWindows { get; set; }

	public int windowWidth { get; set; }

	public int windowHeight { get; set; }

	public bool mainFullscreen { get; set; }

	public int mainFps { get; set; }

	public int helperFps { get; set; }

	public string mainPriority { get; set; }

	public bool memoryLimitOn { get; set; }

	public double memoryLimitGB { get; set; }

	public bool memoryLimitSkipMain { get; set; }

	public bool windowMemoryOn { get; set; }

	public double windowMemoryGB { get; set; }

	public bool minFreeMemoryOn { get; set; }

	public double minFreeMemoryGB { get; set; }

	public bool useBootstrapper { get; set; }

	public bool autoReboot { get; set; }

	public bool potassium { get; set; }

	public string potassiumPath { get; set; }

	public bool keepAutoAttach { get; set; }

	public bool potassiumAutoLogin { get; set; }

	public bool scriptCheck { get; set; }

	public int scriptStartTimeout { get; set; }

	public bool pulseCheck { get; set; }

	public int scriptTimeout { get; set; }

	public bool balloonRejoin { get; set; }

	public int balloonRejoinCount { get; set; }

	public int balloonQuietMinutes { get; set; }

	public bool helperMemCapOn { get; set; }

	public int helperMemCapMB { get; set; }

	public bool helperQualityOn { get; set; }

	public int helperQuality { get; set; }

	public bool autoAcceptWarnings { get; set; }

	public ScheduleConfig schedule { get; set; }

	public AdvancedConfig advanced { get; set; }

	public bool trayHintShown { get; set; }

	public bool discordOn { get; set; }

	public int discordInterval { get; set; }

	public bool discordEvents { get; set; }

	public string discordMentions { get; set; }

	public AppConfig()
	{
		version = 1;
		link = "";
		linkFile = "";
		autoStartEngine = true;
		startupDelay = 0;
		minimizeWindows = true;
		windowWidth = 800;
		windowHeight = 600;
		mainFullscreen = false;
		mainFps = 60;
		helperFps = 30;
		mainPriority = "";
		memoryLimitOn = true;
		memoryLimitGB = 8.0;
		windowMemoryOn = true;
		windowMemoryGB = 3.5;
		minFreeMemoryOn = true;
		minFreeMemoryGB = 1.5;
		useBootstrapper = false;
		autoReboot = false;
		potassium = true;
		potassiumPath = "";
		keepAutoAttach = true;
		potassiumAutoLogin = false;
		scriptCheck = false;
		scriptStartTimeout = 60;
		pulseCheck = false;
		scriptTimeout = 90;
		balloonRejoin = false;
		balloonRejoinCount = 6;
		balloonQuietMinutes = 120;
		helperMemCapOn = false;
		helperMemCapMB = 700;
		helperQualityOn = false;
		helperQuality = 1;
		autoAcceptWarnings = false;
		schedule = new ScheduleConfig();
		advanced = new AdvancedConfig();
		discordOn = false;
		discordInterval = 15;
		discordEvents = true;
		discordMentions = "";
	}

	public void Normalize()
	{
		if (discordMentions == null)
		{
			discordMentions = "";
		}
		if (discordInterval < 1)
		{
			discordInterval = 15;
		}
		if (link == null)
		{
			link = "";
		}
		if (linkFile == null)
		{
			linkFile = "";
		}
		if (mainPriority == null)
		{
			mainPriority = "";
		}
		if (potassiumPath == null)
		{
			potassiumPath = "";
		}
		if (schedule == null)
		{
			schedule = new ScheduleConfig();
		}
		if (advanced == null)
		{
			advanced = new AdvancedConfig();
		}
		if (string.IsNullOrEmpty(schedule.time))
		{
			schedule.time = "09:51";
		}
		if (schedule.timeZone == null)
		{
			schedule.timeZone = "";
		}
		if (schedule.action != "reboot")
		{
			schedule.action = "rejoin";
		}
	}
}
