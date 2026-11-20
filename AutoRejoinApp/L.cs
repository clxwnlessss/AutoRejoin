using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace AutoRejoinApp;

// Языки интерфейса: русский (исходный текст в коде) и английский (словарь lang-en.tsv: «русский<TAB>english»).
// L.T("русский") возвращает перевод, если выбран английский, иначе сам текст.
public static class L
{
	private static readonly Dictionary<string, string> En = new Dictionary<string, string>(StringComparer.Ordinal);

	// Тексты движка с числами и причинами внутри: «перезайдёт через 3 сек» и т. п.
	private static readonly List<KeyValuePair<Regex, string>> EnginePatterns = new List<KeyValuePair<Regex, string>>();

	public static string Lang { get; private set; } = "ru";

	public static bool IsEnglish => Lang == "en";

	// "" — по языку Windows: русский для ru/uk/be, иначе английский.
	public static void Init(string configured)
	{
		string lang = (configured ?? "").Trim().ToLowerInvariant();
		if (lang != "ru" && lang != "en")
		{
			string ui = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
			lang = (ui == "ru" || ui == "uk" || ui == "be") ? "ru" : "en";
		}
		Lang = lang;
		if (IsEnglish && En.Count == 0)
		{
			Load();
		}
	}

	private static void Load()
	{
		using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AutoRejoinApp.lang-en.tsv");
		if (stream == null)
		{
			return;
		}
		using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
		string line;
		while ((line = reader.ReadLine()) != null)
		{
			if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
			{
				continue;
			}
			int tab = line.IndexOf('\t');
			if (tab <= 0)
			{
				continue;
			}
			string ru = Unescape(line.Substring(0, tab));
			string en = Unescape(line.Substring(tab + 1));
			if (ru.StartsWith("~", StringComparison.Ordinal))
			{
				// ~регулярное выражение → шаблон с $1, $2
				EnginePatterns.Add(new KeyValuePair<Regex, string>(new Regex(ru.Substring(1), RegexOptions.CultureInvariant), en));
			}
			else
			{
				En[ru] = en;
			}
		}
	}

	private static string Unescape(string s)
	{
		return s.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\s", " ");
	}

	public static string T(string ru)
	{
		if (!IsEnglish || ru == null)
		{
			return ru;
		}
		if (En.TryGetValue(ru, out var en))
		{
			return en;
		}
		return ru;
	}

	// Текст, пришедший от движка (состояние окна, причина перезахода): словарь, потом шаблоны.
	public static string Engine(string ru)
	{
		if (!IsEnglish || string.IsNullOrEmpty(ru))
		{
			return ru;
		}
		if (En.TryGetValue(ru, out var en))
		{
			return en;
		}
		foreach (KeyValuePair<Regex, string> pattern in EnginePatterns)
		{
			Match m = pattern.Key.Match(ru);
			if (m.Success)
			{
				string result = pattern.Value;
				for (int i = m.Groups.Count - 1; i >= 1; i--)
				{
					result = result.Replace("$" + i, Engine(m.Groups[i].Value));
				}
				return result;
			}
		}
		return ru;
	}

	// Перевод готовой разметки XAML: тексты, кнопки, подсказки, плейсхолдеры, заголовок окна.
	public static void Apply(object root)
	{
		if (!IsEnglish || root == null)
		{
			return;
		}
		Walk(root, 0);
	}

	// Для элементов из шаблонов (карточки аккаунтов): обходит визуальное дерево.
	public static void ApplyVisual(DependencyObject root)
	{
		if (!IsEnglish || root == null)
		{
			return;
		}
		WalkVisual(root, 0);
	}

	private static void WalkVisual(DependencyObject node, int depth)
	{
		if (depth > 60)
		{
			return;
		}
		if (node is TextBlock textBlock && textBlock.GetBindingExpression(TextBlock.TextProperty) == null && !string.IsNullOrEmpty(textBlock.Text))
		{
			textBlock.Text = T(textBlock.Text);
		}
		if (node is FrameworkElement fe && fe.ToolTip is string tip)
		{
			fe.ToolTip = T(tip);
		}
		int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);
		for (int i = 0; i < count; i++)
		{
			WalkVisual(System.Windows.Media.VisualTreeHelper.GetChild(node, i), depth + 1);
		}
	}

	private static void Walk(object node, int depth)
	{
		if (node == null || depth > 200)
		{
			return;
		}
		switch (node)
		{
		case Window window:
			window.Title = T(window.Title);
			break;
		case TextBlock textBlock:
			if (textBlock.Inlines.Count <= 1 && !(textBlock.Inlines.FirstInline is Run run0 && run0.Text != textBlock.Text))
			{
				if (!string.IsNullOrEmpty(textBlock.Text))
				{
					textBlock.Text = T(textBlock.Text);
				}
			}
			else
			{
				// Копия списка: смена Run.Text меняет коллекцию Inlines во время перебора.
				foreach (Inline inline in new List<Inline>(textBlock.Inlines))
				{
					if (inline is Run run)
					{
						string translated = T(run.Text);
						if (translated != run.Text)
						{
							run.Text = translated;
						}
					}
				}
				return;
			}
			break;
		case TextBox textBox:
			if (textBox.Tag is string tag)
			{
				textBox.Tag = T(tag);
			}
			break;
		case ContentControl contentControl:
			if (contentControl.Content is string content)
			{
				contentControl.Content = T(content);
			}
			break;
		}
		if (node is FrameworkElement fe && fe.ToolTip is string tip)
		{
			fe.ToolTip = T(tip);
		}
		if (node is DependencyObject dependency)
		{
			List<object> children = new List<object>();
			foreach (object child in LogicalTreeHelper.GetChildren(dependency))
			{
				children.Add(child);
			}
			foreach (object child in children)
			{
				Walk(child, depth + 1);
			}
		}
	}
}
