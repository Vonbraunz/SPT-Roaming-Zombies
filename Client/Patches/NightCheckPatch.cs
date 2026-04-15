using System.Reflection;
using Comfort.Common;
using EFT;
using SPT.Reflection.Patching;
using UnityEngine;

namespace ZombieHorde.Client.Patches
{
    public class NightCheckPatch : ModulePatch
    {
        private const int NightStartHour = 22;
        private const int NightEndHour = 5;

        protected override MethodBase GetTargetMethod()
        {
            return typeof(GameWorld).GetMethod("OnGameStarted", BindingFlags.Public | BindingFlags.Instance);
        }

        [PatchPrefix]
        public static void PatchPrefix()
        {
            var gameWorld = Singleton<GameWorld>.Instance;
            if (gameWorld == null) return;

            // Factory maps ignore the clock entirely
            var locationId = gameWorld.MainPlayer?.Location?.ToLower() ?? "";
            if (locationId == "factory4_night")
            {
                Debug.Log("[RoamingZombies] factory4_night — always night");
                return;
            }
            if (locationId == "factory4_day")
            {
                Debug.Log("[RoamingZombies] factory4_day — always day");
                return;
            }

            if (gameWorld.GameDateTime == null)
            {
                Debug.LogWarning("[RoamingZombies] GameDateTime is null — skipping night check");
                return;
            }

            int hour = gameWorld.GameDateTime.Calculate().Hour;
            bool isNight = hour >= NightStartHour || hour < NightEndHour;
            Debug.Log($"[RoamingZombies] Raid time: {hour:D2}:00 — {(isNight ? "night" : "day")}");
        }
    }
}
