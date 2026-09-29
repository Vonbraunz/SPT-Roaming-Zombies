using System.Reflection;
using System.Text.Json.Serialization;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Web;
using SPTarkov.Server.Web.Models.Configs;
using SPTarkov.Server.Web.Services;

namespace ZombieHorde;

// SPT 4.1 replaced AbstractModMetadata + IModWebMetadata with a single IModMetadata
// interface. IsBundleMod is gone; HasPrepatcher is new (unrelated to this mod).
// IModBlazorMetadata registers this mod on the SIC Mod Pages list.
public record ZombieHordeMetadata : IModMetadata, IModBlazorMetadata
{
    public string ModGuid { get; init; } = "com.vonbraunz.roamingzombies";
    public string Name { get; init; } = "Roaming Zombies";
    public string Author { get; init; } = "DrBraun";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("1.2.2");
    // ~4.1.0 := >=4.1.0 <4.2.0 — covers the full 4.1.x hotfix line including 4.1.6
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.0");
    public bool HasPrepatcher { get; init; } = false;
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; }
    public string License { get; init; } = "MIT";

    // SIC Mod Pages entry (separate from Config Editor registration below)
    public string? WWWRootUrl { get; init; }
    public string? HomePage { get; init; } = "/roaming-zombies";
    public string? HomePageDescription { get; init; } = "Enable/disable hordes and tune spawn rates";
}


/// <summary>
/// Mutable config so SIC's Config Editor can copy property values onto the live instance.
/// Do not try to drive PMC/Scav/boss faction chances from here — ABPS owns that surface.
/// </summary>
[Injectable(InjectionType = InjectionType.Singleton)]
public class ZombieHordeConfig
{
    /// <summary>Master toggle. When false, no infected spawns are injected.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("hordeSize")]
    public HordeSizeConfig HordeSize { get; set; } = new() { Min = 2, Max = 4 };

    /// <summary>
    /// Count of pistol (BotDifficulty.hard / EZombieMode.Shooting) zombies to spawn
    /// per infected type, in addition to the melee (normal) zombies. Set min=max=0 to disable.
    /// </summary>
    [JsonPropertyName("pistolHordeSize")]
    public HordeSizeConfig PistolHordeSize { get; set; } = new() { Min = 1, Max = 2 };

    [JsonPropertyName("spawnDelaySeconds")]
    public int SpawnDelaySeconds { get; set; } = 120;

    [JsonPropertyName("spawnChance")]
    public Dictionary<string, int> SpawnChance { get; set; } = new()
    {
        ["bigmap"] = 70,
        ["factory4_night"] = 100,
        ["interchange"] = 75,
        ["laboratory"] = 100,
        ["lighthouse"] = 40,
        ["rezervbase"] = 65,
        ["sandbox"] = 65,
        ["sandbox_high"] = 65,
        ["shoreline"] = 35,
        ["tarkovstreets"] = 80,
        ["woods"] = 30,
        ["labyrinth"] = 80
    };

    [JsonPropertyName("ignoreMaxBots")]
    public bool IgnoreMaxBots { get; set; } = true;

    /// <summary>
    /// When true, zombies always spawn on every raid regardless of spawnChance.
    /// Also sets ForceSpawn=true to bypass bot limits.
    /// </summary>
    [JsonPropertyName("alwaysSpawn")]
    public bool AlwaysSpawn { get; set; } = false;

    /// <summary>
    /// Multiplies each map's spawnChance (clamped 0–100). 1.0 = unchanged.
    /// Does not affect alwaysSpawn.
    /// </summary>
    [JsonPropertyName("spawnChanceMultiplier")]
    public double SpawnChanceMultiplier { get; set; } = 1.0;

