using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AutoRejoinApp;

// Popular Roblox games AutoRejoin can join (public servers). Place and universe IDs were checked against the official
// Roblox API (apis.roblox.com/universes, games.roblox.com/v1/games). Names, online count, icons and preview pictures
// are loaded live from the public Roblox APIs (no login needed).
public static class GameCatalog
{
	public class Game
	{
		public string Name;

		public string PlaceId;

		public long UniverseId;

		public string Genre;

		public string LiveName;

		public long Playing = -1;

		public string IconUrl;

		public string PreviewUrl;

		public bool Custom;
	}

	public const string BeeSwarm = "1537690962";

	public static readonly List<Game> Games = new List<Game>
	{
		G("Bee Swarm Simulator", "1537690962", 601130232L, "Симулятор"),
		G("Blox Fruits", "2753915549", 994732206L, "Аниме · RPG"),
		G("Anime Adventures", "8304191830", 3183403065L, "Аниме · TD"),
		G("Anime Vanguards", "16146832113", 5578556129L, "Аниме · TD"),
		G("Pet Simulator 99", "8737899170", 3317771874L, "Симулятор"),
		G("Grow a Garden", "126884695634066", 7436755782L, "Симулятор"),
		G("Steal a Brainrot", "109983668079237", 7709344486L, "Казуальная"),
		G("99 Nights in the Forest", "79546208627805", 7326934954L, "Выживание"),
		G("Fisch", "16732694052", 5750914919L, "Симулятор"),
		G("King Legacy", "4520749081", 1451439645L, "Аниме · RPG"),
		G("Adopt Me!", "920587237", 383310974L, "Ролевая"),
		G("Brookhaven RP", "4924922222", 1686885941L, "Ролевая"),
		G("Murder Mystery 2", "142823291", 66654135L, "Мини-игры"),
		G("Rivals", "17625359962", 6035872082L, "Шутер"),
		G("Sol's RNG", "15532962292", 5361032378L, "RNG"),
		G("Toilet Tower Defense", "13775256536", 4778845442L, "TD"),
		G("Tower Defense Simulator", "3260590327", 1176784616L, "TD"),
		G("The Strongest Battlegrounds", "10449761463", 3808081382L, "Файтинг"),
		G("Blade Ball", "13772394625", 4777817887L, "Мини-игры"),
		G("Blue Lock: Rivals", "18668065416", 6325068386L, "Спорт"),
		G("Dead Rails", "116495829188952", 7018190066L, "Выживание"),
		G("BedWars", "6872265039", 2619619496L, "PvP"),
		G("Bubble Gum Simulator Infinity", "85896571713843", 6504986360L, "Симулятор"),
		G("Doors", "6516141723", 2440500124L, "Хоррор"),
		G("Jailbreak", "606849621", 245662005L, "Ролевая"),
		G("Grand Piece Online", "1730877806", 648454481L, "Аниме · RPG"),
		G("Da Hood", "2788229376", 1008451066L, "PvP"),
		G("Arsenal", "286090429", 111958650L, "Шутер"),
		G("Arm Wrestle Simulator", "13127800756", 4582358979L, "Симулятор"),
		G("Shindo Life", "4616652839", 1511883870L, "Аниме · RPG"),
		G("Pet Simulator X", "6284583030", 2316994223L, "Симулятор"),
		G("Anime Fighters Simulator", "6299805723", 2324662457L, "Аниме")
	};

	private static Game G(string name, string place, long universe, string genre)
	{
		return new Game
		{
			Name = name,
			PlaceId = place,
			UniverseId = universe,
			Genre = genre
		};
	}

	public static Game Find(string placeId)
	{
		return Games.FirstOrDefault((Game g) => g.PlaceId == placeId);
	}

