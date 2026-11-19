using System;
using System.Web.Script.Serialization;

namespace AutoRejoinApp;

public class VaultAccount
{
	public string id { get; set; }

	public string cookie { get; set; }

	public string link { get; set; }

	public bool main { get; set; }

	public bool enabled { get; set; }

	public string userId { get; set; }

	public string name { get; set; }

	public string addedAt { get; set; }

	public string login { get; set; }

	public string username { get; set; }

	public string password { get; set; }

	public string master { get; set; }

	public string cookieAt { get; set; }

	[ScriptIgnore]
	public bool UsesPassword => login == "password";

	public VaultAccount()
	{
		id = Guid.NewGuid().ToString("N").Substring(0, 12);
		cookie = "";
		link = "";
		enabled = true;
		userId = "";
		name = "";
		addedAt = DateTime.UtcNow.ToString("o");
		login = "cookie";
		username = "";
		password = "";
		master = "";
		cookieAt = "";
	}
}
