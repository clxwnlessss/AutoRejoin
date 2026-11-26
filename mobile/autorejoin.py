#!/usr/bin/env python3
# AutoRejoin Mobile — keeps Roblox accounts in game on a rooted Android (UGPhone, cloud phones, emulators).
# Runs in Termux. Every Roblox app (com.roblox.client and its clones) is one account that is already logged in:
# no cookies needed. For each app it watches the process and the Roblox log (the same log format as on PC) and,
# when the app is closed, frozen out of the game, kicked or disconnected, force-stops it and opens the game
# (public server or your VIP) again through a roblox:// link.
#
#   python autorejoin.py setup   — finds the Roblox apps and writes config.json
#   python autorejoin.py         — runs (Ctrl+C to stop)
import json
import os
import re
import shlex
import subprocess
import sys
import time
import urllib.parse
import urllib.request

VERSION = "1.0"
HERE = os.path.dirname(os.path.abspath(__file__))
CONFIG = os.path.join(HERE, "config.json")

JOIN = re.compile(r"! Joining game '[^']+' place (\d+)")
KICK = re.compile(r"setting replicator disconnect reason to (\d+)")
DISCONNECT = re.compile(r"Disconnected from server|Sending disconnect with reason|Lost connection|Connection lost|"
                        r"ID_CONNECTION_LOST|ID_DISCONNECTION_NOTIFICATION|leaveUGCGameInternal")

DEFAULTS = {
    "accounts": [],          # [{"package": "com.roblox.client", "name": "main", "link": "1537690962"}]
    "link": "1537690962",    # used by accounts without their own link: place ID, game link or VIP link
    "checkEvery": 5,         # seconds between checks
    "joinTimeout": 120,      # app started but not in game after this many seconds -> relaunch
    "outGrace": 45,          # out of game (menu, disconnect, teleport) longer than this -> relaunch
    "launchGap": 8,          # seconds between launches of different apps
    "webhook": "",           # optional Discord webhook for drop / back-in-game alerts
}

C = {"gray": "\033[90m", "green": "\033[92m", "yellow": "\033[93m", "red": "\033[91m", "cyan": "\033[96m", "end": "\033[0m"}


def say(text, color="", name=None):
    stamp = time.strftime("%H:%M:%S")
    who = f"[{name}] " if name else ""
    print(f"{C['gray']}{stamp}{C['end']} {C.get(color, '')}{who}{text}{C['end']}", flush=True)


def su(cmd, timeout=20):
    """Runs a shell command as root and returns its stdout ('' on error)."""
    try:
        out = subprocess.run(["su", "-c", cmd], capture_output=True, timeout=timeout)
        return out.stdout.decode("utf-8", "replace")
    except Exception:
        return ""


def find_roblox_packages():
    out = su("pm list packages")
    pkgs = sorted({line.split(":", 1)[1].strip() for line in out.splitlines() if line.startswith("package:")})
    return [p for p in pkgs if "roblox" in p.lower()]


def deep_link(link):
    """Place ID, game link, VIP link (privateServerLinkCode) or share link -> roblox:// link for the app."""
    link = (link or "").strip()
    if re.fullmatch(r"\d{4,20}", link):
        return f"roblox://experiences/start?placeId={link}"
    url = urllib.parse.urlparse(link)
    query = urllib.parse.parse_qs(url.query)
    if "/share" in url.path and query.get("code"):
        kind = (query.get("type") or ["Server"])[0]
        return f"roblox://navigation/share_links?code={urllib.parse.quote(query['code'][0])}&type={kind}"
    m = re.search(r"/games/(\d+)", url.path)
    place = m.group(1) if m else (query.get("placeId") or [None])[0]
    if not place:
        raise ValueError(f"don't understand the link: {link}")
    code = (query.get("privateServerLinkCode") or query.get("linkCode") or [None])[0]
    return f"roblox://experiences/start?placeId={place}" + (f"&linkCode={urllib.parse.quote(code)}" if code else "")


