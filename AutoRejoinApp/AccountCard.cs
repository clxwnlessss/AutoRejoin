using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace AutoRejoinApp;

public class AccountCard : Notify
{
	public readonly string Key;

	private string name;

	private string detail;

	private string detailTip;

	private string stateText = "";

	private string uptime = "";

	private string memory = "";

	private string memoryTip = L.T("Память окна");

	private string restarts = "0";

	private string authNote;

	private bool isMain;

	private bool enabled;

	private bool ownLink;

	private bool hasWindow;

	private ImageSource avatar;

	private Brush stateBrush = Palette.Gray;

	private Brush stateBack = Palette.GraySoft;

	private DateTime? startedAt;

	public string UserId { get; private set; }

	public string Name
	{
		get
		{
			return name;
		}
		set
		{
			if (Set(ref name, value, "Name", "Initials"))
			{
				Changed("AvatarBack");
			}
		}
	}

	public string Initials
	{
		get
		{
			string text = (name ?? "?").Trim();
			if (text.Length == 0)
			{
				return "?";
			}
			return text.Substring(0, Math.Min(2, text.Length)).ToUpperInvariant();
		}
	}

	public Brush AvatarBack => Palette.AvatarFor(name);

	public ImageSource Avatar
	{
		get
		{
			return avatar;
		}
		set
		{
			Set(ref avatar, value, "Avatar");
		}
	}

	public bool IsMain => isMain;

	public Visibility MainVisibility
	{
		get
		{
			if (!isMain)
			{
				return Visibility.Collapsed;
			}
			return Visibility.Visible;
		}
	}

	public Visibility OwnLinkVisibility
	{
		get
		{
			if (!ownLink)
			{
				return Visibility.Collapsed;
			}
			return Visibility.Visible;
		}
	}

	public bool Enabled
	{
		get
		{
			return enabled;
		}
		set
		{
			if (Set(ref enabled, value, "Enabled", "RowOpacity"))
			{
				this.EnabledToggled?.Invoke(this);
			}
		}
	}

	public double RowOpacity
	{
		get
		{
			if (!enabled)
			{
				return 0.55;
			}
			return 1.0;
		}
	}

	public string StateText
	{
		get
		{
			return stateText;
		}
		private set
		{
			Set(ref stateText, value, "StateText");
		}
	}

	public Brush StateBrush
	{
		get
		{
			return stateBrush;
		}
		private set
		{
			Set(ref stateBrush, value, "StateBrush");
		}
	}

	public Brush StateBack
	{
		get
		{
			return stateBack;
		}
		private set
		{
			Set(ref stateBack, value, "StateBack");
		}
	}

	public string Detail
	{
		get
		{
			return detail;
		}
		private set
		{
			Set(ref detail, value, "Detail");
		}
	}

	public string DetailTip
	{
		get
		{
			return detailTip;
		}
		private set
		{
			Set(ref detailTip, value, "DetailTip");
		}
	}

	public string Uptime
	{
		get
		{
			return uptime;
		}
		private set
		{
			Set(ref uptime, value, "Uptime");
		}
	}

	public string Memory
	{
		get
		{
			return memory;
		}
		private set
		{
			Set(ref memory, value, "Memory");
		}
	}

	public string MemoryTip
	{
		get
		{
			return memoryTip;
		}
		private set
		{
			Set(ref memoryTip, value, "MemoryTip");
		}
	}

	public string Restarts
	{
		get
		{
			return restarts;
		}
		private set
		{
			Set(ref restarts, value, "Restarts");
		}
	}

	public Visibility MetricsVisibility
	{
		get
		{
			if (!hasWindow)
			{
				return Visibility.Collapsed;
			}
			return Visibility.Visible;
		}
	}

	public string State { get; private set; }

	public bool InGame => State == "ingame";

	public event Action<AccountCard> EnabledToggled;

	public AccountCard(VaultAccount entry)
	{
		Key = entry.id;
		Update(entry);
	}

	public void Update(VaultAccount entry)
	{
		Name = (string.IsNullOrEmpty(entry.name) ? L.T("Аккаунт") : entry.name);
		UserId = entry.userId;
		Set(ref isMain, entry.main, "IsMain", "MainVisibility");
		Set(ref ownLink, !string.IsNullOrWhiteSpace(entry.link), "OwnLinkVisibility");
		Set(ref enabled, entry.enabled, "Enabled", "RowOpacity");
	}

	private void SetState(string state, string text, Brush brush, Brush back)
	{
		State = state;
		StateText = text;
		StateBrush = brush;
		StateBack = back;
	}

