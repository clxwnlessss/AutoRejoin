using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace AutoRejoinApp;

public class EngineHost
{
	private readonly Dispatcher dispatcher;

	private Process process;

	private volatile bool stopping;

	public bool IsRunning
	{
		get
		{
			try
			{
				return process != null && !process.HasExited;
			}
			catch
			{
				return false;
			}
		}
	}

	public int ProcessId
	{
		get
		{
			try
			{
				return IsRunning ? process.Id : 0;
			}
			catch
			{
				return 0;
			}
		}
	}

	public event Action<IDictionary<string, object>> Message;

	public event Action<string> ErrorLine;

	public event Action<int, bool> Exited;

	public EngineHost(Dispatcher dispatcher)
	{
		this.dispatcher = dispatcher;
	}

	private static string PowerShellPath()
	{
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell\\v1.0\\powershell.exe");
		if (!File.Exists(text))
		{
			return "powershell.exe";
		}
		return text;
	}

	public static ProcessStartInfo CreateStartInfo(string arguments)
	{
		ProcessStartInfo processStartInfo = new ProcessStartInfo(PowerShellPath(), "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + AppPaths.EngineScript + "\" -DataDir \"" + AppPaths.DataDir + "\" " + arguments);
		processStartInfo.UseShellExecute = false;
		processStartInfo.CreateNoWindow = true;
		processStartInfo.WindowStyle = ProcessWindowStyle.Hidden;
		processStartInfo.RedirectStandardInput = true;
		processStartInfo.RedirectStandardOutput = true;
		processStartInfo.RedirectStandardError = true;
		processStartInfo.StandardOutputEncoding = Encoding.UTF8;
		processStartInfo.StandardErrorEncoding = Encoding.UTF8;
		processStartInfo.WorkingDirectory = AppPaths.AppDir;
		return processStartInfo;
	}

	public void Start(int startupDelay)
	{
		if (IsRunning)
		{
			return;
		}
		if (!File.Exists(AppPaths.EngineScript))
		{
			throw new FileNotFoundException("Рядом с AutoRejoin.exe нет engine.ps1 — распакуй папку программы целиком.", AppPaths.EngineScript);
		}
		SystemTools.KillOrphanEngine();
		Process started = new Process();
		started.StartInfo = CreateStartInfo((startupDelay > 0) ? ("-StartupDelay " + startupDelay) : "");
		started.EnableRaisingEvents = true;
		started.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
		{
			if (e.Data != null)
			{
				OnLine(e.Data);
			}
		};
		started.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
		{
			if (!string.IsNullOrWhiteSpace(e.Data))
			{
				dispatcher.BeginInvoke((Action)delegate
				{
					Raise(this.ErrorLine, e.Data);
				});
			}
		};
		started.Exited += delegate
		{
			int code = -1;
			try
			{
				code = started.ExitCode;
			}
			catch
			{
			}
			bool byUs = stopping;
			try
			{
				Task.Run(delegate
				{
					try
					{
						started.WaitForExit();
					}
					catch
					{
					}
				}).Wait(3000);
			}
			catch
			{
			}
			dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)delegate
			{
				if (process == started)
				{
					process = null;
				}
				this.Exited?.Invoke(code, byUs);
			});
		};
		stopping = false;
		started.Start();
		started.BeginOutputReadLine();
		started.BeginErrorReadLine();
		process = started;
	}

	private void OnLine(string line)
	{
		if (line.Length > 0 && line[0] == '{')
		{
			IDictionary<string, object> data = null;
			try
			{
				data = Store.Json().DeserializeObject(line) as IDictionary<string, object>;
			}
			catch
			{
			}
			if (data != null)
			{
				dispatcher.BeginInvoke((Action)delegate
				{
					this.Message?.Invoke(data);
				});
				return;
			}
		}
		if (!string.IsNullOrWhiteSpace(line))
		{
			dispatcher.BeginInvoke((Action)delegate
			{
				Raise(this.ErrorLine, line);
			});
		}
	}

	private static void Raise(Action<string> handler, string text)
	{
		handler?.Invoke(text);
	}

	public void Send(string command)
	{
		Process process = this.process;
		if (process == null)
		{
			return;
		}
		try
		{
			if (!process.HasExited)
			{
				process.StandardInput.WriteLine(command);
				process.StandardInput.Flush();
			}
		}
		catch
		{
		}
	}

	public Task StopAsync()
	{
		Process current = process;
		if (current == null)
		{
			return Task.FromResult(0);
		}
		stopping = true;
		Send("stop");
		return Task.Run(delegate
		{
			try
			{
				if (!current.WaitForExit(8000))
				{
					current.Kill();
				}
				current.WaitForExit(3000);
			}
			catch
			{
			}
		});
	}

	public void Kill()
	{
		Process process = this.process;
		if (process == null)
		{
			return;
		}
		stopping = true;
		try
		{
			if (!process.HasExited)
			{
				process.Kill();
			}
		}
		catch
		{
		}
	}
}
