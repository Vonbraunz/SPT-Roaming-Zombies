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
    public override SemanticVersioning.Version Version { get; init; } = new("1.0.0");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.13");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public override string? Url { get; init; }
    public override bool? IsBundleMod { get; init; } = false;
    public override string License { get; init; } = "MIT";
}

[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 100)]
public class ZombieHordeServer(
    ISptLogger<ZombieHordeServer> logger,
    DatabaseService databaseService,
    ModHelper modHelper,
    RandomUtil randomUtil,
    JsonUtil jsonUtil) : IOnLoad
{
    private static readonly Dictionary<string, string> MapZones = new()
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

    // Zombie types spawned as separate independent waves — all fire at the same time
    // so they arrive together as a mixed horde. Avoids BossSupport serialization issues.
    private static readonly string[] ZombieTypes =
    [
        "infectedAssault",
        "infectedPmc",
        "infectedCivil",
        "infectedLaborant"
    ];

    private static readonly List<string> ValidMaps = [.. MapZones.Keys];

    public async Task OnLoad()
    {
        var modPath = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        var configPath = Path.Combine(modPath, "config.json");
        var config = await jsonUtil.DeserializeFromFileAsync<ZombieHordeConfig>(configPath);

        if (config == null)
        {
            logger.Error("[RoamingZombies] Failed to load config.json");
            return;
        }

        var locations = databaseService.GetLocations();
        var locationDict = locations.GetDictionary();

        foreach (var map in ValidMaps)
        {
            if (!config.SpawnChance.TryGetValue(map, out var chance))
                continue;

            var actualKey = locations.GetMappedKey(map);
            if (!locationDict.TryGetValue(actualKey, out var location))
            {
                logger.Warning($"[RoamingZombies] Map '{map}' not found in locations");
                continue;
            }

            var zone = MapZones[map];
            var totalSpawned = 0;

            foreach (var zombieType in ZombieTypes)
            {
                var count = randomUtil.GetInt(config.HordeSize.Min, config.HordeSize.Max + 1);

                location.Base.BossLocationSpawn.Add(new BossLocationSpawn
                {
                    BossName = zombieType,
                    BossChance = chance,
                    BossDifficulty = "normal",
                    BossEscortType = zombieType,
                    BossEscortAmount = count.ToString(),
                    BossEscortDifficulty = "normal",
                    BossZone = zone,
                    Delay = 0,
                    DependKarma = false,
                    DependKarmaPVE = false,
                    ForceSpawn = false,
                    IgnoreMaxBots = true,
                    SpawnMode = ["regular", "pve"],
                    Supports = null!,
                    Time = config.SpawnDelaySeconds,
                    TriggerId = "",
                    TriggerName = ""
                });

                totalSpawned += count;
            }

            logger.Info($"[RoamingZombies] {map}: ~{totalSpawned} zombies across {ZombieTypes.Length} types, {chance}% chance, {config.SpawnDelaySeconds}s delay");
        }

        logger.Info("[RoamingZombies] Zombie spawns injected into all valid maps");
    }
}

public record ZombieHordeConfig
{
    [JsonPropertyName("hordeSize")]
    public required HordeSizeConfig HordeSize { get; set; }

    [JsonPropertyName("spawnDelaySeconds")]
    public int SpawnDelaySeconds { get; set; }

    [JsonPropertyName("spawnChance")]
    public required Dictionary<string, int> SpawnChance { get; set; }
}

public record HordeSizeConfig
{
    [JsonPropertyName("min")]
    public int Min { get; set; }

    [JsonPropertyName("max")]
    public int Max { get; set; }
}