def hide(link):
    """Link for the console: private server codes are cut."""
    return re.sub(r"((?:code|linkCode|privateServerLinkCode)=)([^&\s]+)", lambda m: m.group(1) + "…" + m.group(2)[-4:], link)


class App:
    def __init__(self, cfg, item):
        self.package = item["package"]
        self.name = item.get("name") or self.package
        self.link = item.get("link") or cfg["link"]
        self.target = deep_link(self.link)
        self.cfg = cfg
        self.log_path = None
        self.log_pos = 0
        self.launched_at = 0.0
        self.in_game = False
        self.ever_joined = False
        self.out_since = 0.0
        self.kick = None
        self.place = None
        self.rejoins = 0
        self.fails = 0
        self.next_launch = 0.0
        self.adopted = False      # the app was already open when we started: read its whole log once
        self.logs = item.get("logDir") or None

    # --- process and log -------------------------------------------------------------------------------------
    def alive(self):
        return bool(su(f"pidof {shlex.quote(self.package)}").strip())

    def log_dir(self):
        # Roblox on Android keeps the same logs as on PC in its app data; if not there, look for them once.
        if self.logs:
            return self.logs
        default = f"/data/data/{self.package}/files/appData/logs"
        if su(f"ls {shlex.quote(default)} 2>/dev/null").strip():
            self.logs = default
        else:
            found = su(f"find /data/data/{shlex.quote(self.package)} -name '*_Player_*.log' 2>/dev/null | head -1").strip()
            self.logs = os.path.dirname(found) if found else default
        return self.logs

    def newest_log(self):
        out = su(f"ls -t {shlex.quote(self.log_dir())} 2>/dev/null")
        for name in out.split():
            if name.endswith(".log") and "_Player_" in name and "CrashHandler" not in name:
                return f"{self.log_dir()}/{name}"
        return None

    def read_new_lines(self):
        path = self.newest_log()
        if not path:
            return []
        if path != self.log_path:
            # a log that appeared after our launch is this window's; an older one we read only from its end
            self.log_path = path
            mtime = su(f"stat -c %Y {shlex.quote(path)}").strip()
            created_after = mtime.isdigit() and int(mtime) >= int(self.launched_at) - 2
            if self.adopted or created_after:
                self.log_pos = 0
            else:
                size = su(f"stat -c %s {shlex.quote(path)}").strip()
                self.log_pos = int(size) if size.isdigit() else 0
            self.adopted = False
        data = su(f"tail -c +{self.log_pos + 1} {shlex.quote(path)}")
        self.log_pos += len(data.encode("utf-8"))
        return data.splitlines()

    def update(self):
        for line in self.read_new_lines():
            m = JOIN.search(line)
            if m:
                self.place = m.group(1)
                if not self.in_game:
                    say(f"in game (place {self.place}).", "green", self.name)
                    if self.ever_joined or self.rejoins:
                        notify(self.cfg, f"✅ {self.name} is back in game.")
                self.in_game = True
                self.ever_joined = True
                self.kick = None
                self.fails = 0
                continue
            m = KICK.search(line)
            if m:
                self.kick = int(m.group(1))
            if m or DISCONNECT.search(line):
                if self.in_game:
                    self.in_game = False
                    self.out_since = time.time()

    # --- decision --------------------------------------------------------------------------------------------
    def restart_reason(self):
        now = time.time()
        if not self.alive():
            return "the app closed" if self.launched_at else "not running"
        if not self.ever_joined:
            if self.launched_at and now - self.launched_at > self.cfg["joinTimeout"]:
                return f"didn't join a server in {self.cfg['joinTimeout']} s"
            return None
        if self.kick and not self.in_game:
            return f"kicked / disconnected (code {self.kick})"
        if not self.in_game and now - self.out_since > self.cfg["outGrace"]:
            return f"out of the game for {self.cfg['outGrace']} s"
        return None

    def launch(self, reason):
        if reason not in ("not running",):
            say(f"{reason} — rejoining.", "yellow", self.name)
            notify(self.cfg, f"⚠️ {self.name}: {reason} — rejoining.")
            self.rejoins += 1
        su(f"am force-stop {shlex.quote(self.package)}")
        time.sleep(1.5)
        say(f"opening {hide(self.link)}...", "cyan", self.name)
        su(f"am start -a android.intent.action.VIEW -d {shlex.quote(self.target)} -p {shlex.quote(self.package)}")
        self.launched_at = time.time()
        self.in_game = False
        self.ever_joined = False
        self.kick = None
        self.log_path = None
        self.fails += 1
        # each failed launch in a row waits longer: 0, 15, 30, 60, ... up to 5 minutes
        self.next_launch = time.time() + min(300, 15 * (2 ** max(0, self.fails - 2))) if self.fails > 1 else 0


