# AutoRejoin Mobile

Keeps Roblox accounts in game on a **rooted Android**: UGPhone, other cloud phones, emulators.
Every Roblox app (`com.roblox.client` and its clones) is one account that is already logged in — **no cookies**.

It watches each app (process + Roblox log) and, when the app closes, gets kicked, disconnected (error 256 and others)
or sits outside a server, force-stops it and opens the game again: a public server or your VIP.

## Install (Termux)

```sh
pkg install -y python curl
curl -O https://raw.githubusercontent.com/clxwnlessss/AutoRejoin/main/mobile/autorejoin.py
su -c id          # allow root for Termux when asked (UGPhone: enable root in the phone settings)
python autorejoin.py setup
python autorejoin.py
```

`setup` finds the Roblox apps and asks for the game (place ID, game link or VIP link) and an optional Discord webhook.

## config.json

```json
{
  "link": "1537690962",
  "accounts": [
    { "package": "com.roblox.client", "name": "main", "link": "" },
    { "package": "com.roblox.clienu", "name": "alt1", "link": "https://www.roblox.com/share?code=...&type=Server" }
  ],
  "checkEvery": 5,
  "joinTimeout": 120,
  "outGrace": 45,
  "launchGap": 8,
  "webhook": ""
}
```

- `link` of an account — its own game or VIP; empty = the common `link`.
- `outGrace` — how long to wait after a menu exit / disconnect without a code (teleports into matches go through the menu).
- `logDir` (optional, per account) — Roblox log folder if it isn't found automatically.
