using System;
using System.Globalization;
using System.IO;
using System.Threading;

namespace AutoRejoinApp;

// Очистка старых логов раз в N часов (по умолчанию 24):
//  • логи Роблокса (%LOCALAPPDATA%\Roblox\logs) старше срока — каждое окно оставляет там по несколько файлов,
//    за неделю их набираются тысячи. Логи открытых окон не трогаются: они свежие, и Роблокс держит их открытыми;
//  • rejoin.log → rejoin.old.log (прошлый old удаляется) — журнал хранит последние 1–2 суток;
//  • app-error.log — удаляется, если старый, и откладывается в app-error.old.log, если разросся.
// Когда чистили в последний раз — в файле logcleanup.stamp, чтобы перезапуск программы не сбивал отсчёт.
public static class LogCleaner
{
	public class Result
	{
		public int Files;
		public long Bytes;

		public string Text
		{
			get
			{
				if (Files != 0)
				{
					return "удалено файлов: " + Files + " (" + ((double)Bytes / 1048576.0).ToString("0.#", CultureInfo.InvariantCulture) + " МБ)";
				}
				return "удалять нечего";
			}
		}
	}

	private static readonly object Gate = new object();

	// Для проверки на тестовых файлах; в обычной работе — пусто.
	public static string RobloxLogDirOverride;

	public static string RobloxLogDir => RobloxLogDirOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "logs");

	private static string StampFile => Path.Combine(AppPaths.DataDir, "logcleanup.stamp");

	public static DateTime? LastRunUtc
	{
		get
		{
			try
			{
				return File.Exists(StampFile) ? new DateTime?(File.GetLastWriteTimeUtc(StampFile)) : null;
			}
			catch
			{
				return null;
			}
		}
	}

	public static bool IsDue(int hours)
	{
		DateTime? last = LastRunUtc;
		if (last.HasValue)
		{
			return (DateTime.UtcNow - last.Value).TotalHours >= (double)hours;
		}
		return true;
	}

	public static Result Run(int hours)
	{
		lock (Gate)
		{
			Result result = new Result();
			DateTime cutoff = DateTime.UtcNow.AddHours(-Math.Max(1, hours));
			// Логи Роблокса.
			try
			{
				if (Directory.Exists(RobloxLogDir))
				{
					foreach (string file in Directory.EnumerateFiles(RobloxLogDir, "*", SearchOption.TopDirectoryOnly))
					{
						TryDelete(file, cutoff, result);
					}
				}
			}
			catch
			{
			}
			// Свой журнал: текущий откладываем, прошлый отложенный удаляем.
			string log = AppPaths.LogFile;
			string oldLog = Path.Combine(AppPaths.DataDir, "rejoin.old.log");
			TryDelete(oldLog, DateTime.MaxValue, result);
			TryMove(log, oldLog);
			// Ошибки окна программы.
			string errors = AppPaths.ErrorLog;
			string oldErrors = Path.Combine(AppPaths.DataDir, "app-error.old.log");
			TryDelete(oldErrors, cutoff, result);
			if (!TryDelete(errors, cutoff, result))
			{
				try
				{
					if (File.Exists(errors) && new FileInfo(errors).Length > 1048576)
					{
						TryDelete(oldErrors, DateTime.MaxValue, result);
						TryMove(errors, oldErrors);
					}
				}
				catch
				{
				}
			}
			try
			{
				File.WriteAllText(StampFile, DateTime.UtcNow.ToString("o"));
				File.SetLastWriteTimeUtc(StampFile, DateTime.UtcNow);
			}
			catch
			{
			}
			return result;
		}
	}

	// Удаляет файл, если он не менялся с cutoff. Занятый файл (открытое окно Роблокса) пропускается.
	private static bool TryDelete(string path, DateTime cutoffUtc, Result result)
	{
		try
		{
			FileInfo info = new FileInfo(path);
			if (!info.Exists || info.LastWriteTimeUtc >= cutoffUtc)
			{
				return false;
			}
			long size = info.Length;
			info.Delete();
			result.Files++;
			result.Bytes += size;
			return true;
		}
		catch
		{
			return false;
		}
	}

	// Журнал пишут и движок, и программа (каждый раз открывая файл ненадолго) — пробуем несколько раз.
	private static void TryMove(string from, string to)
	{
		for (int i = 0; i < 10; i++)
		{
			try
			{
				if (!File.Exists(from))
				{
					return;
				}
				File.Move(from, to);
				return;
			}
			catch (IOException)
			{
				Thread.Sleep(100);
			}
			catch
			{
				return;
			}
		}
	}
}
