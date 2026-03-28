using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using SPT.Reflection.Patching;
using HarmonyLib;
using UnityEngine;

namespace ZombieHorde.Client.Patches
{
    public class NightCheckPatch : ModulePatch
    {
        private const int NightStartHour = 22;
        private const int NightEndHour = 6;

        private static readonly string[] ZombieBossNames =
        {
            "infectedAssault",
            "infectedPmc",
            "infectedCivil",
            "infectedLaborant"
        };

        protected override MethodBase GetTargetMethod()
        {
            return typeof(GameWorld).GetMethod("OnGameStarted", BindingFlags.Public | BindingFlags.Instance);
        }

        [PatchPrefix]
        public static void PatchPrefix()
        {
            var gameWorld = Singleton<GameWorld>.Instance;
            if (gameWorld == null) return;

            if (gameWorld.GameDateTime == null)
            {
                Debug.LogWarning("[RoamingZombies] GameDateTime is null — skipping night check");
                return;
            }

            int hour = gameWorld.GameDateTime.Calculate().Hour;
            bool isNight = hour >= NightStartHour || hour < NightEndHour;

            if (isNight)
            {
                Debug.Log($"[RoamingZombies] Night raid detected (hour={hour}) — zombies will spawn");
                return;
            }

            Debug.Log($"[RoamingZombies] Daytime raid detected (hour={hour}) — removing zombie spawns");

            // Remove zombie BossLocationSpawn entries from the location data
            // Access the location base via reflection since the runtime property name may be obfuscated
            var locationField = typeof(GameWorld).GetField("Location",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (locationField == null)
            {
                // Try common obfuscated patterns
                foreach (var field in typeof(GameWorld).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (field.FieldType.Name.Contains("LocationSettingsClass") ||
                        field.FieldType.Name.Contains("LocationBase") ||
                        field.FieldType.FullName.Contains("Location"))
                    {
                        locationField = field;
                        break;
                    }
                }
            }

            if (locationField != null)
            {
                var location = locationField.GetValue(gameWorld);
                if (location != null)
                {
                    RemoveZombieSpawns(location);
                    return;
                }
            }

            // Fallback: search all fields for a BossLocationSpawn list
            foreach (var field in typeof(GameWorld).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                var val = field.GetValue(gameWorld);
                if (val == null) continue;

                foreach (var prop in val.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (prop.Name == "BossLocationSpawn" && prop.PropertyType.IsGenericType)
                    {
                        RemoveFromList(prop.GetValue(val));
                        Debug.Log("[RoamingZombies] Removed zombie spawns via fallback search");
                        return;
                    }
                }
            }

            Debug.LogWarning("[RoamingZombies] Could not find BossLocationSpawn list — zombie spawns may still occur in daytime");
        }

        private static void RemoveZombieSpawns(object location)
        {
            // Walk the location object to find BossLocationSpawn
            var type = location.GetType();

            // Check direct property
            var bossSpawnProp = type.GetProperty("BossLocationSpawn",
                BindingFlags.Public | BindingFlags.Instance);

            if (bossSpawnProp == null)
            {
                // Check for a Base or LocationBase sub-property
                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    var sub = prop.GetValue(location);
                    if (sub == null) continue;
                    bossSpawnProp = sub.GetType().GetProperty("BossLocationSpawn",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (bossSpawnProp != null)
                    {
                        RemoveFromList(bossSpawnProp.GetValue(sub));
                        Debug.Log("[RoamingZombies] Removed zombie spawns from location base");
                        return;
                    }
                }
            }
            else
            {
                RemoveFromList(bossSpawnProp.GetValue(location));
                Debug.Log("[RoamingZombies] Removed zombie spawns from location");
            }
        }

        private static void RemoveFromList(object listObj)
        {
            if (listObj == null) return;

            // Use reflection to iterate and remove zombie entries
            var listType = listObj.GetType();
            var countProp = listType.GetProperty("Count");
            if (countProp == null) return;

            int count = (int)countProp.GetValue(listObj);
            var indexer = listType.GetProperty("Item");
            var removeAt = listType.GetMethod("RemoveAt");

            if (indexer == null || removeAt == null) return;

            for (int i = count - 1; i >= 0; i--)
            {
                var item = indexer.GetValue(listObj, new object[] { i });
                if (item == null) continue;

                var bossNameProp = item.GetType().GetProperty("BossName",
                    BindingFlags.Public | BindingFlags.Instance);
                if (bossNameProp == null) continue;

                var bossName = bossNameProp.GetValue(item) as string;
                if (bossName != null && ZombieBossNames.Contains(bossName))
                {
                    removeAt.Invoke(listObj, new object[] { i });
                    Debug.Log($"[RoamingZombies] Removed zombie spawn: {bossName}");
                }
            }
        }
    }
}
