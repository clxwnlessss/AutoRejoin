using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AutoRejoinApp;

// Keeps the last few small frames of every visible in-game window (one every 2.5 s, 4 per account), so when an account
// drops there is a "storyboard" of the seconds before it: the frames + one taken at the moment of the drop.
// Minimized windows are not captured in the background (that would flash them); for them only the drop frame is taken.
public class DropRecorder
{
	private class Frame
	{
		public DateTime At;
		public byte[] Jpeg;
	}

	private readonly ConcurrentDictionary<string, List<Frame>> frames = new ConcurrentDictionary<string, List<Frame>>();

	private volatile KeyValuePair<string, int>[] watched = new KeyValuePair<string, int>[0];

	private readonly Timer timer;

	private int busy;

	public volatile bool Enabled = true;

	public DropRecorder()
	{
		timer = new Timer(delegate
		{
			Tick();
		}, null, 3000, 2500);
	}

	// From the UI thread on every engine status: which accounts are in game and their window pid.
	public void Watch(IEnumerable<KeyValuePair<string, int>> inGameWindows)
	{
		watched = inGameWindows.ToArray();
	}

	private void Tick()
	{
		if (!Enabled || Interlocked.Exchange(ref busy, 1) == 1)
		{
			return;
		}
		try
		{
			KeyValuePair<string, int>[] list = watched;
			HashSet<string> keys = new HashSet<string>(list.Select((KeyValuePair<string, int> w) => w.Key));
			foreach (string stale in frames.Keys.Where((string k) => !keys.Contains(k)).ToList())
			{
				// keep frames of a window that just died for a bit — the drop handler reads them
				if (frames.TryGetValue(stale, out var old) && (old.Count == 0 || DateTime.UtcNow - old[old.Count - 1].At > TimeSpan.FromSeconds(30.0)))
				{
					frames.TryRemove(stale, out _);
				}
			}
			foreach (KeyValuePair<string, int> w in list)
			{
				using Bitmap shot = WindowShot.Capture(w.Value, 480, restoreMinimized: false, out _);
				if (shot == null)
				{
					continue;
				}
				List<Frame> buf = frames.GetOrAdd(w.Key, (string _) => new List<Frame>());
				lock (buf)
				{
					buf.Add(new Frame { At = DateTime.UtcNow, Jpeg = WindowShot.Jpeg(shot, 70) });
					while (buf.Count > 4)
					{
						buf.RemoveAt(0);
					}
				}
			}
		}
		catch (Exception ex)
		{
			App.LogError(L.T("раскадровка"), ex);
		}
		finally
		{
			Interlocked.Exchange(ref busy, 0);
		}
	}

	// The storyboard for a drop: buffered frames + a frame now (pid may be gone already). null if there is nothing.
	public Task<byte[]> Storyboard(string key, int? pid, string accountName)
	{
		if (!Enabled)
		{
			return Task.FromResult<byte[]>(null);
		}
		return Task.Run(delegate
		{
			List<KeyValuePair<string, Bitmap>> list = new List<KeyValuePair<string, Bitmap>>();
			try
			{
				DateTime now = DateTime.UtcNow;
				if (frames.TryGetValue(key, out var buf))
				{
					List<Frame> copy;
					lock (buf)
					{
						copy = buf.Where((Frame f) => now - f.At < TimeSpan.FromSeconds(40.0)).ToList();
					}
					foreach (Frame f in copy)
					{
						list.Add(new KeyValuePair<string, Bitmap>("−" + Math.Max(1, (int)(now - f.At).TotalSeconds) + L.T(" сек"), WindowShot.FromJpeg(f.Jpeg)));
					}
				}
				if (pid.HasValue)
				{
					Bitmap live = WindowShot.Capture(pid.Value, 480, restoreMinimized: true, out _);
					if (live != null)
					{
						list.Add(new KeyValuePair<string, Bitmap>(L.T("в момент вылета"), live));
					}
				}
				if (list.Count == 0)
				{
					return null;
				}
				return WindowShot.Storyboard(list, L.T("Раскадровка вылета · ") + accountName + " · " + DateTime.Now.ToString("HH:mm:ss"));
			}
			catch (Exception ex)
			{
				App.LogError(L.T("раскадровка"), ex);
				return null;
			}
			finally
			{
				foreach (KeyValuePair<string, Bitmap> f in list)
				{
					f.Value.Dispose();
				}
			}
		});
	}
}
