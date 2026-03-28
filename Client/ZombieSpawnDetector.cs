using System.Collections.Generic;
using System.IO;
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

        private GameWorld _gameWorld;
        private bool _hordeDetected;
        private AudioClip _hordeClip;

        public static void Init()
        {
            if (Singleton<GameWorld>.Instantiated)
                Singleton<GameWorld>.Instance.GetOrAddComponent<ZombieSpawnDetector>();
        }

        private void Start()
        {
            _gameWorld = Singleton<GameWorld>.Instance;
            _hordeDetected = false;

            // Subscribe to new bot spawns
            _gameWorld.OnPersonAdd += OnPersonAdd;

            // Also check bots already present at raid start
            foreach (var player in _gameWorld.AllAlivePlayersList)
                CheckPlayer(player as Player);

            // Load custom audio
            LoadAudioClip();
        }

        private void OnPersonAdd(IPlayer iPlayer)
        {
            CheckPlayer(iPlayer as Player);
        }

        private void CheckPlayer(Player player)
        {
            if (player == null || player.IsYourPlayer) return;
            if (_hordeDetected) return;

            var role = player.Profile?.Info?.Settings?.Role;
            if (role == null) return;

            if (ZombieTypes.Contains(role.ToString()))
            {
                _hordeDetected = true;
                OnHordeSpawned();
            }
        }

        private void OnHordeSpawned()
        {
            // HUD notification
            NotificationManagerClass.DisplayMessageNotification(
                "The dead are rising... zombies have been spotted nearby.",
                ENotificationDurationType.Long);

            // Custom sound
            if (_hordeClip != null)
            {
                PlayClip(_hordeClip);
            }
            else
            {
                Debug.LogWarning("[RoamingZombies] No custom audio clip loaded — notification only");
            }
        }

        private void PlayClip(AudioClip clip)
        {
            var audioObj = new GameObject("ZombieHorde_Audio");
            var source = audioObj.AddComponent<AudioSource>();
            source.clip = clip;
            source.volume = 1.0f;
            source.spatialBlend = 0f; // 2D sound (not positional)
            source.Play();

            // Destroy after clip finishes
            Destroy(audioObj, clip.length + 0.5f);
        }

        private void LoadAudioClip()
        {
            // Look for horde_alert.ogg or horde_alert.wav next to the plugin DLL
            var dllPath = Assembly.GetExecutingAssembly().Location;
            var pluginDir = Path.GetDirectoryName(dllPath);

            string audioPath = null;
            foreach (var ext in new[] { "ogg", "wav" })
            {
                var candidate = Path.Combine(pluginDir, $"horde_alert.{ext}");
                if (File.Exists(candidate))
                {
                    audioPath = candidate;
                    break;
                }
            }

            if (audioPath == null)
            {
                Debug.Log("[RoamingZombies] No horde_alert.ogg/.wav found in plugin folder — sound disabled");
                return;
            }

            StartCoroutine(LoadAudioCoroutine(audioPath));
        }

        private System.Collections.IEnumerator LoadAudioCoroutine(string filePath)
        {
            var audioType = filePath.EndsWith(".ogg") ? AudioType.OGGVORBIS : AudioType.WAV;
            var uri = "file:///" + filePath.Replace("\\", "/");

            using (var request = UnityWebRequestMultimedia.GetAudioClip(uri, audioType))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.ConnectionError ||
                    request.result == UnityWebRequest.Result.ProtocolError)
                {
                    Debug.LogError($"[RoamingZombies] Failed to load audio: {request.error}");
                    yield break;
                }

                _hordeClip = DownloadHandlerAudioClip.GetContent(request);
                Debug.Log($"[RoamingZombies] Loaded horde alert sound: {filePath}");
            }
        }

        private void OnDestroy()
        {
            if (_gameWorld != null)
                _gameWorld.OnPersonAdd -= OnPersonAdd;
        }
    }
}
