namespace AutoRejoinApp;

public class AppConfig
{
	public int version { get; set; }

	// Normalize changed the config (version upgrade) — LoadConfig saves it, so the engine reads the new values too.
	[System.Web.Script.Serialization.ScriptIgnore]
	public bool Migrated { get; private set; }

	public string link { get; set; }

	public string linkFile { get; set; }

	// Игра по умолчанию (публичные сервера), если ссылка на сервер не задана: ID плейса, название, universe (для иконки).
	public string gamePlaceId { get; set; }

	public string gameName { get; set; }

	public long gameUniverseId { get; set; }

	// VIP server per game and script per game (see GameSetup.cs).
	public System.Collections.Generic.List<VipLink> vipLinks { get; set; }

	public System.Collections.Generic.List<GameScript> gameScripts { get; set; }

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

	// Экзекьютор: "potassium", "real" (Real Executor), "volt" или "synapsez". Скрипт фермы пишет файлы в workspace выбранного.
	public string executor { get; set; }

	// Папка или exe Volt / Synapse Z, если AutoRejoin не нашёл его сам.
	public string executorPath { get; set; }

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

	// Макросы OverFlux (Star Saw, Jelly Beans, Auto Clicker, Saw + Beans): клавиши, интервалы, счётчики.
	public MacrosConfig macros { get; set; }

	public bool trayHintShown { get; set; }

	// Ставить новые версии с GitHub самому, как только они вышли (проверка раз в 5 минут).
	public bool autoUpdate { get; set; }

	public string language { get; set; }

	// Оформление: "potassium" (по умолчанию, как у Потассиума) или "neon". Меняется после перезапуска.
	public string theme { get; set; }

	// Свои обои: файл в папке данных (wallpaper-*.jpg/png), затемнение и размытие (сразу), прозрачность карточек (после перезапуска).
	public string wallpaper { get; set; }

	public int wallpaperDim { get; set; }

	public int wallpaperBlur { get; set; }

	public int wallpaperCards { get; set; }

	public bool leaderboardGuest { get; set; }

	public string leaderboardUrl { get; set; }

	public bool logCleanupOn { get; set; }

	public int logCleanupHours { get; set; }

	public bool discordOn { get; set; }

	public int discordInterval { get; set; }

	public bool discordEvents { get; set; }

	public bool discordErrors { get; set; }

	public bool dropStoryboard { get; set; }

	public bool honeyStats { get; set; }

	public bool dailyReport { get; set; }

	public string discordMentions { get; set; }

	public AppConfig()
	{
		version = 2;
		link = "";
		linkFile = "";
		gamePlaceId = "1537690962";
		gameName = "Bee Swarm Simulator";
		gameUniverseId = 601130232L;
		vipLinks = new System.Collections.Generic.List<VipLink>();
		gameScripts = new System.Collections.Generic.List<GameScript>();
		autoStartEngine = true;
		startupDelay = 0;
		minimizeWindows = false;
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
		executor = "potassium";
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
		macros = new MacrosConfig();
		language = "";
		theme = "potassium";
		autoUpdate = true;
		wallpaper = "";
		wallpaperDim = 55;
		wallpaperBlur = 0;
		wallpaperCards = 80;
		leaderboardGuest = false;
		leaderboardUrl = "";
		logCleanupOn = true;
		logCleanupHours = 24;
		discordOn = false;
		discordInterval = 15;
		discordEvents = true;
		discordErrors = true;
		dropStoryboard = true;
		honeyStats = true;
		dailyReport = true;
		discordMentions = "";
	}

	public void Normalize()
	{
		// v2 (3.8.3): helper windows are no longer minimized by default — people want to see them, and scripts of
		// games like Anime Adventures stall in minimized windows. Turned off once for everyone; can be turned back on.
		if (version < 2)
		{
			minimizeWindows = false;
			version = 2;
			Migrated = true;
		}
		if (vipLinks == null)
		{
			vipLinks = new System.Collections.Generic.List<VipLink>();
		}
		vipLinks.RemoveAll((VipLink v) => v == null || string.IsNullOrEmpty(v.placeId));
		if (gameScripts == null)
		{
			gameScripts = new System.Collections.Generic.List<GameScript>();
		}
		gameScripts.RemoveAll((GameScript g) => g == null || string.IsNullOrEmpty(g.placeId));
		if (discordMentions == null)
		{
			discordMentions = "";
		}
		if (executor != "real" && executor != "volt" && executor != "synapsez")
		{
			executor = "potassium";
		}
		if (executorPath == null)
		{
			executorPath = "";
		}
		if (theme != "neon" && theme != "potassium")
		{
			theme = "potassium";
		}
		if (language == null)
		{
			language = "";
		}
		if (leaderboardUrl == null)
		{
			leaderboardUrl = "";
		}
		if (logCleanupHours < 1)
		{
			logCleanupHours = 24;
		}
		if (discordInterval < 1)
		{
			discordInterval = 15;
		}
		if (link == null)
		{
			link = "";
		}
		if (string.IsNullOrEmpty(gamePlaceId) || !System.Text.RegularExpressions.Regex.IsMatch(gamePlaceId, "^\\d{4,20}$"))
		{
			gamePlaceId = "1537690962";
			gameName = "Bee Swarm Simulator";
			gameUniverseId = 601130232L;
		}
		if (string.IsNullOrEmpty(gameName))
		{
			gameName = gamePlaceId;
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
		if (macros == null)
		{
			macros = new MacrosConfig();
		}
		macros.Normalize();
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
