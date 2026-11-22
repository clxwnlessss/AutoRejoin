using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace AutoRejoinApp;

// In-game macros from OverFlux (by clxwnlesss): Auto Star Saw, Auto Jelly Beans, Saw + Beans and Auto Clicker.
// Each macro is a loop that presses hotbar keys / clicks into the focused window, started and stopped by a global hotkey
// or from the Macros page.

// ---------------- config (saved in config.json → "macros") ----------------
public class MacroBase
{
	public string hotkey { get; set; }

	public int startDelayMs { get; set; }

	public int jitterMs { get; set; }

	public int stopAfterMin { get; set; }

	public int maxCycles { get; set; }
}

public class StarSawMacro : MacroBase
{
	public string slot { get; set; } = "3";

	public int intervalMs { get; set; } = 10000;

	public int holdMs { get; set; } = 50;

	public int pressesPerCycle { get; set; } = 1;

	public int pressGapMs { get; set; } = 150;

	public StarSawMacro()
	{
		hotkey = "F6";
	}
}

public class JellyBeansMacro : MacroBase
{
	public string slot { get; set; } = "4";

	public int intervalMs { get; set; } = 35000;

	public int holdMs { get; set; } = 50;

	public JellyBeansMacro()
	{
		hotkey = "F7";
	}
}

public class ComboMacro : MacroBase
{
	public string sawSlot { get; set; } = "3";

	public string beansSlot { get; set; } = "4";

	public bool beansFirst { get; set; } = true;

	public int gapMs { get; set; } = 500;

	public int intervalMs { get; set; } = 10000;

	public int holdMs { get; set; } = 50;

	public ComboMacro()
	{
		hotkey = "F9";
	}
}

public class ClickerMacro : MacroBase
{
	public int cps { get; set; } = 10;

	public string button { get; set; } = "left";

	public bool doubleClick { get; set; }

	public ClickerMacro()
	{
		hotkey = "F8";
	}
}

public class MacroTotals
{
	public long actions { get; set; }

	public long runtimeMs { get; set; }

	public int runs { get; set; }
}

public class MacrosConfig
{
	public bool robloxOnly { get; set; } = true;

	public string emergencyStop { get; set; } = "F3";

	public StarSawMacro starSaw { get; set; } = new StarSawMacro();

	public JellyBeansMacro jellyBeans { get; set; } = new JellyBeansMacro();

	public ComboMacro combo { get; set; } = new ComboMacro();

	public ClickerMacro clicker { get; set; } = new ClickerMacro();

	public Dictionary<string, MacroTotals> totals { get; set; } = new Dictionary<string, MacroTotals>();

	public void Normalize()
	{
		if (starSaw == null)
		{
			starSaw = new StarSawMacro();
		}
		if (jellyBeans == null)
		{
			jellyBeans = new JellyBeansMacro();
		}
		if (combo == null)
		{
			combo = new ComboMacro();
		}
		if (clicker == null)
		{
			clicker = new ClickerMacro();
		}
		if (totals == null)
		{
			totals = new Dictionary<string, MacroTotals>();
		}
		if (emergencyStop == null)
		{
			emergencyStop = "F3";
		}
		foreach (MacroBase m in new MacroBase[4] { starSaw, jellyBeans, combo, clicker })
		{
			m.hotkey = m.hotkey ?? "";
			m.startDelayMs = Clamp(m.startDelayMs, 0, 600000);
			m.jitterMs = Clamp(m.jitterMs, 0, 60000);
			m.stopAfterMin = Clamp(m.stopAfterMin, 0, 1440);
			m.maxCycles = Clamp(m.maxCycles, 0, 1000000);
		}
		starSaw.intervalMs = Clamp(starSaw.intervalMs, 100, 3600000);
		starSaw.holdMs = Clamp(starSaw.holdMs, 10, 2000);
		starSaw.pressesPerCycle = Clamp(starSaw.pressesPerCycle, 1, 20);
		starSaw.pressGapMs = Clamp(starSaw.pressGapMs, 0, 10000);
		jellyBeans.intervalMs = Clamp(jellyBeans.intervalMs, 100, 3600000);
		jellyBeans.holdMs = Clamp(jellyBeans.holdMs, 10, 2000);
		combo.intervalMs = Clamp(combo.intervalMs, 100, 3600000);
		combo.holdMs = Clamp(combo.holdMs, 10, 2000);
		combo.gapMs = Clamp(combo.gapMs, 0, 10000);
		clicker.cps = Clamp(clicker.cps, 1, 500);
		if (clicker.button != "right" && clicker.button != "middle")
		{
			clicker.button = "left";
		}
		starSaw.slot = Slot(starSaw.slot, "3");
		jellyBeans.slot = Slot(jellyBeans.slot, "4");
		combo.sawSlot = Slot(combo.sawSlot, "3");
		combo.beansSlot = Slot(combo.beansSlot, "4");
	}