	public void ApplyEngine(IDictionary<string, object> data)
	{
		string text = Store.Str(data, "state") ?? "";
		string text2 = L.Engine(Store.Str(data, "text") ?? "");
		string text3 = Store.Str(data, "name");
		if (!string.IsNullOrEmpty(text3) && text3 != "новый аккаунт")
		{
			Name = text3;
		}
		switch (text)
		{
		case "ingame":
			SetState(text, L.T("В игре"), Palette.Green, Palette.GreenSoft);
			break;
		case "script":
			SetState(text, L.T("Ждёт скрипт"), Palette.Blue, Palette.BlueSoft);
			break;
		case "loading":
			SetState(text, L.T("Заходит"), Palette.Blue, Palette.BlueSoft);
			break;
		case "launching":
			SetState(text, L.T("Запускается"), Palette.Blue, Palette.BlueSoft);
			break;
		case "queued":
			SetState(text, L.T("В очереди"), Palette.Gray, Palette.GraySoft);
			break;
		case "waiting":
			SetState(text, L.T("Пауза"), Palette.Yellow, Palette.YellowSoft);
			break;
		case "offline":
			SetState(text, L.T("Отключён"), Palette.Yellow, Palette.YellowSoft);
			break;
		case "deferred":
			SetState(text, L.T("Не пускает"), Palette.Red, Palette.RedSoft);
			break;
		case "problem":
			SetState(text, L.T("Ошибка"), Palette.Red, Palette.RedSoft);
			break;
		case "blocked":
			SetState(text, L.T("Нужна перезагрузка"), Palette.Red, Palette.RedSoft);
			break;
		default:
			SetState(text, "—", Palette.Gray, Palette.GraySoft);
			break;
		}
		double? num = Store.Num(data, "memGB");
		startedAt = Store.Time(data, "startedAt");
		hasWindow = Store.Num(data, "pid").HasValue;
		Changed("MetricsVisibility");
		double? num2 = Store.Num(data, "wsGB");
		double? num3 = Store.Num(data, "capMB");
		string text4 = (num.HasValue ? (num.Value.ToString("0.0", CultureInfo.InvariantCulture) + L.T(" ГБ")) : "");
		if (num2.HasValue)
		{
			text4 = text4 + ((text4.Length > 0) ? " · " : "") + "RAM " + num2.Value.ToString("0.0", CultureInfo.InvariantCulture);
			if (num3.HasValue)
			{
				text4 = text4 + "/" + (num3.Value / 1024.0).ToString("0.0", CultureInfo.InvariantCulture);
			}
			text4 += L.T(" ГБ");
		}
		Memory = text4;
		MemoryTip = L.T("Память окна: сколько оно заняло (вместе с файлом подкачки)") + (num2.HasValue ? L.T(" · RAM — сколько из этого сейчас в оперативной памяти") : "") + (num3.HasValue ? (L.T(" / потолок RAM (") + ((int)num3.Value).ToString(CultureInfo.InvariantCulture) + L.T(" МБ)")) : "");
		Restarts = ((int)(Store.Num(data, "restarts") ?? 0.0)).ToString(CultureInfo.InvariantCulture);
		RefreshUptime();
		string text5 = Capitalize(text2);
		string text6 = text5;
		string text7 = L.Engine(Store.Str(data, "lastReason"));
		DateTime? dateTime = Store.Time(data, "lastReasonAt");
		if (!string.IsNullOrEmpty(text7) && dateTime.HasValue)
		{
			string text8 = dateTime.Value.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
			string text9 = text6;
			text6 = text9 + L.T("\nПоследний перезаход в ") + text8 + ": " + text7;
			if (text == "ingame" || text == "script")
			{
				string text10 = text5;
				text5 = text10 + L.T(" · перезаход в ") + text8 + ": " + text7;
			}
		}
		if (Store.Flag(data, "userOpen"))
		{
			text5 += L.T(" · окном управляешь ты");
			text6 += L.T("\nОкно открыто тобой — не сворачивается и не перезаходит из-за памяти.");
		}
		Detail = text5;
		DetailTip = text6;
		ApplyAuthNote();
	}

	public void SetAuthNote(string note)
	{
		authNote = note;
		ApplyAuthNote();
	}

	private void ApplyAuthNote()
	{
		if (authNote != null)
		{
			SetState("auth", L.T("Вход"), Palette.Blue, Palette.BlueSoft);
			Detail = authNote;
			DetailTip = authNote;
		}
	}

	public void ApplyIdle(string text, string detailText)
	{
		hasWindow = false;
		Changed("MetricsVisibility");
		if (!enabled)
		{
			SetState("disabled", L.T("Выключен"), Palette.Gray, Palette.GraySoft);
		}
		else
		{
			SetState("idle", text, Palette.Gray, Palette.GraySoft);
		}
		Detail = (enabled ? detailText : L.T("Выключен — AutoRejoin его не запускает и не трогает его окно"));
		DetailTip = Detail;
		Memory = "";
		Uptime = "";
		if (enabled)
		{
			ApplyAuthNote();
		}
	}

	public void RefreshUptime()
	{
		if (!startedAt.HasValue || !hasWindow)
		{
			Uptime = "";
			return;
		}
		TimeSpan timeSpan = DateTime.UtcNow - startedAt.Value;
		if (timeSpan.TotalSeconds < 0.0)
		{
			timeSpan = TimeSpan.Zero;
		}
		if (timeSpan.TotalMinutes < 1.0)
		{
			Uptime = (int)timeSpan.TotalSeconds + L.T(" сек");
		}
		else if (timeSpan.TotalHours < 1.0)
		{
			Uptime = (int)timeSpan.TotalMinutes + L.T(" мин");
		}
		else if (timeSpan.TotalDays < 1.0)
		{
			Uptime = (int)timeSpan.TotalHours + L.T(" ч ") + timeSpan.Minutes + L.T(" мин");
		}
		else
		{
			Uptime = (int)timeSpan.TotalDays + L.T(" д ") + timeSpan.Hours + L.T(" ч");
		}
	}

	private static string Capitalize(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return "";
		}
		return char.ToUpper(text[0]) + text.Substring(1);
	}
}
