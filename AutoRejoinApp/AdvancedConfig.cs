namespace AutoRejoinApp;

public class AdvancedConfig
{
	public double checkInterval { get; set; }

	public int launchTimeout { get; set; }

	public int joinTimeout { get; set; }

	public int outOfGameGrace { get; set; }

	public int hangTimeout { get; set; }

	public int relaunchDelay { get; set; }

	public int launchGap { get; set; }

	public int launchCooldown { get; set; }

	public int potassiumWarmup { get; set; }

	public int memFaultLimit { get; set; }

	public AdvancedConfig()
	{
		memFaultLimit = 3000;
		checkInterval = 1.0;
		launchTimeout = 120;
		joinTimeout = 60;
		outOfGameGrace = 15;
		hangTimeout = 60;
		relaunchDelay = 1;
		launchGap = 2;
		launchCooldown = 3;
		potassiumWarmup = 3;
	}
}
