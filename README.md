# Roaming Zombies

**A horde mod for [SPT](https://www.sp-tarkov.com/) that turns every raid into survival horror.** Mixed hordes of infected bots spawn on every map, any time of day. Melee zombies hunt you down and swing knives in your face. Pistol zombies shoot from cover. One horde per raid — you get a notification, then they come.

> *"The dead are rising... zombies have been spotted nearby."*

[Download v1.2.0](../../releases) · [Changelog](#changelog) · [Fika + ABPS compatible](#compatibility)

> ### ⚠️ I cannot stress this enough — zombies are a real challenge now
>
> Previous versions had zombies that shuffled around harmlessly. **v1.2.0 is a different mod.** Melee zombies actively hunt you, face you before each swing, and chain knife attacks at 1-second intervals. Pistol zombies engage from 15 m with real aim. A horde on Labs or night Factory can absolutely wipe you if you're not paying attention. **Plan your raid, bring ammo, watch your six.**

---

## What's new in v1.2.0

**Zombies are an actual threat now. I cannot stress this enough.** Previous versions spawned infected bots that shambled around and couldn't fight back — a bizarre touch-damage effect was the only way they could hurt you. Not anymore. Expect to die if you don't respect the horde.

### Melee zombies
- **Actively hunt the player.** They pathfind toward you and face you before each swing so the hit actually lands.
- **Real knife swing animations.** EFT's standard `KnifeController.MakeKnifeKick()` is triggered directly — proper animation, proper hit-cast, proper damage.
- **Damage is native.** Hits go through EFT's `KnifeCollider` → `ApplyDamage` pipeline using whatever knife the bot spawned with (Bayonet, Kiba, Hatchet, Halloween knife). **Armor class reduction works normally** — a level-4 vest actually protects you now.
- **1 second swing cadence** once in range. They'll chain combos on you if you don't move.

### Pistol zombies (new)
- A secondary horde spawns at `BotDifficulty=hard` which activates EFT's `EZombieMode.Shooting` — they pull a Makarov and engage you from 15 meters.
- Route through EFT's standard bot shoot AI, so no client-side hacks — they aim, fire, lead you, take cover, all the usual stuff.
- Configurable count per horde. Default is 1-2 pistol zombies per infected type per raid.

### Plus the classic touch-infection damage
EFT's built-in zombie-infection contact damage still stacks on top. Get surrounded and you'll bleed health even between swings. Good luck.

---

## Compatibility

### ✅ Fika — including headless

Fully supported across all Fika modes. The client DLL must be installed on **whichever machine is authoritative for bot AI** — that's the host in a host/client session, or the headless in a headless session. Zombies pursue the closest human player in all three setups.

| Fika setup | Client DLL install | Notes |
|---|---|---|
| Solo SPT | Local client | Single-player, just works |
| Fika host + clients | **Host required**, clients optional | Host drives pursuit + swings for everyone. Clients that also install the DLL get the horde notification + alert sound locally |
| **Fika headless + clients** | **Headless required**, clients optional | Headless drives pursuit + swings for all connected players. Install client DLL on connected player machines only if you want the notification + audio on their HUD |

The `SpawnMode=null` fix (shipped in v1.1.1) ensures zombies actually spawn in Fika headless sessions — earlier builds used `["regular","pve"]` which EFT's bot spawner silently filtered out on headless.

Zombies target the **closest human player** to each zombie, so in a Fika session with multiple players the horde splits up naturally rather than all piling onto one person.

**SAIN users:** SAIN explicitly excludes all five infected WildSpawnTypes from its brain overrides (`BotSpawnController.StrictExclusionList`), so SAIN and Roaming Zombies don't fight each other. You can run both together cleanly.

### ✅ ABPS (Acid's Bot Placement System)

ABPS is a great mod, but its post-raid spawn wipe breaks most horde mods. We hook the same endpoint and re-inject zombie spawns **after** ABPS's wipe runs, every single raid. The mods coexist — ABPS does its scav wave thing, we keep the infected horde alive.

```
/client/match/local/end
  ├─ ABPS wipes BossLocationSpawn entries     ← fires first (alphabetical order)
  └─ ZombieHordeRouter re-injects zombies     ← fires second, restores the horde
```

Tested. Works every raid, forever.

### Requirements

| Requirement | Version |
|---|---|
| SPT | `~4.0.13` |
| BepInEx ConfigurationManager | *optional* (for F12 panel) |

**No mod dependencies.** No BigBrain, no SAIN, no MoreBotsAPI. Self-contained.

---

## Installation

1. Download the latest release ZIP from [Releases](../../releases)
2. Extract into your SPT root folder — files land in the correct spots automatically:

```
SPT Root/
├── BepInEx/plugins/ZombieHorde/
│   └── ZombieHorde.Client.dll
└── SPT/user/mods/ZombieHorde/
    ├── ZombieHorde.Server.dll
    └── config.json
```

3. Restart SPT Server, launch the game

### Fika-specific install notes

The client DLL must be on **whichever machine drives bot AI**:

- **Fika host session:** install the client DLL on the host machine. Guest clients can install it too for notifications + audio, but pursuit and attacks are driven by the host.
- **Fika headless session:** install the client DLL in the headless's `BepInEx/plugins/ZombieHorde/` folder. This is where bots live and where our pursuit + swing logic needs to run. Guest clients can install the DLL for notifications + audio if they want.

If you skip this step on a headless and only install on the player machines, zombies will spawn but act half-asleep — they'll drift toward players via EFT's native patrol but won't actively hunt or attack.

---

## Configuration

Edit `SPT/user/mods/ZombieHorde/config.json`:

```json
{
  "hordeSize": {
    "min": 2,
    "max": 4
  },
  "pistolHordeSize": {
    "min": 1,
    "max": 2
  },
  "ignoreMaxBots": true,
  "alwaysSpawn": false,
  "spawnDelaySeconds": 120,
  "spawnChance": {
    "bigmap": 70,
    "factory4_night": 100,
    "interchange": 75,
    "laboratory": 100,
    "lighthouse": 40,
    "rezervbase": 65,
    "sandbox": 65,
    "sandbox_high": 65,
    "shoreline": 35,
    "tarkovstreets": 80,
    "woods": 30,
    "labyrinth": 80
  }
}
```

| Field | Description |
|---|---|
| `hordeSize.min` / `max` | Number of **melee** zombies per infected type (there are 4 types, so 2–4 × 4 = 8–16 melee zombies per raid at defaults) |
| `pistolHordeSize.min` / `max` | Number of **pistol** zombies per infected type. Set both to `0` for melee-only hordes |
| `ignoreMaxBots` | When `true`, zombies bypass the map's bot cap so they always spawn alongside other AI |
| `alwaysSpawn` | **The "100% on every map" override.** When `true`, ignores `spawnChance` entirely — zombies spawn in every raid on every map. Also forces `ForceSpawn=true` so they ignore bot caps. Flip this on if you want guaranteed hordes everywhere, every raid |
| `spawnDelaySeconds` | Seconds after raid start before the horde spawns. Default 120 s gives you time to load in and orient yourself before it hits |
| `spawnChance` | Per-map percent chance (0–100). Rolled server-side. Set to `100` for guaranteed spawns, `0` to disable zombies on a specific map |

### Default spawn chances

Hand-tuned per map — tight/atmospheric maps are guaranteed, open maps are sparse so the horde feels like an encounter, not a background hum. The range is **30–100 %**, with Labs and night Factory always firing.

| Map | Default | Rationale |
|---|---|---|
| `factory4_night` | **100 %** | Small, dark, horde paradise |
| `laboratory` (Labs) | **100 %** | Tight corridors, maximum horror |
| `labyrinth` | 80 % | Dark and tight, thematic fit |
| `tarkovstreets` (Streets) | 80 % | Dense urban, excellent fit |
| `interchange` | 75 % | Mid-size urban |
| `bigmap` (Customs) | 70 % | Mid-size military/urban |
| `rezervbase` (Reserve) | 65 % | Mid-size military |
| `sandbox` / `sandbox_high` (Ground Zero) | 65 % | Small but open |
| `lighthouse` | 40 % | Large coastal, atmospheric-only |
| `shoreline` | 35 % | Huge open map, sparse |
| `woods` | 30 % | Biggest map, sparse by design |

Tune however you like. Labs every raid and Woods off entirely? Set Woods to `0`. Want zombies *everywhere* 100 % of the time? Skip the per-map dice roll and flip `alwaysSpawn` to `true` — it overrides all `spawnChance` values.

**Default horde size:** 12–24 zombies per raid (8–16 melee + 4–8 pistol).

---

## F12 Settings Panel

With [BepInEx ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager) installed, press **F12** in-raid for live toggles:

| Setting | Default |
|---|---|
| Show Horde Spawn Alert | `true` |
| Play Horde Audio | `true` |

Melee damage is no longer tunable via F12 — swings now use EFT's native damage pipeline with whatever knife the zombie spawned with, so armor reduction works naturally. If you want tougher/weaker zombies, adjust the infected bot JSONs directly or use a bot-difficulty mod.

---

## Custom Alert Sound

Drop an audio file named `horde_alert.ogg` (or `.wav`) into:

```
BepInEx/plugins/ZombieHorde/horde_alert.ogg
```

It plays once when the first zombie of the horde is detected. If no file is present, only the HUD notification appears.

---

## How It Works

For modders / curious readers:

**Server side** — `ZombieSpawnService` injects `BossLocationSpawn` entries for all four infected types on each map, with two difficulty variants per type:
- `BotDifficulty=normal` → `EZombieMode.Fast` (melee, knife)
- `BotDifficulty=hard` → `EZombieMode.Shooting` (ranged, Makarov)

`ZombieHordeRouter` hooks `/client/match/local/end` to re-inject after raid end, coexisting with ABPS's wipe via alphabetical ordering (we fire second).

**Client side — `RaidStartPatch`** forces `Side=Savage` on every infected bot so they behave as hostile AI and don't get side-filtered by PMCs / scavs.

**Client side — pursuit and swing:**

EFT's infected brain (`GClass324`) has a working combat layer (`GClass98`) that correctly decides "run at player, then melee" at close range. But its pursuit method `BotMeleeWeaponData.RunToEnemyUpdate()` bails on its internal `CanRunToEnemyToHit` NavMesh check — we confirmed this by dnSpy'ing a Halloween 2024 client build (EFT 0.15.5.1) and comparing it to current EFT. The attack decision is returned, but the logic node that would execute it never runs.

So we bypass the broken pursuit path entirely:
- `BotOwner.Steering.LookToPoint(playerPos)` — face the player (called before every swing and every repath)
- `BotOwner.GoToPoint(playerPos)` — navigate toward the player directly, skipping the failing path check
- `KnifeController.MakeKnifeKick()` — trigger the swing animation at close range, which enables the `KnifeCollider` for its active window. Damage flows through EFT's standard `KnifeCollider.OnHit` → `ActiveHealthController.ApplyDamage` pipeline.

**Pistol zombies** get zero client-side intervention — their brain returns `BotLogicDecision.shootFromPlace` / `attackMoving`, which route to well-tested standard logic nodes in EFT's bot code. They just work.

---

## Changelog

### v1.2.0 — "The Dead Actually Rise"
The one that makes zombies threatening.
- **Real knife swings** with native damage and animation. Melee zombies attack via `KnifeController.MakeKnifeKick()`; armor class reduction works naturally.
- **Active pursuit** — melee zombies pathfind toward the player and face you before each swing.
- **Pistol zombies** added at `BotDifficulty=hard`. Configurable count via `pistolHordeSize`.
- Removed the reflection-based `ApplyMeleeDamage` hack (no longer needed).
- Removed the "night check" dead code + fixed the docs (zombies have always spawned any time of day; now the README says so).
- F12 panel simplified — only notification + audio toggles remain.

### v1.1.1
- F12 settings panel via BepInEx ConfigurationManager.
- Default `spawnDelaySeconds` raised to 120 s.
- **Fika headless spawn fix** — `SpawnMode` set to `null` (was `["regular","pve"]`, filtered out in headless sessions).
- Real `spawnChance` percentages — dice rolled server-side before injection.
- `alwaysSpawn` override added.

### v1.1.0
- Fixed zombie melee damage (reflection field-matching bug)
- **BPS persistence fix** — horde re-injected across raids after Acid's Bot Placement System wipes spawns
- Fixed infected bot faction (`EPlayerSide.Savage`)
- Removed BigBrain dependency

### v1.0.0
- Initial release

---

## License

MIT — see [LICENSE](LICENSE)

---

*Made by DrBraun. Thanks to the SPT modding community for dnSpy help, SAIN for making the compat research straightforward, and BSG for leaving just enough of the Halloween event wiring in place to hack together.*