    public void CopyFrom(ZombieHordeConfig other)
    {
        Enabled = other.Enabled;
        SpawnDelaySeconds = other.SpawnDelaySeconds;
        IgnoreMaxBots = other.IgnoreMaxBots;
        AlwaysSpawn = other.AlwaysSpawn;
        SpawnChanceMultiplier = other.SpawnChanceMultiplier;
        HordeSize = new HordeSizeConfig { Min = other.HordeSize.Min, Max = other.HordeSize.Max };
        PistolHordeSize = new HordeSizeConfig { Min = other.PistolHordeSize.Min, Max = other.PistolHordeSize.Max };
        SpawnChance = new Dictionary<string, int>(other.SpawnChance);
    }
}

public class HordeSizeConfig
{
    [JsonPropertyName("min")]
    public int Min { get; set; }

    [JsonPropertyName("max")]
    public int Max { get; set; }
}

/// <summary>
/// Singleton service that owns the loaded config and can re-inject zombie
/// BossLocationSpawn entries on demand — called at startup, after each raid end,
/// and when SIC applies a config change.
/// </summary>
[Injectable(InjectionType = InjectionType.Singleton)]
public class ZombieSpawnService(
    ISptLogger<ZombieSpawnService> logger,
    LocationTable locationTable,
    RandomUtil randomUtil,
    ZombieHordeConfig config)
{
    public static readonly Dictionary<string, string> MapZones = new()
    {
        ["bigmap"]         = "ZoneDormitory,ZoneGasStation,ZoneScavBase,ZoneBrige,ZoneCustoms,ZoneOldVill",
        ["factory4_night"] = "BotZone",
        ["interchange"]    = "ZoneCenterBot,ZoneCenter,ZoneOLI,ZoneIDEA,ZoneGoshan",
        ["laboratory"]     = "BotZoneFloor1,BotZoneFloor2,BotZoneBasement",
        ["lighthouse"]     = "Zone_TreatmentContainers,Zone_Chalet,Zone_Blockpost,Zone_DestroyedHouse,Zone_Rocks,Zone_Village",
        ["rezervbase"]     = "ZoneRailStrorage,ZonePTOR2,ZoneBarrack,ZoneSubStorage,ZonePTOR1",
        ["sandbox"]        = "ZoneSandbox",
        ["sandbox_high"]   = "ZoneSandbox",
        ["shoreline"]      = "ZoneGreenHouses,ZonePort,ZoneSanatorium1,ZoneSanatorium2,ZoneSmuglers,ZoneMeteoStation",
        ["tarkovstreets"]  = "ZoneCarShowroom,ZoneClimova,ZoneMvd,ZoneSW01,ZoneConcordia",
        ["woods"]          = "ZoneWoodCutter,ZoneScavBase2,ZoneMiniHouse,ZoneBrokenVill,ZoneBigRocks",
        ["labyrinth"]      = "BotZone"
    };

    internal static readonly string[] ZombieTypes =
    [
        "infectedAssault",
        "infectedPmc",
        "infectedCivil",
        "infectedLaborant"
    ];

    private static readonly HashSet<string> ZombieTypeSet = new(ZombieTypes, StringComparer.OrdinalIgnoreCase);

    private bool _loaded;

    public ZombieHordeConfig Config => config;

    public void MarkLoaded() => _loaded = true;

    /// <summary>
    /// Removes only our infected BossLocationSpawn rows. Leaves ABPS / vanilla / other
    /// faction entries untouched — we never edit PMC/Scav/boss chances.
    /// </summary>
    public void ClearZombieSpawns()
    {
        var locationDict = locationTable.GetDictionary();
        var removed = 0;

        foreach (var map in MapZones.Keys)
        {
            var actualKey = locationTable.GetMappedKey(map);
            if (!locationDict.TryGetValue(actualKey, out var location))
                continue;

            var list = location.Base.BossLocationSpawn;
            removed += list.RemoveAll(spawn =>
                spawn.BossName != null && ZombieTypeSet.Contains(spawn.BossName));
        }

        if (removed > 0)
            logger.Info($"[RoamingZombies] Cleared {removed} infected BossLocationSpawn entries");
    }

    public void InjectSpawns()
    {
        if (!_loaded)
        {
            logger.Warning("[RoamingZombies] InjectSpawns called before config was loaded — skipping");
            return;
        }

        // Always strip our previous rows first so SIC re-apply and post-raid re-inject
        // cannot stack duplicate infected waves. ABPS wipe already empties the list;
        // clearing infected-only is still safe and leaves ABPS rows alone.
        ClearZombieSpawns();

        if (!config.Enabled)
        {
            logger.Info("[RoamingZombies] Disabled via config — no zombie spawns injected");
            return;
        }

        var locationDict = locationTable.GetDictionary();
        var totalMaps = 0;
        var multiplier = Math.Clamp(config.SpawnChanceMultiplier, 0.0, 10.0);

        foreach (var (map, zone) in MapZones)
        {
            if (!config.SpawnChance.TryGetValue(map, out var baseChance))
                continue;

            var chance = (int)Math.Clamp(Math.Round(baseChance * multiplier), 0, 100);

            // Roll dice server-side so the percentage actually works.
            // alwaysSpawn bypasses the roll entirely and forces spawns.
            if (!config.AlwaysSpawn)
            {
                if (chance <= 0)
                    continue;
                if (chance < 100 && randomUtil.GetInt(1, 101) > chance)
                    continue;
            }

            var actualKey = locationTable.GetMappedKey(map);
            if (!locationDict.TryGetValue(actualKey, out var location))
                continue;

            // ─── WAVE LOGIC (tested + working in 1.2.1, disabled for release) ────────────
            // Scaffolding for the v2.0 "rolling waves throughout the raid" feature is kept
            // here so we can re-enable with a one-line change when 2.0 ships. With
            // WaveCount = 1 the for loop degenerates to a single pass at SpawnDelaySeconds,
            // matching pre-1.2.1 single-spawn behavior. Bump WaveCount to 5 to get
            // 5 waves spaced WaveIntervalSeconds apart.
            const int WaveCount           = 1;    // 1 = single spawn; >1 = wave-based
            const int WaveIntervalSeconds = 60;   // seconds between consecutive waves
            // ──────────────────────────────────────────────────────────────────────────────

            for (int wave = 0; wave < WaveCount; wave++)
            {
                // First wave at SpawnDelaySeconds, each subsequent wave WaveInterval later.
                var waveTime = config.SpawnDelaySeconds + (wave * WaveIntervalSeconds);

                foreach (var zombieType in ZombieTypes)
                {
                    // Melee zombies (BotDifficulty.normal → EZombieMode.Fast → knife).
                    var meleeCount = randomUtil.GetInt(config.HordeSize.Min, config.HordeSize.Max + 1);
                    location.Base.BossLocationSpawn.Add(new BossLocationSpawn
                    {
                        BossName             = zombieType,
                        BossChance           = 100,
                        BossDifficulty       = "normal",
                        BossEscortType       = zombieType,
                        BossEscortAmount     = meleeCount.ToString(),
                        BossEscortDifficulty = "normal",
                        BossZone             = zone,
                        Delay                = 0,
                        DependKarma          = false,
                        DependKarmaPVE       = false,
                        ForceSpawn           = config.AlwaysSpawn,
                        IgnoreMaxBots        = config.IgnoreMaxBots,
                        SpawnMode            = null,
                        Supports             = null!,
                        Time                 = waveTime,
                        TriggerId            = "",
                        TriggerName          = ""
                    });

                    // Pistol zombies (BotDifficulty.hard → EZombieMode.Shooting → Makarov).
                    var pistolCount = randomUtil.GetInt(config.PistolHordeSize.Min, config.PistolHordeSize.Max + 1);
                    if (pistolCount > 0)
                    {
                        location.Base.BossLocationSpawn.Add(new BossLocationSpawn
                        {
                            BossName             = zombieType,
                            BossChance           = 100,
                            BossDifficulty       = "hard",
                            BossEscortType       = zombieType,
                            BossEscortAmount     = pistolCount.ToString(),
                            BossEscortDifficulty = "hard",
                            BossZone             = zone,
                            Delay                = 0,
                            DependKarma          = false,
                            DependKarmaPVE       = false,
                            ForceSpawn           = config.AlwaysSpawn,
                            IgnoreMaxBots        = config.IgnoreMaxBots,
                            SpawnMode            = null,
                            Supports             = null!,
                            Time                 = waveTime,
                            TriggerId            = "",
                            TriggerName          = ""
                        });
                    }
                }
            }

            totalMaps++;
        }

        logger.Info($"[RoamingZombies] Zombie spawns injected into {totalMaps} maps");
    }
}

