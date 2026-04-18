using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using ZombieHorde.Client.Patches;

namespace ZombieHorde.Client
{
    [BepInPlugin("com.vonbraunz.roamingzombies", "Roaming Zombies", "1.1.1")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;

        // F12 ConfigurationManager-visible settings.
        // Values are read live — changes take effect without a raid restart.
        public static ConfigEntry<bool>  EnableMeleeDamage;
        public static ConfigEntry<float> MeleeDamage;
        public static ConfigEntry<float> MeleeRange;
        public static ConfigEntry<float> MeleeCooldown;
        public static ConfigEntry<bool>  EnableHordeNotification;
        public static ConfigEntry<bool>  EnableHordeAudio;

        private void Awake()
        {
            Log = Logger;

            BindConfig();

            new NightCheckPatch().Enable();
            new RaidStartPatch().Enable();
            new InfectedSidePatch().Enable();

            Logger.LogInfo("[RoamingZombies] Client loaded — patches active");
        }

        private void BindConfig()
        {
            const string melee = "1. Zombie Melee";
            const string notif = "2. Notifications";

            EnableMeleeDamage = Config.Bind(
                melee, "Enable Melee Damage", true,
                "Master toggle — when off, zombies can't hurt the player via the proximity damage system.");

            MeleeDamage = Config.Bind(
                melee, "Damage Per Hit", 5f,
                new ConfigDescription(
                    "Damage dealt per melee hit. Ignores armor — keep low.",
                    new AcceptableValueRange<float>(1f, 50f)));

            MeleeRange = Config.Bind(
                melee, "Melee Range (m)", 2f,
                new ConfigDescription(
                    "Distance in meters at which a zombie can land a hit.",
                    new AcceptableValueRange<float>(1f, 10f)));

            MeleeCooldown = Config.Bind(
                melee, "Attack Interval (s)", 1.5f,
                new ConfigDescription(
                    "Seconds between hits from the same zombie.",
                    new AcceptableValueRange<float>(0.25f, 5f)));

            EnableHordeNotification = Config.Bind(
                notif, "Show Horde Spawn Alert", true,
                "Display a UI notification when zombies are first detected in the raid.");

            EnableHordeAudio = Config.Bind(
                notif, "Play Horde Audio", true,
                "Play horde_alert.ogg/.wav (if present in the plugin folder) when zombies are first detected.");
        }
    }
}
