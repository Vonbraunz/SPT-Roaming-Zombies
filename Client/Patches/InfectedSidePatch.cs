using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

namespace ZombieHorde.Client.Patches
{
    /// <summary>
    /// Forces infected/zombie bots to return EPlayerSide.Savage from their Side property
    /// so that the damage system treats them as hostile (same faction as scavs).
    ///
    /// Targets Player.Side (not BotOwner.Side) because the damage pipeline resolves
    /// the shooter as an IPlayer/Player reference — BotOwner is the AI controller
    /// and its Side getter is never consulted during hit processing.
    /// </summary>
    public class InfectedSidePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(Player).GetProperty(nameof(Player.Side))?.GetGetMethod(true);
        }

        [PatchPostfix]
        private static void PatchPostfix(Player __instance, ref EPlayerSide __result)
        {
            try
            {
                var role = __instance?.Profile?.Info?.Settings?.Role;
                if (role == null) return;

                switch (role)
                {
                    case WildSpawnType.infectedAssault:
                    case WildSpawnType.infectedPmc:
                    case WildSpawnType.infectedCivil:
                    case WildSpawnType.infectedLaborant:
                    case WildSpawnType.infectedTagilla:
                        __result = EPlayerSide.Savage;
                        break;
                }
            }
            catch
            {
                // Fail silently — original value is preserved
            }
        }
    }
}
