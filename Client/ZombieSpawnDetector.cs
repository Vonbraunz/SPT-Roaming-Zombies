using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Communications;
using UnityEngine;
using UnityEngine.Networking;

namespace ZombieHorde.Client
{
    public class ZombieSpawnDetector : MonoBehaviour
    {
        private static readonly HashSet<string> ZombieTypes = new HashSet<string>
        {
            "infectedAssault",
            "infectedPmc",
            "infectedCivil",
            "infectedLaborant"
        };

        // Proximity melee settings
        private const float MeleeRange    = 4f;
        private const float MeleeDamage   = 35f;
        private const float MeleeCooldown = 1.5f;

        private GameWorld    _gameWorld;
        private bool         _hordeDetected;
        private AudioClip    _hordeClip;

        // All live zombie bots tracked for proximity melee
        private readonly List<Player>              _zombies    = new List<Player>();
        private readonly Dictionary<Player, float> _nextAttack = new Dictionary<Player, float>();
        private float _nextDistLog;

        // Reflection cache for ApplyDamage — resolved once on first use
        private static MethodInfo _applyDamageMethod;
        private static Type       _damageInfoType;
        private static FieldInfo  _fiDamage;
        private static FieldInfo  _fiDamageType;
        private static FieldInfo  _fiPlayer;
        private static FieldInfo  _fiDirection;
        private static object     _meleeEnumValue;
        private static bool       _reflectionReady;

        private static readonly EBodyPart[] BodyParts =
        {
            EBodyPart.Chest, EBodyPart.Head, EBodyPart.LeftArm, EBodyPart.RightArm
        };

        public static void Init()
        {
            if (Singleton<GameWorld>.Instantiated)
                Singleton<GameWorld>.Instance.GetOrAddComponent<ZombieSpawnDetector>();
        }

        private void Start()
        {
            _gameWorld     = Singleton<GameWorld>.Instance;
            _hordeDetected = false;

            Plugin.Log.LogInfo("[RoamingZombies] ZombieSpawnDetector started — subscribing to OnPersonAdd");
            _gameWorld.OnPersonAdd += OnPersonAdd;

            foreach (var player in _gameWorld.AllAlivePlayersList)
                CheckPlayer(player as Player);

            LoadAudioClip();
        }

        private void OnPersonAdd(IPlayer iPlayer)
        {
            CheckPlayer(iPlayer as Player);
        }

        private void CheckPlayer(Player player)
        {
            if (player == null || player.IsYourPlayer) return;

            var role = player.Profile?.Info?.Settings?.Role;
            Plugin.Log.LogInfo($"[RoamingZombies] CheckPlayer: {player.name} role={role}");
            if (role == null) return;
            if (!ZombieTypes.Contains(role.ToString())) return;

            FixInfectedSide(player);

            if (!_zombies.Contains(player))
                _zombies.Add(player);

            if (!_hordeDetected)
            {
                _hordeDetected = true;
                OnHordeSpawned();
            }
        }

        private void Update()
        {
            var main = _gameWorld?.MainPlayer;
            if (main == null || !main.HealthController.IsAlive) return;

            // Remove dead / null zombies
            _zombies.RemoveAll(z => z == null || !z.HealthController.IsAlive);

            // Periodic distance log so we can see how close zombies are getting
            if (Time.time >= _nextDistLog && _zombies.Count > 0)
            {
                _nextDistLog = Time.time + 3f;
                float minDist = float.MaxValue;
                foreach (var z in _zombies)
                    minDist = Mathf.Min(minDist, Vector3.Distance(z.Transform.position, main.Transform.position));
                Plugin.Log.LogInfo($"[RoamingZombies] Nearest zombie: {minDist:F1}m ({_zombies.Count} tracked)");
            }

            foreach (var zombie in _zombies)
            {
                float dist = Vector3.Distance(zombie.Transform.position, main.Transform.position);
                if (dist > MeleeRange) continue;

                float now = Time.time;
                if (_nextAttack.TryGetValue(zombie, out float next) && now < next) continue;

                _nextAttack[zombie] = now + MeleeCooldown;

                Plugin.Log.LogDebug($"[RoamingZombies] Zombie in range ({dist:F1}m) — applying {MeleeDamage} damage");
                ApplyMeleeDamage(zombie, main);
            }
        }

        private static void ApplyMeleeDamage(Player attacker, Player victim)
        {
            try
            {
                if (!EnsureReflection(victim)) return;

                var damageInfo = Activator.CreateInstance(_damageInfoType);

                _fiDamage?.SetValue(damageInfo, MeleeDamage);
                _fiDamageType?.SetValue(damageInfo, _meleeEnumValue);

                // Player field expects IPlayerOwner — only set if the type is compatible
                if (_fiPlayer != null && _fiPlayer.FieldType.IsInstanceOfType(attacker))
                    _fiPlayer.SetValue(damageInfo, attacker);

                if (_fiDirection != null)
                {
                    var dir = (victim.Transform.position - attacker.Transform.position).normalized;
                    _fiDirection.SetValue(damageInfo, dir);
                }

                var bodyPart = BodyParts[UnityEngine.Random.Range(0, BodyParts.Length)];
                _applyDamageMethod.Invoke(
                    victim.ActiveHealthController,
                    new[] { (object)bodyPart, MeleeDamage, damageInfo });
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[RoamingZombies] ApplyMeleeDamage failed: {ex.Message}");
            }
        }

