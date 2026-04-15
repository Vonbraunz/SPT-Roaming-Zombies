using BepInEx;
using BepInEx.Logging;
using ZombieHorde.Client.Patches;

namespace ZombieHorde.Client
{
    [BepInPlugin("com.vonbraunz.roamingzombies", "Roaming Zombies", "1.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;

            new NightCheckPatch().Enable();
            new RaidStartPatch().Enable();
            new InfectedSidePatch().Enable();

            Logger.LogInfo("[RoamingZombies] Client loaded — patches active");
        }
    }
}