def notify(cfg, text):
    if not cfg.get("webhook"):
        return
    try:
        body = json.dumps({"content": text, "username": "AutoRejoin Mobile"}).encode()
        req = urllib.request.Request(cfg["webhook"], data=body, headers={"Content-Type": "application/json", "User-Agent": "AutoRejoin-Mobile"})
        urllib.request.urlopen(req, timeout=10).read()
    except Exception as e:
        say(f"Discord webhook: {e}", "gray")


def load_config():
    if not os.path.exists(CONFIG):
        return None
    with open(CONFIG, encoding="utf-8") as f:
        cfg = dict(DEFAULTS, **json.load(f))
    return cfg


def setup():
    if not su("id").startswith("uid=0"):
        say("no root: run `su` once and allow Termux (UGPhone: Settings -> Root -> allow Termux).", "red")
        return
    pkgs = find_roblox_packages()
    if not pkgs:
        say("no Roblox apps found (com.roblox.client or clones).", "red")
        return
    old = load_config() or dict(DEFAULTS)
    link = input(f"Game for all accounts — place ID, game link or VIP link [{old['link']}]: ").strip() or old["link"]
    deep_link(link)  # validates
    known = {a["package"]: a for a in old.get("accounts", [])}
    accounts = []
    for i, p in enumerate(pkgs, 1):
        item = known.get(p, {"package": p, "name": "main" if p == "com.roblox.client" else f"alt{i}", "link": ""})
        accounts.append(item)
        say(f"{item['name']}: {p}", "cyan")
    hook = input(f"Discord webhook for alerts (Enter to skip) [{'set' if old.get('webhook') else 'none'}]: ").strip() or old.get("webhook", "")
    cfg = dict(old, accounts=accounts, link=link, webhook=hook)
    with open(CONFIG, "w", encoding="utf-8") as f:
        json.dump(cfg, f, indent=2, ensure_ascii=False)
    say(f"saved {CONFIG}. Per-account games: edit \"link\" of an account. Run: python autorejoin.py", "green")


def run():
    cfg = load_config()
    if not cfg or not cfg["accounts"]:
        say("no config yet — run: python autorejoin.py setup", "yellow")
        return
    if not su("id").startswith("uid=0"):
        say("no root — AutoRejoin Mobile needs root to watch and restart Roblox (UGPhone: enable root for Termux).", "red")
        return
    apps = [App(cfg, a) for a in cfg["accounts"]]
    for app in apps:
        if app.alive():
            # already open: take its state from the whole log; no join line within joinTimeout -> relaunch
            app.adopted = True
            app.launched_at = time.time()
            say("already open — watching.", "gray", app.name)
    say(f"AutoRejoin Mobile {VERSION}: {len(apps)} account(s). Ctrl+C to stop.", "green")
    last_launch = 0.0
    while True:
        for app in apps:
            try:
                app.update()
                reason = app.restart_reason()
                if not reason:
                    continue
                now = time.time()
                if now < app.next_launch or now - last_launch < cfg["launchGap"]:
                    continue
                app.launch(reason)
                last_launch = time.time()
            except Exception as e:
                say(f"error: {e}", "red", app.name)
        time.sleep(cfg["checkEvery"])


if __name__ == "__main__":
    try:
        setup() if len(sys.argv) > 1 and sys.argv[1] == "setup" else run()
    except KeyboardInterrupt:
        say("stopped.", "gray")
