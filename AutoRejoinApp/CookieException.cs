using System;

namespace AutoRejoinApp;

public class CookieException : Exception
{
	public int Status { get; private set; }

	public CookieException(string message)
		: base(message)
	{
	}

	public CookieException(string message, int status)
		: base(message)
	{
		Status = status;
	}
}
