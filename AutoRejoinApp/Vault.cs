using System.Collections.Generic;

namespace AutoRejoinApp;

public class Vault
{
	public List<VaultAccount> accounts { get; set; }

	public PotassiumLogin potassium { get; set; }

	public string discordWebhook { get; set; }

	public Vault()
	{
		accounts = new List<VaultAccount>();
		potassium = new PotassiumLogin();
	}
}
