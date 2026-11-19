namespace AutoRejoinApp;

public class ModeratedException : CookieException
{
	public ModeratedException(string message)
		: base(message, 403)
	{
	}
}
