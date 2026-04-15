# Roaming Zombies

A server + client mod for [SPT](https://www.sp-tarkov.com/) that spawns mixed hordes of infected bots on all maps — **night raids only**. One horde per raid, arriving on a configurable delay.

---

## Features

- Spawns a mixed horde of `infectedAssault`, `infectedPmc`, `infectedCivil`, and `infectedLaborant` bots
- **Night raids only** (22:00 – 06:00 in-game time) — daytime raids are unaffected
- **One horde per raid** — spawns once on a timer, never again
- **Persists across raids** — horde spawn data is re-injected after each session ends
- Zombies deal melee damage on proximity (35 HP/hit, 1.5 s cooldown, 4 m range)
- Configurable spawn chance, horde size, and spawn delay per map
- HUD notification when the horde arrives: *"The dead are rising..."*
- Optional custom alert sound (`horde_alert.ogg` / `.wav`)
- SPT Forge compatible — version-checked against SPT automatically
- **No extra mod dependencies required**

---

## Requirements

| Requirement | Version |
|---|---|
| SPT | `~4.0.13` |

---

## Installation

1. Download the latest release ZIP from the [Releases](../../releases) page
2. Extract the ZIP into your SPT root folder — files will drop into the correct locations automatically:

```
SPT Root/
├── BepInEx/plugins/ZombieHorde/
│   └── ZombieHorde.Client.dll
└── SPT/user/mods/ZombieHorde/
    ├── ZombieHorde.Server.dll
    └── config.json
```

3. Restart SPT Server and launch the game

---

## Configuration

Edit `SPT/user/mods/ZombieHorde/config.json`:

```json
{
  "hordeSize": {
    "min": 2,
    "max": 4
  },
  "spawnDelaySeconds": 120,
  "spawnChance": {
    "bigmap": 70,
    "factory4_night": 100,
    "interchange": 70,
    "laboratory": 50,
    "lighthouse": 70,
    "rezervbase": 70,
    "sandbox": 50,
    "sandbox_high": 50,
    "shoreline": 70,
    "tarkovstreets": 80,
    "woods": 70,
    "labyrinth": 60
  }
}
```

| Field | Description |
|---|---|
| `hordeSize.min` / `max` | Number of bots spawned **per zombie type** (4 types × min–max each) |
| `spawnDelaySeconds` | Seconds after raid start before the horde spawns |
| `spawnChance` | Percent chance (0–100) the horde spawns on each map. Set to `0` to disable a map |

> **Total horde size** = 4 types × `hordeSize` random roll each. With default settings (2–4 per type), expect 8–16 zombies per raid.

---

## Custom Alert Sound

Drop an audio file named `horde_alert.ogg` (or `horde_alert.wav`) into:

```
BepInEx/plugins/ZombieHorde/horde_alert.ogg
```

The sound plays for the local player when the first zombie of the horde is detected. If no file is present, only the HUD notification displays.

---

## Fika Compatibility

✅ **Compatible with [Fika](https://github.com/project-fika/Fika-Plugin)**

| Component | Notes |
|---|---|
| **Server mod** | Installed on the SPT server as normal — all players in the session get the same zombie spawns automatically |
| **Client mod** | Must be installed by **all players** (host + guests) to receive the HUD notification and alert sound. Zombies spawn correctly even if guests don't have it installed |
| **Night check** | Runs on the host client (who controls bot spawning in Fika), so daytime filtering works correctly |
| **Spawn mode** | Uses `"regular"` spawn mode — correct for Fika co-op sessions |

**TL;DR:** Install the server mod on the server, and have all players install the client mod for the full experience.

---

## How It Works

- **Server side:** At startup, injects `BossLocationSpawn` entries for all four infected bot types into each map's spawn data using the map's real bot zones. After each raid ends (`/client/match/local/end`), spawn data is re-injected so zombies appear in every subsequent raid
- **Client side:** On raid start, checks `GameWorld.GameDateTime.Calculate().Hour` — if it's daytime (06:00–22:00), zombie entries are stripped before `BossSpawnScenario` processes them
- **Melee damage:** `ZombieSpawnDetector` tracks all live zombie bots each frame. When one closes within 4 m of the player it applies damage directly to the health controller via reflection (`ApplyDamage` on `ActiveHealthController`), on a per-zombie cooldown
- **Detection:** Subscribes to `GameWorld.OnPersonAdd` — when the first infected bot appears, fires the HUD notification and plays the alert sound (once per raid)

---

## Changelog

### v1.1.0
- Fixed zombie melee damage (reflection field-matching bug — `DamageType` was found before `Damage`)
- Fixed horde persistence across raids (BPS spawn wipe after session end)
- Fixed infected bot faction (`EPlayerSide.Savage` now correctly forced at spawn)
- Removed BigBrain dependency — no extra mods required

### v1.0.0
- Initial release

---

## License

MIT — see [LICENSE](LICENSE)

---

*Made by DrBraun*
