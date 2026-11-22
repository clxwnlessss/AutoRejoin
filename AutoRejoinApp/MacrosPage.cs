using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AutoRejoinApp;

// "Macros" page: the OverFlux macros (Star Saw, Jelly Beans, Auto Clicker, Saw + Beans) with start/stop, live status,
// global hotkeys (F6–F9, emergency stop F3) and per-macro settings saved in config.json.
public class MacrosPage
{
	private class Card
	{
		public string Id;

		public MacroRunner Runner;

		public Button Toggle;

		public TextBlock Status;

		public TextBlock Totals;

		public TextBlock HotkeyBadge;

		public Border Dot;

		public long PendingActions;
	}

	private readonly MainController main;

	private readonly StackPanel host;

	private readonly Dictionary<string, Card> cards = new Dictionary<string, Card>();

	private readonly DispatcherTimer timer;

	private GlobalHotkeys hotkeys;

	private TextBlock hotkeyWarning;

	private MacrosConfig M => main.Config.macros;

	public bool AnyRunning => cards.Values.Any((Card c) => c.Runner.Running);

	public MacrosPage(MainController main, StackPanel host)
	{
		this.main = main;
		this.host = host;
		foreach (string id in MacroLibrary.Ids)
		{
			MacroRunner runner = new MacroRunner(id, () => M.robloxOnly);
			Card card = new Card
			{
				Id = id,
				Runner = runner
			};
			runner.Acted += delegate(MacroRunner r, int n)
			{
				System.Threading.Interlocked.Add(ref card.PendingActions, n);
			};
			runner.Stopped += delegate(MacroRunner r, TimeSpan runtime, string reason)
			{
				main.Window.Dispatcher.BeginInvoke((Action)delegate
				{
					FlushTotals(card, runtime);
					main.AppLog(null, MacroLibrary.Name(id) + L.T(" остановлен: ") + reason, "Gray");
				});
			};
			cards[id] = card;
		}
		Build();
		timer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(250.0)
		};
		timer.Tick += delegate
		{
			Refresh();
		};
		timer.Start();
		main.Window.SourceInitialized += delegate
		{
			RegisterHotkeys();
		};
		if (new System.Windows.Interop.WindowInteropHelper(main.Window).Handle != IntPtr.Zero)
		{
			RegisterHotkeys();
		}
	}

	// ---------- start / stop ----------
	public void Toggle(string id)
	{
		Card card = cards[id];
		if (card.Runner.Running)
		{
			card.Runner.Stop(L.T("остановлен вручную"));
		}
		else
		{
			Start(card);
		}
		Refresh();
	}

	private void Start(Card card)
	{
		M.Normalize();
		card.Runner.Start(MacroLibrary.Build(M, card.Id), MacroLibrary.Options(M, card.Id));
		main.AppLog(null, MacroLibrary.Name(card.Id) + L.T(" запущен") + (M.robloxOnly ? L.T(" (жмёт только в окне Роблокса)") : ""), "Cyan");
	}

	public void StopAll(string reason)
	{
		foreach (Card card in cards.Values)
		{
			card.Runner.Stop(reason);
		}
		Refresh();
	}

	// Settings changed while a macro runs — restart it with the new values.
	private void Changed(string id)
	{
		M.Normalize();
		main.ConfigChanged();
		if (id != null && cards[id].Runner.Running)
		{
			cards[id].Runner.Stop(L.T("новые настройки"));
			Start(cards[id]);
		}
	}

	private void FlushTotals(Card card, TimeSpan runtime)
	{
		if (!M.totals.TryGetValue(card.Id, out var t))
		{
			t = (M.totals[card.Id] = new MacroTotals());
		}
		t.actions += System.Threading.Interlocked.Exchange(ref card.PendingActions, 0L);
		if (runtime > TimeSpan.Zero)
		{
			t.runtimeMs += (long)runtime.TotalMilliseconds;
			t.runs++;
		}
		main.ConfigChanged();
	}

	// ---------- hotkeys ----------
	private void RegisterHotkeys()
	{
		try
		{
			if (hotkeys == null)
			{
				hotkeys = new GlobalHotkeys(main.Window);
			}
			List<KeyValuePair<string, Action>> keys = new List<KeyValuePair<string, Action>>();
			if (!string.IsNullOrWhiteSpace(M.emergencyStop))
			{
				keys.Add(new KeyValuePair<string, Action>(M.emergencyStop, delegate
				{
					StopAll(L.T("стоп-клавиша ") + M.emergencyStop);
				}));
			}
			foreach (string id in MacroLibrary.Ids)
			{
				string key = MacroLibrary.Options(M, id).hotkey;
				if (!string.IsNullOrWhiteSpace(key))
				{
					keys.Add(new KeyValuePair<string, Action>(key, delegate
					{
						Toggle(id);
					}));
				}
			}
			List<string> failed = hotkeys.Set(keys);
			if (hotkeyWarning != null)
			{
				hotkeyWarning.Text = failed.Count == 0 ? "" : (L.T("Эти клавиши заняты другой программой: ") + string.Join(", ", failed));
				hotkeyWarning.Visibility = failed.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
			}
		}
		catch (Exception ex)
		{
			App.LogError(L.T("горячие клавиши макросов"), ex);
		}
	}

	public void Dispose()
	{
		StopAll(L.T("выход из программы"));
		timer.Stop();
		hotkeys?.Dispose();
	}

	// ---------- UI ----------
	private void Build()
	{
		host.Children.Clear();
		BuildGeneral();
		foreach (string id in MacroLibrary.Ids)
		{
			BuildCard(cards[id]);
		}
		Refresh();
	}

	private void BuildGeneral()
	{
		Border card = new Border
		{
			Style = Res("Card"),
			Margin = new Thickness(0.0, 0.0, 0.0, 14.0),
			Padding = new Thickness(24.0, 16.0, 24.0, 16.0)
		};
		StackPanel panel = new StackPanel();
		Grid row = new Grid();
		row.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		row.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		StackPanel text = new StackPanel
		{
			VerticalAlignment = VerticalAlignment.Center
		};
		text.Children.Add(new TextBlock
		{
			Text = L.T("Только в окне Роблокса"),
			FontSize = 14.0,
			FontWeight = FontWeights.SemiBold
		});
		text.Children.Add(Hint(L.T("Пока активно другое окно, макросы ждут и ничего не нажимают.")));
		row.Children.Add(text);
		CheckBox only = Switch(M.robloxOnly, delegate(bool v)
		{
			M.robloxOnly = v;
			Changed(null);
		});
		Grid.SetColumn(only, 1);
		row.Children.Add(only);
		panel.Children.Add(row);
		WrapPanel line = new WrapPanel
		{
			Margin = new Thickness(0.0, 14.0, 0.0, 0.0)
		};
		line.Children.Add(Labeled(L.T("Стоп-всё"), HotkeyBox(M.emergencyStop, delegate(string v)
		{
			M.emergencyStop = v;
			Changed(null);
			RegisterHotkeys();
		})));
		Button stopAll = new Button
		{
			Content = L.T("Остановить все"),
			Style = Res("Btn"),
			VerticalAlignment = VerticalAlignment.Bottom,
			Margin = new Thickness(0.0, 0.0, 0.0, 2.0)
		};
		stopAll.Click += delegate
		{
			StopAll(L.T("остановлены вручную"));
		};
		line.Children.Add(stopAll);
		panel.Children.Add(line);
		hotkeyWarning = Hint("");
		hotkeyWarning.Foreground = Palette.Red;
		hotkeyWarning.Margin = new Thickness(0.0, 10.0, 0.0, 0.0);
		hotkeyWarning.Visibility = Visibility.Collapsed;
		panel.Children.Add(hotkeyWarning);
		card.Child = panel;
		host.Children.Add(card);
	}

	private void BuildCard(Card c)
	{
		string id = c.Id;
		MacroBase o = MacroLibrary.Options(M, id);
		Border card = new Border
		{
			Style = Res("Card"),
			Margin = new Thickness(0.0, 0.0, 0.0, 14.0),
			Padding = new Thickness(24.0, 18.0, 24.0, 18.0)
		};
		StackPanel panel = new StackPanel();
		Grid head = new Grid();
		head.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		head.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		head.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		Border tile = new Border
		{
			Width = 40.0,
			Height = 40.0,
			CornerRadius = new CornerRadius(11.0),
			Background = Palette.Make(ThemeInfo.Neon ? "#2A1050" : "#121212"),
			BorderBrush = Palette.Make(ThemeInfo.Neon ? "#7C3AED" : "#262626"),
			BorderThickness = new Thickness(1.0),
			Child = Ico.Make(id == "clicker" ? "dot" : (id == "combo" ? "bolt" : (id == "starSaw" ? "star" : "hex")), 18.0, Palette.Make(ThemeInfo.Neon ? "#E9D5FF" : "#EDEDED"))
		};
		head.Children.Add(tile);
		StackPanel titles = new StackPanel
		{
			Margin = new Thickness(14.0, 0.0, 0.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center
		};
		StackPanel nameRow = new StackPanel
		{
			Orientation = Orientation.Horizontal
		};
		c.Dot = new Border
		{
			Width = 8.0,
			Height = 8.0,
			CornerRadius = new CornerRadius(4.0),
			Margin = new Thickness(0.0, 0.0, 8.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center
		};
		nameRow.Children.Add(c.Dot);
		nameRow.Children.Add(new TextBlock
		{
			Text = MacroLibrary.Name(id),
			Style = Res("H2"),
			FontSize = 16.0
		});
		c.HotkeyBadge = new TextBlock
		{
			FontSize = 11.5,
			FontWeight = FontWeights.SemiBold,
			Foreground = Palette.Text2,
			Margin = new Thickness(10.0, 0.0, 0.0, 0.0),
			VerticalAlignment = VerticalAlignment.Center
		};
		nameRow.Children.Add(c.HotkeyBadge);
		titles.Children.Add(nameRow);
		c.Status = Hint("");
		titles.Children.Add(c.Status);
		Grid.SetColumn(titles, 1);
		head.Children.Add(titles);
		c.Toggle = new Button
		{
			MinWidth = 120.0,
			VerticalAlignment = VerticalAlignment.Center
		};
		c.Toggle.Click += delegate
		{
			Toggle(id);
		};
		Grid.SetColumn(c.Toggle, 2);
		head.Children.Add(c.Toggle);
		panel.Children.Add(head);
		panel.Children.Add(Hint(Describe(id)));
		((TextBlock)panel.Children[1]).Margin = new Thickness(54.0, 8.0, 0.0, 0.0);

		WrapPanel fields = new WrapPanel
		{
			Margin = new Thickness(54.0, 12.0, 0.0, 0.0)
		};
		switch (id)
		{
		case "starSaw":
		{
			StarSawMacro s = M.starSaw;
			fields.Children.Add(Labeled(L.T("Слот"), SlotBox(s.slot, delegate(string v) { s.slot = v; Changed(id); })));
			fields.Children.Add(Labeled(L.T("Каждые, сек"), Seconds(s.intervalMs, delegate(int v) { s.intervalMs = v; Changed(id); })));
			fields.Children.Add(Labeled(L.T("Нажатий за раз"), Number(s.pressesPerCycle, 1, 20, delegate(double v) { s.pressesPerCycle = (int)v; Changed(id); })));
			fields.Children.Add(Labeled(L.T("Пауза между, мс"), Number(s.pressGapMs, 0, 10000, delegate(double v) { s.pressGapMs = (int)v; Changed(id); })));
			fields.Children.Add(Labeled(L.T("Держать, мс"), Number(s.holdMs, 10, 2000, delegate(double v) { s.holdMs = (int)v; Changed(id); })));
			break;
		}
		case "jellyBeans":
		{
			JellyBeansMacro j = M.jellyBeans;
			fields.Children.Add(Labeled(L.T("Слот"), SlotBox(j.slot, delegate(string v) { j.slot = v; Changed(id); })));
			fields.Children.Add(Labeled(L.T("Каждые, сек"), Seconds(j.intervalMs, delegate(int v) { j.intervalMs = v; Changed(id); })));
			fields.Children.Add(Labeled(L.T("Держать, мс"), Number(j.holdMs, 10, 2000, delegate(double v) { j.holdMs = (int)v; Changed(id); })));
			break;
		}
		case "combo":
		{
			ComboMacro b = M.combo;
			fields.Children.Add(Labeled(L.T("Слот Saw"), SlotBox(b.sawSlot, delegate(string v) { b.sawSlot = v; Changed(id); })));
			fields.Children.Add(Labeled(L.T("Слот Beans"), SlotBox(b.beansSlot, delegate(string v) { b.beansSlot = v; Changed(id); })));
			fields.Children.Add(Labeled(L.T("Каждые, сек"), Seconds(b.intervalMs, delegate(int v) { b.intervalMs = v; Changed(id); })));
			fields.Children.Add(Labeled(L.T("Между, мс"), Number(b.gapMs, 0, 10000, delegate(double v) { b.gapMs = (int)v; Changed(id); })));
			fields.Children.Add(Labeled(L.T("Держать, мс"), Number(b.holdMs, 10, 2000, delegate(double v) { b.holdMs = (int)v; Changed(id); })));
			fields.Children.Add(Labeled(L.T("Beans первыми"), Switch(b.beansFirst, delegate(bool v) { b.beansFirst = v; Changed(id); })));
			break;
		}
		default:
		{
			ClickerMacro k = M.clicker;
			fields.Children.Add(Labeled(L.T("Кликов в сек"), Number(k.cps, 1, 500, delegate(double v) { k.cps = (int)v; Changed(id); })));
			ComboBox button = new ComboBox
			{
				Style = Res("Combo"),
				Width = 120.0,
				Height = 38.0
			};
			foreach (string b2 in new string[3] { "left", "right", "middle" })
			{
				button.Items.Add(new ChoiceItem
				{
					Value = b2,
					Name = b2 == "left" ? L.T("Левая") : (b2 == "right" ? L.T("Правая") : L.T("Средняя"))
				});
			}
			button.SelectedItem = button.Items.Cast<ChoiceItem>().First((ChoiceItem i) => i.Value == k.button);
			button.SelectionChanged += delegate
			{
				if (button.SelectedItem is ChoiceItem ci)
				{
					k.button = ci.Value;
					Changed(id);
				}
			};
			fields.Children.Add(Labeled(L.T("Кнопка"), button));
			fields.Children.Add(Labeled(L.T("Двойной клик"), Switch(k.doubleClick, delegate(bool v) { k.doubleClick = v; Changed(id); })));
			break;
		}
		}
		fields.Children.Add(Labeled(L.T("Клавиша"), HotkeyBox(o.hotkey, delegate(string v)
		{
			o.hotkey = v;
			Changed(null);
			RegisterHotkeys();
		})));
		panel.Children.Add(fields);

		WrapPanel extra = new WrapPanel
		{
			Margin = new Thickness(54.0, 2.0, 0.0, 0.0)
		};
		extra.Children.Add(Labeled(L.T("Старт через, мс"), Number(o.startDelayMs, 0, 600000, delegate(double v) { o.startDelayMs = (int)v; Changed(id); })));
		extra.Children.Add(Labeled(L.T("Разброс ±, мс"), Number(o.jitterMs, 0, 60000, delegate(double v) { o.jitterMs = (int)v; Changed(id); })));
		extra.Children.Add(Labeled(L.T("Стоп через, мин"), Number(o.stopAfterMin, 0, 1440, delegate(double v) { o.stopAfterMin = (int)v; Changed(id); })));
		extra.Children.Add(Labeled(L.T("Макс. циклов"), Number(o.maxCycles, 0, 1000000, delegate(double v) { o.maxCycles = (int)v; Changed(id); })));
		panel.Children.Add(extra);
		c.Totals = Hint("");
		c.Totals.Margin = new Thickness(54.0, 4.0, 0.0, 0.0);
		panel.Children.Add(c.Totals);
		card.Child = panel;
		host.Children.Add(card);
	}

	private static string Describe(string id)
	{
		return id switch
		{
			"starSaw" => L.T("Жмёт слот Star Saw раз в N секунд."),
			"jellyBeans" => L.T("Жмёт слот Jelly Beans раз в N секунд."),
			"combo" => L.T("Jelly Beans и Star Saw по очереди раз в N секунд."),
			_ => L.T("Кликает мышью с заданной скоростью (до 500 в сек)."),
		};
	}

	private void Refresh()
	{
		foreach (Card c in cards.Values)
		{
			MacroState s = c.Runner.State;
			MacroBase o = MacroLibrary.Options(M, c.Id);
			c.HotkeyBadge.Text = string.IsNullOrWhiteSpace(o.hotkey) ? "" : ("[" + o.hotkey + "]");
			c.Toggle.Content = s.Running ? L.T("Стоп") : L.T("Старт");
			c.Toggle.Style = Res(s.Running ? "Btn" : "BtnPrimary");
			c.Dot.Background = !s.Running ? Palette.Text3 : (s.Paused ? Palette.Yellow : Palette.Green);
			if (!s.Running)
			{
				c.Status.Text = L.T("Остановлен");
			}
			else
			{
				List<string> parts = new List<string>();
				if (s.Paused)
				{
					parts.Add(L.T("пауза — ждёт окно Роблокса"));
				}
				else if (s.NextActionAt.HasValue)
				{
					parts.Add(string.Format(L.T("следующий через {0:0.0} с"), Math.Max(0.0, (s.NextActionAt.Value - DateTime.Now).TotalSeconds)));
				}
				else
				{
					parts.Add(L.T("работает"));
				}
				parts.Add(string.Format(L.T("циклов {0}"), s.Cycles));
				parts.Add(string.Format(L.T("нажатий {0}"), s.Actions));
				if (s.StartedAt.HasValue)
				{
					parts.Add(Duration(DateTime.Now - s.StartedAt.Value));
				}
				c.Status.Text = string.Join(" · ", parts);
			}
			M.totals.TryGetValue(c.Id, out var t);
			long actions = (t?.actions ?? 0) + System.Threading.Interlocked.Read(ref c.PendingActions);
			c.Totals.Text = actions == 0 ? "" : string.Format(L.T("Всего: {0} нажатий за {1} запусков, {2}"), actions, t?.runs ?? 0, Duration(TimeSpan.FromMilliseconds(t?.runtimeMs ?? 0)));
		}
	}

	private static string Duration(TimeSpan d)
	{
		if (d.TotalHours >= 1.0)
		{
			return string.Format(L.T("{0} ч {1:00} мин"), (int)d.TotalHours, d.Minutes);
		}
		return d.TotalMinutes >= 1.0 ? string.Format(L.T("{0} мин {1:00} с"), (int)d.TotalMinutes, d.Seconds) : string.Format(L.T("{0} с"), d.Seconds);
	}

	// ---------- small controls ----------
	private Style Res(string key)
	{
		return (Style)host.FindResource(key);
	}

	private TextBlock Hint(string text)
	{
		return new TextBlock
		{
			Text = text,
			Style = Res("Hint"),
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0.0, 3.0, 0.0, 0.0)
		};
	}

	private FrameworkElement Labeled(string label, FrameworkElement control)
	{
		StackPanel p = new StackPanel
		{
			Margin = new Thickness(0.0, 0.0, 14.0, 10.0)
		};
		p.Children.Add(new TextBlock
		{
			Text = label,
			Style = Res("Hint"),
			FontSize = 11.5,
			Margin = new Thickness(2.0, 0.0, 0.0, 5.0)
		});
		control.HorizontalAlignment = HorizontalAlignment.Left;
		p.Children.Add(control);
		return p;
	}

	private CheckBox Switch(bool value, Action<bool> changed)
	{
		CheckBox box = new CheckBox
		{
			Style = Res("Switch"),
			IsChecked = value,
			Margin = new Thickness(0.0, 8.0, 0.0, 0.0)
		};
		box.Checked += delegate
		{
			changed(obj: true);
		};
		box.Unchecked += delegate
		{
			changed(obj: false);
		};
		return box;
	}

	private TextBox Number(double value, double min, double max, Action<double> changed)
	{
		double current = value;
		TextBox box = new TextBox
		{
			Style = Res("Field"),
			Height = 38.0,
			Padding = new Thickness(10.0, 0.0, 10.0, 0.0),
			Width = 96.0,
			TextAlignment = TextAlignment.Right,
			Text = ((int)value).ToString(CultureInfo.InvariantCulture)
		};
		Action commit = delegate
		{
			if (!double.TryParse((box.Text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var r))
			{
				box.Text = ((int)current).ToString(CultureInfo.InvariantCulture);
				return;
			}
			r = Math.Max(min, Math.Min(max, Math.Round(r)));
			box.Text = ((int)r).ToString(CultureInfo.InvariantCulture);
			if (r != current)
			{
				current = r;
				changed(r);
			}
		};
		box.LostKeyboardFocus += delegate
		{
			commit();
		};
		box.KeyDown += delegate(object s, KeyEventArgs e)
		{
			if (e.Key == Key.Return)
			{
				commit();
				Keyboard.ClearFocus();
			}
		};
		box.PreviewTextInput += delegate(object s, TextCompositionEventArgs e)
		{
			e.Handled = !Regex.IsMatch(e.Text, "^\\d$");
		};
		return box;
	}

	// Interval in seconds (0.1 … 3600), stored as milliseconds.
	private TextBox Seconds(int ms, Action<int> changed)
	{
		double current = ms / 1000.0;
		Func<double, string> fmt = (double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
		TextBox box = new TextBox
		{
			Style = Res("Field"),
			Height = 38.0,
			Padding = new Thickness(10.0, 0.0, 10.0, 0.0),
			Width = 96.0,
			TextAlignment = TextAlignment.Right,
			Text = fmt(current)
		};
		Action commit = delegate
		{
			string s = (box.Text ?? "").Trim().Replace(',', '.');
			if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var r))
			{
				box.Text = fmt(current);
				return;
			}
			r = Math.Max(0.1, Math.Min(3600.0, r));
			box.Text = fmt(r);
			if (Math.Abs(r - current) > 0.0001)
			{
				current = r;
				changed((int)Math.Round(r * 1000.0));
			}
		};
		box.LostKeyboardFocus += delegate
		{
			commit();
		};
		box.KeyDown += delegate(object s, KeyEventArgs e)
		{
			if (e.Key == Key.Return)
			{
				commit();
				Keyboard.ClearFocus();
			}
		};
		box.PreviewTextInput += delegate(object s, TextCompositionEventArgs e)
		{
			e.Handled = !Regex.IsMatch(e.Text, "^[\\d.,]$");
		};
		return box;
	}

	// Hotbar slot: one key ("1".."7", a letter, F-key…).
	private TextBox SlotBox(string value, Action<string> changed)
	{
		string current = value;
		TextBox box = new TextBox
		{
			Style = Res("Field"),
			Height = 38.0,
			Padding = new Thickness(10.0, 0.0, 10.0, 0.0),
			Width = 70.0,
			TextAlignment = TextAlignment.Center,
			Text = value,
			MaxLength = 9
		};
		Action commit = delegate
		{
			string v = (box.Text ?? "").Trim();
			if (MacroInput.ToVk(v) == 0)
			{
				box.Text = current;
				return;
			}
			v = v.ToUpperInvariant();
			box.Text = v;
			if (v != current)
			{
				current = v;
				changed(v);
			}
		};
		box.LostKeyboardFocus += delegate
		{
			commit();
		};
		box.KeyDown += delegate(object s, KeyEventArgs e)
		{
			if (e.Key == Key.Return)
			{
				commit();
				Keyboard.ClearFocus();
			}
		};
		return box;
	}

	// Hotkey field: click it and press the key (with Ctrl/Alt/Shift); Backspace/Delete clears it.
	private TextBox HotkeyBox(string value, Action<string> changed)
	{
		TextBox box = new TextBox
		{
			Style = Res("Field"),
			Height = 38.0,
			Padding = new Thickness(10.0, 0.0, 10.0, 0.0),
			Width = 130.0,
			TextAlignment = TextAlignment.Center,
			Text = value ?? "",
			IsReadOnly = true,
			Cursor = Cursors.Hand,
			Tag = L.T("нажми клавишу")
		};
		box.PreviewKeyDown += delegate(object s, KeyEventArgs e)
		{
			e.Handled = true;
			Key key = e.Key == Key.System ? e.SystemKey : e.Key;
			if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt || key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin)
			{
				return;
			}
			string text;
			if (key == Key.Back || key == Key.Delete || key == Key.Escape)
			{
				text = "";
			}
			else
			{
				string name = KeyName(key);
				if (name == null)
				{
					return;
				}
				List<string> parts = new List<string>();
				if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
				{
					parts.Add("Ctrl");
				}
				if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)
				{
					parts.Add("Alt");
				}
				if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
				{
					parts.Add("Shift");
				}
				parts.Add(name);
				text = string.Join("+", parts);
			}
			box.Text = text;
			changed(text);
			Keyboard.ClearFocus();
		};
		return box;
	}

	private static string KeyName(Key key)
	{
		if (key >= Key.F1 && key <= Key.F24)
		{
			return "F" + (key - Key.F1 + 1);
		}
		if (key >= Key.D0 && key <= Key.D9)
		{
			return ((char)('0' + (key - Key.D0))).ToString();
		}
		if (key >= Key.A && key <= Key.Z)
		{
			return ((char)('A' + (key - Key.A))).ToString();
		}
		if (key >= Key.NumPad0 && key <= Key.NumPad9)
		{
			return "Num" + (key - Key.NumPad0);
		}
		return null;
	}
}
