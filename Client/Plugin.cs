using BepInEx;
using ZombieHorde.Client.Patches;

namespace ZombieHorde.Client
{
    [BepInPlugin("com.vonbraunz.roamingzombies", "Roaming Zombies", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        private void Awake()
        {
            new NightCheckPatch().Enable();
            new RaidStartPatch().Enable();
            Logger.LogInfo("[RoamingZombies] Client plugin loaded — night-only filter + horde detection active");
        }
    }
}
