using System;

namespace AutoRejoinApp;

public class ModerationInfo
{
	public string Type;

	public string Message;

	public DateTime? EndUtc;

	public bool IsWarning => string.Equals(Type, "Warn", StringComparison.OrdinalIgnoreCase);

	public bool CanReactivate
	{
		get
		{
			if (!IsWarning)
			{
				if (EndUtc.HasValue)
				{
					return EndUtc.Value <= DateTime.UtcNow;
				}
				return false;
			}
			return true;
		}
	}

	public string Describe()
	{
		string text = (IsWarning ? L.T("предупреждение") : (L.T("бан (") + Type + ")"));
		if (!IsWarning && EndUtc.HasValue)
		{
			text = text + L.T(" до ") + EndUtc.Value.ToLocalTime().ToString("dd.MM HH:mm");
		}
		if (!string.IsNullOrEmpty(Message))
		{
			text = text + ": " + ((Message.Length > 220) ? (Message.Substring(0, 220) + "…") : Message);
		}
		return text;
	}
}