	private static int Clamp(int v, int a, int b)
	{
		return Math.Max(a, Math.Min(b, v));
	}

	private static string Slot(string s, string fallback)
	{
		return (MacroInput.ToVk(s ?? "") != 0) ? s.Trim() : fallback;
	}
}

// ---------------- input (keybd_event / mouse_event with real scan codes, like OverFlux) ----------------
public static class MacroInput
{
	[DllImport("user32.dll")]
	private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

	[DllImport("user32.dll")]
	private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

	[DllImport("user32.dll")]
	private static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

	[DllImport("user32.dll")]
	private static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

	private const uint KEYEVENTF_EXTENDEDKEY = 1u;

	private const uint KEYEVENTF_KEYUP = 2u;

	private static readonly Dictionary<string, int> Named = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
	{
		["SPACE"] = 0x20, ["ENTER"] = 0x0D, ["RETURN"] = 0x0D, ["TAB"] = 0x09, ["ESC"] = 0x1B, ["ESCAPE"] = 0x1B,
		["SHIFT"] = 0x10, ["CTRL"] = 0x11, ["CONTROL"] = 0x11, ["ALT"] = 0x12, ["BACKSPACE"] = 0x08,
		["UP"] = 0x26, ["DOWN"] = 0x28, ["LEFT"] = 0x25, ["RIGHT"] = 0x27,
		["-"] = 0xBD, ["="] = 0xBB, [","] = 0xBC, ["."] = 0xBE, ["/"] = 0xBF, [";"] = 0xBA, ["'"] = 0xDE,
		["["] = 0xDB, ["]"] = 0xDD, ["\\"] = 0xDC, ["`"] = 0xC0
	};

	/// "3" / "E" / "F5" / "Space" / "Num4" → virtual-key code, 0 if unknown.
	public static int ToVk(string key)
	{
		string k = (key ?? "").Trim().ToUpperInvariant();
		if (k.Length == 1 && ((k[0] >= '0' && k[0] <= '9') || (k[0] >= 'A' && k[0] <= 'Z')))
		{
			return k[0];
		}
		if (k.Length >= 2 && k[0] == 'F' && int.TryParse(k.Substring(1), out var f) && f >= 1 && f <= 24)
		{
			return 0x70 + f - 1;
		}
		if (k.Length == 4 && k.StartsWith("NUM") && char.IsDigit(k[3]))
		{
			return 0x60 + (k[3] - '0');
		}
		return Named.TryGetValue(k, out var vk) ? vk : 0;
	}

	public static async Task<bool> PressKey(string key, int holdMs, CancellationToken token)
	{
		int vk = ToVk(key);
		if (vk == 0)
		{
			return false;
		}
		byte scan = (byte)(MapVirtualKeyW((uint)vk, 0u) & 0xFF);
		uint ext = (vk >= 0x25 && vk <= 0x28) ? KEYEVENTF_EXTENDEDKEY : 0u;
		keybd_event((byte)vk, scan, ext, UIntPtr.Zero);
		try
		{
			await Task.Delay(Math.Max(10, holdMs), token);
		}
		finally
		{
			keybd_event((byte)vk, scan, ext | KEYEVENTF_KEYUP, UIntPtr.Zero);
		}
		return true;
	}