	/// Live names, online counts, icons and preview pictures for the given games (batches of up to 50).
	public static async Task LoadLiveAsync(IList<Game> games)
	{
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		foreach (Game[] chunk in games.Where((Game g) => g.UniverseId > 0).Select((Game g, int i) => new { g, i }).GroupBy(x => x.i / 30, x => x.g).Select(x => x.ToArray()))
		{
			string ids = string.Join(",", chunk.Select((Game g) => g.UniverseId));
			try
			{
				object data = Store.Json().DeserializeObject(await Get("https://games.roblox.com/v1/games?universeIds=" + ids));
				foreach (IDictionary<string, object> d in Items(data))
				{
					Game g2 = chunk.FirstOrDefault((Game x) => x.UniverseId == (long)(Store.Num(d, "id") ?? 0.0));
					if (g2 != null)
					{
						g2.LiveName = Store.Str(d, "name");
						g2.Playing = (long)(Store.Num(d, "playing") ?? -1.0);
					}
				}
			}
			catch
			{
			}
			try
			{
				object icons = Store.Json().DeserializeObject(await Get("https://thumbnails.roblox.com/v1/games/icons?universeIds=" + ids + "&returnPolicy=PlaceHolder&size=256x256&format=Png&isCircular=false"));
				foreach (IDictionary<string, object> d in Items(icons))
				{
					Game g3 = chunk.FirstOrDefault((Game x) => x.UniverseId == (long)(Store.Num(d, "targetId") ?? 0.0));
					if (g3 != null && Store.Str(d, "state") == "Completed")
					{
						g3.IconUrl = Store.Str(d, "imageUrl");
					}
				}
			}
			catch
			{
			}
		}
	}

	/// Wide preview picture (the game's first thumbnail, 768x432).
	public static async Task LoadPreviewAsync(Game g)
	{
		if (g.PreviewUrl != null || g.UniverseId <= 0)
		{
			return;
		}
		try
		{
			object data = Store.Json().DeserializeObject(await Get("https://thumbnails.roblox.com/v1/games/multiget/thumbnails?universeIds=" + g.UniverseId + "&countPerUniverse=1&defaults=true&size=768x432&format=Png&isCircular=false"));
			foreach (IDictionary<string, object> d in Items(data))
			{
				object thumbs = d.ContainsKey("thumbnails") ? d["thumbnails"] : null;
				IDictionary<string, object> first = (thumbs as object[] ?? (thumbs as System.Collections.ArrayList)?.ToArray() ?? new object[0]).OfType<IDictionary<string, object>>().FirstOrDefault();
				if (first != null && Store.Str(first, "state") == "Completed")
				{
					g.PreviewUrl = Store.Str(first, "imageUrl");
				}
			}
		}
		catch
		{
		}
	}

	/// Any game by link or place ID ("https://www.roblox.com/games/2753915549/..." or "2753915549").
	public static async Task<Game> ResolveAsync(string text)
	{
		Match m = Regex.Match(text ?? "", "(?:games/|placeId=|^\\s*)(\\d{4,20})");
		if (!m.Success)
		{
			throw new Exception(L.T("Вставь ссылку на игру Роблокса или ID плейса (только цифры)."));
		}
		string place = m.Groups[1].Value;
		Game known = Find(place);
		if (known != null)
		{
			return known;
		}
		ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
		long universe;
		try
		{
			universe = (long)(Store.Num(Store.Json().DeserializeObject(await Get("https://apis.roblox.com/universes/v1/places/" + place + "/universe")) as IDictionary<string, object>, "universeId") ?? 0.0);
		}
		catch
		{
			universe = 0L;
		}
		if (universe <= 0)
		{
			throw new Exception(L.T("Роблокс не знает такую игру — проверь ссылку."));
		}
		Game g = new Game
		{
			Name = place,
			PlaceId = place,
			UniverseId = universe,
			Genre = L.T("Своя игра"),
			Custom = true
		};
		await LoadLiveAsync(new List<Game> { g });
		if (!string.IsNullOrEmpty(g.LiveName))
		{
			g.Name = g.LiveName;
		}
		return g;
	}

	public static string Online(long playing)
	{
		if (playing < 0)
		{
			return "";
		}
		if (playing >= 1000000)
		{
			return (playing / 1000000.0).ToString("0.#") + L.T(" млн онлайн");
		}
		if (playing >= 1000)
		{
			return (playing / 1000.0).ToString("0.#") + L.T(" тыс. онлайн");
		}
		return playing + L.T(" онлайн");
	}

	private static IEnumerable<IDictionary<string, object>> Items(object json)
	{
		object list = (json as IDictionary<string, object>)?["data"];
		return (list as object[] ?? (list as System.Collections.ArrayList)?.ToArray() ?? new object[0]).OfType<IDictionary<string, object>>();
	}

	private static async Task<string> Get(string url)
	{
		HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
		req.UserAgent = "AutoRejoin/" + Updater.CurrentText;
		req.Accept = "application/json";
		Task<WebResponse> task = req.GetResponseAsync();
		if (await Task.WhenAny(task, Task.Delay(15000)) != task)
		{
			req.Abort();
			throw new TimeoutException();
		}
		using WebResponse resp = await task;
		using StreamReader reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8);
		return await reader.ReadToEndAsync();
	}
}
