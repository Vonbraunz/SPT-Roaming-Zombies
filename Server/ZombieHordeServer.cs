using System.Reflection;
using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Web;

namespace ZombieHorde;

public record ZombieHordeMetadata : AbstractModMetadata, IModWebMetadata
{
    public override string ModGuid { get; init; } = "com.vonbraunz.roamingzombies";
    public override string Name { get; init; } = "Roaming Zombies";
    public override string Author { get; init; } = "DrBraun";
    public override List<string>? Contributors { get; init; }
    public override SemanticVersioning.Version Version { get; init; } = new("1.1.1");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.13");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public override string? Url { get; init; }
    public override bool? IsBundleMod { get; init; } = false;
    public override string License { get; init; } = "MIT";
}

/// <summary>
/// Singleton service that owns the loaded config and can re-inject zombie
/// BossLocationSpawn entries on demand — called at startup and after each raid end.
/// </summary>
[Injectable(InjectionType = InjectionType.Singleton)]
public class ZombieSpawnService(
    ISptLogger<ZombieSpawnService> logger,
    DatabaseService databaseService,
    RandomUtil randomUtil)
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

    private static readonly string[] ZombieTypes =
    [
        "infectedAssault",
        "infectedPmc",
        "infectedCivil",
        "infectedLaborant"
    ];

    private ZombieHordeConfig? _config;

    public void Initialize(ZombieHordeConfig config)
    {
        _config = config;
    }

    public void InjectSpawns()
    {
        if (_config == null)
        {
            logger.Warning("[RoamingZombies] InjectSpawns called before config was loaded — skipping");
            return;
        }

        var locations = databaseService.GetLocations();
        var locationDict = locations.GetDictionary();
        var totalMaps = 0;

        foreach (var (map, zone) in MapZones)
        {
            if (!_config.SpawnChance.TryGetValue(map, out var chance))
                continue;

            // Roll dice server-side so the percentage actually works.
            // alwaysSpawn bypasses the roll entirely and forces spawns.
            if (!_config.AlwaysSpawn)
            {
                if (chance <= 0)
                    continue;
                if (chance < 100 && randomUtil.GetInt(1, 101) > chance)
                    continue;
            }

            var actualKey = locations.GetMappedKey(map);
            if (!locationDict.TryGetValue(actualKey, out var location))
                continue;

            foreach (var zombieType in ZombieTypes)
            {
                var count = randomUtil.GetInt(_config.HordeSize.Min, _config.HordeSize.Max + 1);

                location.Base.BossLocationSpawn.Add(new BossLocationSpawn
                {
                    BossName             = zombieType,
                    BossChance           = 100,
                    BossDifficulty       = "normal",
                    BossEscortType       = zombieType,
                    BossEscortAmount     = count.ToString(),
                    BossEscortDifficulty = "normal",
                    BossZone             = zone,
                    Delay                = 0,
                    DependKarma          = false,
                    DependKarmaPVE       = false,
                    ForceSpawn           = _config.AlwaysSpawn,
                    IgnoreMaxBots        = _config.IgnoreMaxBots,
                    SpawnMode            = null,
                    Supports             = null!,
                    Time                 = _config.SpawnDelaySeconds,
                    TriggerId            = "",
                    TriggerName          = ""
                });
            }

            totalMaps++;
        }

        logger.Info($"[RoamingZombies] Zombie spawns injected into {totalMaps} maps");
    }
}

/// <summary>
/// IOnLoad — loads config and does the initial spawn injection.
/// Priority > BPS (PostDBModLoader + 69420) so we run after BPS's startup wipe.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 70000)]
public class ZombieHordeServer(
    ISptLogger<ZombieHordeServer> logger,
    ZombieSpawnService spawnService,
    ModHelper modHelper,
    JsonUtil jsonUtil) : IOnLoad
{
    public async Task OnLoad()
    {
        var modPath  = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        var config   = await jsonUtil.DeserializeFromFileAsync<ZombieHordeConfig>(Path.Combine(modPath, "config.json"));

        if (config == null)
        {
            logger.Error("[RoamingZombies] Failed to load config.json");
            return;
        }

        spawnService.Initialize(config);
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
                async (url, info, sessionId, output) =>
                {
                    spawnService.InjectSpawns();
                    return output;
                })
        ])
{ }

public record ZombieHordeConfig
{
    [JsonPropertyName("hordeSize")]
    public required HordeSizeConfig HordeSize { get; set; }

    [JsonPropertyName("spawnDelaySeconds")]
    public int SpawnDelaySeconds { get; set; }

    [JsonPropertyName("spawnChance")]
    public required Dictionary<string, int> SpawnChance { get; set; }

    [JsonPropertyName("ignoreMaxBots")]
    public bool IgnoreMaxBots { get; set; } = true;

    /// <summary>
    /// When true, zombies always spawn on every raid regardless of spawnChance.
    /// Also sets ForceSpawn=true to bypass bot limits.
    /// </summary>
    [JsonPropertyName("alwaysSpawn")]
    public bool AlwaysSpawn { get; set; } = false;
}

public record HordeSizeConfig
{
    [JsonPropertyName("min")]
    public int Min { get; set; }

    [JsonPropertyName("max")]
    public int Max { get; set; }
}