	public static void Click(string button)
	{
		uint down = 2u, up = 4u;
		if (button == "right")
		{
			down = 8u;
			up = 16u;
		}
		else if (button == "middle")
		{
			down = 32u;
			up = 64u;
		}
		mouse_event(down, 0u, 0u, 0u, UIntPtr.Zero);
		mouse_event(up, 0u, 0u, 0u, UIntPtr.Zero);
	}

	/// Is a Roblox window (RobloxPlayerBeta) the active window right now.
	public static bool IsRobloxFocused()
	{
		try
		{
			IntPtr hwnd = GetForegroundWindow();
			if (hwnd == IntPtr.Zero)
			{
				return false;
			}
			GetWindowThreadProcessId(hwnd, out var pid);
			using Process p = Process.GetProcessById((int)pid);
			return p.ProcessName.Equals("RobloxPlayerBeta", StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}
}

// ---------------- runner ----------------
public class MacroState
{
	public bool Running;

	public bool Paused;

	public DateTime? StartedAt;

	public DateTime? NextActionAt;

	public int Cycles;

	public long Actions;
}

public class MacroContext
{
	private readonly MacroRunner runner;

	private readonly CancellationToken token;

	private static readonly Random Rng = new Random();

	internal MacroContext(MacroRunner runner, CancellationToken token)
	{
		this.runner = runner;
		this.token = token;
	}

	public Task Sleep(int ms)
	{
		return Task.Delay(Math.Max(0, ms), token);
	}

	/// Wait for the next cycle — the page shows a countdown.
	public async Task Wait(int ms)
	{
		runner.State.NextActionAt = DateTime.Now.AddMilliseconds(ms);
		runner.Changed();
		await Task.Delay(Math.Max(0, ms), token);
		runner.State.NextActionAt = null;
	}

	public async Task Press(string key, int holdMs)
	{
		await runner.WaitFocus(token);
		token.ThrowIfCancellationRequested();
		await MacroInput.PressKey(key, holdMs, token);
		runner.Count(1);
	}

	public async Task Click(string button, int times)
	{
		if (runner.RobloxOnly() && !MacroInput.IsRobloxFocused())
		{
			await runner.WaitFocus(token);
			return;
		}
		for (int i = 0; i < times; i++)
		{
			MacroInput.Click(button);
		}
		runner.Count(times);
	}

	public void CycleDone()
	{
		runner.CycleDone();
	}

	public int Jitter(int ms, int amount)
	{
		if (amount <= 0)
		{
			return Math.Max(0, ms);
		}
		lock (Rng)
		{
			return Math.Max(0, ms + (int)Math.Round((Rng.NextDouble() * 2.0 - 1.0) * amount));
		}
	}
}

public class MacroRunner
{
	public readonly string Id;

	public MacroState State = new MacroState();

	internal readonly Func<bool> RobloxOnly;

	private CancellationTokenSource cts;

	private int maxCycles;

	private DateTime lastEmit = DateTime.MinValue;

	/// Raised on a worker thread when the state changes (throttled for fast macros like the clicker).
	public event Action<MacroRunner> StateChanged;

	/// (runner, runtime, reason) when the macro stops.
	public event Action<MacroRunner, TimeSpan, string> Stopped;

	public event Action<MacroRunner, int> Acted;

	public MacroRunner(string id, Func<bool> robloxOnly)
	{
		Id = id;
		RobloxOnly = robloxOnly;
	}

	public bool Running => State.Running;

	public void Start(Func<MacroContext, Task> loop, MacroBase opts)
	{
		if (State.Running)
		{
			return;
		}
		CancellationTokenSource source = new CancellationTokenSource();
		cts = source;
		maxCycles = opts.maxCycles;
		State = new MacroState
		{
			Running = true,
			StartedAt = DateTime.Now
		};
		Changed(force: true);
		MacroContext ctx = new MacroContext(this, source.Token);
		if (opts.stopAfterMin > 0)
		{
			Task.Delay(TimeSpan.FromMinutes(opts.stopAfterMin), source.Token).ContinueWith(delegate(Task t)
			{
				if (!t.IsCanceled)
				{
					Stop(string.Format(L.T("таймер {0} мин"), opts.stopAfterMin));
				}
			}, TaskScheduler.Default);
		}
		Task.Run(async delegate
		{
			try
			{
				if (opts.startDelayMs > 0)
				{
					await ctx.Wait(opts.startDelayMs);
				}
				while (!source.IsCancellationRequested)
				{
					await loop(ctx);
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex)
			{
				Stop(L.T("ошибка: ") + ex.Message);
			}
		});
	}

	public void Stop(string reason)
	{
		CancellationTokenSource source = cts;
		if (!State.Running || source == null)
		{
			return;
		}
		cts = null;
		source.Cancel();
		TimeSpan runtime = State.StartedAt.HasValue ? (DateTime.Now - State.StartedAt.Value) : TimeSpan.Zero;
		State.Running = false;
		State.Paused = false;
		State.NextActionAt = null;
		Changed(force: true);
		Stopped?.Invoke(this, runtime, reason);
	}

	internal async Task WaitFocus(CancellationToken token)
	{
		if (!RobloxOnly() || MacroInput.IsRobloxFocused())
		{
			return;
		}
		State.Paused = true;
		Changed(force: true);
		while (RobloxOnly() && !MacroInput.IsRobloxFocused())
		{
			await Task.Delay(250, token);
		}
		State.Paused = false;
		Changed(force: true);
	}

	internal void Count(int n)
	{
		State.Actions += n;
		Acted?.Invoke(this, n);
		Changed();
	}

	internal void CycleDone()
	{
		State.Cycles++;
		Changed(force: true);
		if (maxCycles > 0 && State.Cycles >= maxCycles)
		{
			Stop(string.Format(L.T("{0} циклов"), maxCycles));
		}
	}

	internal void Changed(bool force = false)
	{
		DateTime now = DateTime.Now;
		if (force || (now - lastEmit).TotalMilliseconds > 100.0)
		{
			lastEmit = now;
			StateChanged?.Invoke(this);
		}
	}
}

// ---------------- the four macros ----------------
public static class MacroLibrary
{
	public static readonly string[] Ids = new string[4] { "starSaw", "jellyBeans", "clicker", "combo" };

	public static string Name(string id)
	{
		return id switch
		{
			"starSaw" => "Auto Star Saw",
			"jellyBeans" => "Auto Jelly Beans",
			"combo" => "Saw + Beans",
			"clicker" => "Auto Clicker",
			_ => id,
		};
	}

	public static MacroBase Options(MacrosConfig m, string id)
	{
		return id switch
		{
			"starSaw" => m.starSaw,
			"jellyBeans" => m.jellyBeans,
			"combo" => m.combo,
			_ => m.clicker,
		};
	}

	public static Func<MacroContext, Task> Build(MacrosConfig m, string id)
	{
		switch (id)
		{
		case "starSaw":
		{
			StarSawMacro c = m.starSaw;
			return async delegate(MacroContext ctx)
			{
				for (int i = 0; i < Math.Max(1, c.pressesPerCycle); i++)
				{
					if (i > 0)
					{
						await ctx.Sleep(c.pressGapMs);
					}
					await ctx.Press(c.slot, c.holdMs);
				}
				ctx.CycleDone();
				await ctx.Wait(ctx.Jitter(c.intervalMs, c.jitterMs));
			};
		}
		case "jellyBeans":
		{
			JellyBeansMacro c = m.jellyBeans;
			return async delegate(MacroContext ctx)
			{
				await ctx.Press(c.slot, c.holdMs);
				ctx.CycleDone();
				await ctx.Wait(ctx.Jitter(c.intervalMs, c.jitterMs));
			};
		}
		case "combo":
		{
			ComboMacro c = m.combo;
			string first = c.beansFirst ? c.beansSlot : c.sawSlot;
			string second = c.beansFirst ? c.sawSlot : c.beansSlot;
			return async delegate(MacroContext ctx)
			{
				await ctx.Press(first, c.holdMs);
				await ctx.Sleep(c.gapMs);
				await ctx.Press(second, c.holdMs);
				ctx.CycleDone();
				await ctx.Wait(ctx.Jitter(c.intervalMs, c.jitterMs));
			};
		}
		default:
		{
			// Time-based batching keeps high CPS accurate (Task.Delay alone ticks at ~15 ms).
			ClickerMacro c = m.clicker;
			int cps = Math.Min(500, Math.Max(1, c.cps));
			int perClick = c.doubleClick ? 2 : 1;
			if (cps <= 60)
			{
				return async delegate(MacroContext ctx)
				{
					await ctx.Click(c.button, perClick);
					ctx.CycleDone();
					await ctx.Sleep(ctx.Jitter((int)Math.Round(1000.0 / cps), c.jitterMs));
				};
			}
			Stopwatch watch = null;
			long done = 0L;
			return async delegate(MacroContext ctx)
			{
				if (watch == null)
				{
					watch = Stopwatch.StartNew();
				}
				long due = watch.ElapsedMilliseconds * cps / 1000 - done;
				if (due > 0)
				{
					int n = (int)Math.Min(due, 50L);
					await ctx.Click(c.button, n * perClick);
					done += n;
					ctx.CycleDone();
				}
				await ctx.Sleep(4);
			};
		}
		}
	}
}

// ---------------- global hotkeys (RegisterHotKey on the main window) ----------------
public class GlobalHotkeys : IDisposable
{
	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

	[DllImport("user32.dll")]
	private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

	private const int WM_HOTKEY = 0x0312;

	private const uint MOD_NOREPEAT = 0x4000;

	private readonly IntPtr hwnd;

	private readonly HwndSource source;

	private readonly Dictionary<int, Action> actions = new Dictionary<int, Action>();

	private int nextId = 0xA100;

	public GlobalHotkeys(Window window)
	{
		hwnd = new WindowInteropHelper(window).EnsureHandle();
		source = HwndSource.FromHwnd(hwnd);
		source.AddHook(Hook);
	}

	/// "F6", "Ctrl+Shift+O" → modifiers + vk; (0, 0) if it can't be parsed.
	public static void Parse(string text, out uint mods, out uint vk)
	{
		mods = 0u;
		vk = 0u;
		if (string.IsNullOrWhiteSpace(text))
		{
			return;
		}
		string[] parts = text.Split('+');
		for (int i = 0; i < parts.Length; i++)
		{
			string p = parts[i].Trim().ToUpperInvariant();
			if (i < parts.Length - 1)
			{
				if (p == "CTRL" || p == "CONTROL")
				{
					mods |= 2u;
				}
				else if (p == "ALT")
				{
					mods |= 1u;
				}
				else if (p == "SHIFT")
				{
					mods |= 4u;
				}
				else if (p == "WIN")
				{
					mods |= 8u;
				}
			}
			else
			{
				vk = (uint)MacroInput.ToVk(p);
			}
		}
	}

	/// Registers all hotkeys again. Returns the keys that could not be registered (taken by another program).
	public List<string> Set(IEnumerable<KeyValuePair<string, Action>> keys)
	{
		Clear();
		List<string> failed = new List<string>();
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, Action> pair in keys)
		{
			Parse(pair.Key, out var mods, out var vk);
			if (vk == 0 || !seen.Add(pair.Key.Trim()))
			{
				continue;
			}
			int id = nextId++;
			if (RegisterHotKey(hwnd, id, mods | MOD_NOREPEAT, vk))
			{
				actions[id] = pair.Value;
			}
			else
			{
				failed.Add(pair.Key);
			}
		}
		return failed;
	}

	public void Clear()
	{
		foreach (int id in actions.Keys)
		{
			UnregisterHotKey(hwnd, id);
		}
		actions.Clear();
	}

	private IntPtr Hook(IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
	{
		if (msg == WM_HOTKEY && actions.TryGetValue(wParam.ToInt32(), out var action))
		{
			handled = true;
			action();
		}
		return IntPtr.Zero;
	}

	public void Dispose()
	{
		Clear();
		source.RemoveHook(Hook);
	}
}