/// <summary>
/// IOnLoad — loads config and does the initial spawn injection.
/// SPT 4.1 removed the old OnLoadOrder.PostDBModLoader stage as part of the
/// Config/Database-server removal; PostLoad (the last defined stage) is the closest
/// equivalent for "run once everything, including other mods' DB edits, is loaded".
/// The +70000 offset mirrors the old BPS-relative ordering — re-verify against BPS's
/// own 4.1 priority once that mod updates.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 70000)]
public class ZombieHordeServer(
    ISptLogger<ZombieHordeServer> logger,
    ZombieSpawnService spawnService,
    ZombieHordeConfig config,
    ModHelper modHelper,
    JsonUtil jsonUtil) : IOnLoad
{
    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var modPath = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        var path = Path.Combine(modPath, "config.json");
        var loaded = await jsonUtil.DeserializeFromFileAsync<ZombieHordeConfig>(path);

        if (loaded == null)
        {
            logger.Error("[RoamingZombies] Failed to load config.json — using in-memory defaults");
        }
        else
        {
            config.CopyFrom(loaded);
        }

        spawnService.MarkLoaded();
        spawnService.InjectSpawns();
    }
}

/// <summary>
/// Re-injects zombie spawns after every raid end.
/// BPS hooks /client/match/local/end and wipes ALL BossLocationSpawn entries before
/// rebuilding its own. Because alphabetical order puts _botplacementsystem before
/// ZombieHorde at equal TypePriority, BPS's handler fires first — our handler fires
/// second, after the wipe, and puts the zombies back.
/// </summary>
[Injectable]
public class ZombieHordeRouter(ZombieSpawnService spawnService, JsonUtil jsonUtil)
    : StaticRouter(
        jsonUtil,
        [
            new RouteAction(
                "/client/match/local/end",
                async (url, info, sessionId, output, cancellationToken) =>
                {
                    spawnService.InjectSpawns();
                    return output;
                })
        ])
{ }

/// <summary>
/// Registers config.json with SPT's SIC Config Editor (Mod Configs tab).
/// </summary>
[Injectable]
public class ZombieHordeConfigEditorProvider(
    ZombieHordeConfig config,
    ZombieSpawnService spawnService,
    ModHelper modHelper) : IConfigEditorConfigProvider
{
    public IEnumerable<ConfigEditorConfigRegistration> GetConfigs()
    {
        var modPath = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        var filePath = Path.Combine(modPath, "config.json");

        // After SIC copies edited JSON onto the live singleton, rebuild spawn tables.
        yield return ConfigEditorConfigRegistration.Create(
            id: "com.vonbraunz.roamingzombies",
            displayName: "Roaming Zombies",
            runtimeConfig: config,
            filePath: filePath) with
        {
            OnAppliedToRuntimeAsync = (edited, cancellationToken) =>
            {
                if (edited is ZombieHordeConfig updated)
                    config.CopyFrom(updated);

                spawnService.InjectSpawns();
                return ValueTask.CompletedTask;
            }
        };
    }
}