        private static bool EnsureReflection(Player victim)
        {
            if (_reflectionReady) return _applyDamageMethod != null;
            _reflectionReady = true;

            try
            {
                var hc = victim.ActiveHealthController;
                if (hc == null) return false;

                _applyDamageMethod = hc.GetType()
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m =>
                        m.Name == "ApplyDamage" &&
                        m.GetParameters().Length == 3 &&
                        m.GetParameters()[0].ParameterType == typeof(EBodyPart));

                if (_applyDamageMethod == null)
                {
                    Plugin.Log.LogWarning("[RoamingZombies] ApplyDamage(EBodyPart,float,*) not found on health controller");
                    return false;
                }

                _damageInfoType = _applyDamageMethod.GetParameters()[2].ParameterType;
                Plugin.Log.LogInfo($"[RoamingZombies] DamageInfo type resolved: {_damageInfoType.FullName}");

                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var allFields = _damageInfoType.GetFields(flags);

                // Damage: float field whose name contains "Damage" but NOT "Type"
                _fiDamage = allFields.FirstOrDefault(fi =>
                    fi.FieldType == typeof(float) &&
                    fi.Name.IndexOf("Damage", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    fi.Name.IndexOf("Type", StringComparison.OrdinalIgnoreCase) < 0);

                // DamageType: enum field whose type name contains "DamageType"
                _fiDamageType = allFields.FirstOrDefault(fi =>
                    fi.FieldType.IsEnum &&
                    fi.FieldType.Name.IndexOf("DamageType", StringComparison.OrdinalIgnoreCase) >= 0);

                // Player: field whose type name contains "Player"
                _fiPlayer = allFields.FirstOrDefault(fi =>
                    fi.FieldType.Name.IndexOf("Player", StringComparison.OrdinalIgnoreCase) >= 0);

                // Direction: Vector3 field whose name contains "Direction"
                _fiDirection = allFields.FirstOrDefault(fi =>
                    fi.FieldType == typeof(Vector3) &&
                    fi.Name.IndexOf("Direction", StringComparison.OrdinalIgnoreCase) >= 0);

                if (_fiDamageType != null)
                    _meleeEnumValue = Enum.Parse(_fiDamageType.FieldType, "Melee", ignoreCase: true);

                Plugin.Log.LogInfo($"[RoamingZombies] Fields resolved — Damage={_fiDamage?.Name}({_fiDamage?.FieldType.Name}) DamageType={_fiDamageType?.Name}({_fiDamageType?.FieldType.Name}) Player={_fiPlayer?.Name} Direction={_fiDirection?.Name}");
                Plugin.Log.LogInfo("[RoamingZombies] Melee reflection ready");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[RoamingZombies] EnsureReflection failed: {ex.Message}");
                return false;
            }
        }

        private static void FixInfectedSide(Player player)
        {
            var info = player?.Profile?.Info;
            if (info == null) return;

            try
            {
                var prop = info.GetType().GetProperty("Side",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop?.CanWrite == true)
                {
                    prop.SetValue(info, EPlayerSide.Savage);
                    Plugin.Log.LogInfo("[RoamingZombies] Set infected Side via property");
                    return;
                }

                var field = info.GetType()
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(fi => fi.FieldType == typeof(EPlayerSide));

                if (field != null)
                {
                    field.SetValue(info, EPlayerSide.Savage);
                    Plugin.Log.LogInfo($"[RoamingZombies] Set infected Side via field '{field.Name}'");
                }
                else
                {
                    Plugin.Log.LogWarning("[RoamingZombies] FixInfectedSide: no EPlayerSide field found");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[RoamingZombies] FixInfectedSide failed: {ex.Message}");
            }
        }

        private void OnHordeSpawned()
        {
            NotificationManagerClass.DisplayMessageNotification(
                "The dead are rising... zombies have been spotted nearby.",
                ENotificationDurationType.Long);

            if (_hordeClip != null)
                PlayClip(_hordeClip);
            else
                Plugin.Log.LogWarning("[RoamingZombies] No custom audio clip loaded — notification only");
        }

        private void PlayClip(AudioClip clip)
        {
            var audioObj = new GameObject("ZombieHorde_Audio");
            var source   = audioObj.AddComponent<AudioSource>();
            source.clip         = clip;
            source.volume       = 0.5f;
            source.spatialBlend = 0f;
            source.Play();
            Destroy(audioObj, clip.length + 0.5f);
        }

        private void LoadAudioClip()
        {
            var pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string audioPath = null;
            foreach (var ext in new[] { "ogg", "wav" })
            {
                var candidate = Path.Combine(pluginDir, $"horde_alert.{ext}");
                if (File.Exists(candidate)) { audioPath = candidate; break; }
            }

            if (audioPath == null)
            {
                Plugin.Log.LogInfo("[RoamingZombies] No horde_alert audio found — sound disabled");
                return;
            }

            StartCoroutine(LoadAudioCoroutine(audioPath));
        }

        private System.Collections.IEnumerator LoadAudioCoroutine(string filePath)
        {
            var audioType = filePath.EndsWith(".ogg") ? AudioType.OGGVORBIS : AudioType.WAV;
            var uri       = "file:///" + filePath.Replace("\\", "/");

            using (var request = UnityWebRequestMultimedia.GetAudioClip(uri, audioType))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.ConnectionError ||
                    request.result == UnityWebRequest.Result.ProtocolError)
                {
                    Plugin.Log.LogError($"[RoamingZombies] Failed to load audio: {request.error}");
                    yield break;
                }

                _hordeClip = DownloadHandlerAudioClip.GetContent(request);
                Plugin.Log.LogInfo($"[RoamingZombies] Loaded horde alert: {filePath}");
            }
        }

        private void OnDestroy()
        {
            if (_gameWorld != null)
                _gameWorld.OnPersonAdd -= OnPersonAdd;
        }
    }
}
