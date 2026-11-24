using UnityEngine;

namespace PirateGame.Networking
{
    [CreateAssetMenu(menuName = "Networking/Steam Network Settings")]
    public sealed class SteamNetworkSettings : ScriptableObject
    {
        [Tooltip("480 is Spacewar, for development tests only. Replace before release.")]
        [SerializeField] private uint appId = 480;
        [SerializeField] private string gameKey = "untitled-pirate-game-m0a";
        [Min(1)] [SerializeField] private int protocolVersion = 1;
        [Min(5)] [SerializeField] private float connectionTimeoutSeconds = 25f;
        [SerializeField] private bool restartThroughSteamInRelease = true;

        public uint AppId => appId;
        public string GameKey => gameKey;
        public int ProtocolVersion => protocolVersion;
        public float ConnectionTimeoutSeconds => connectionTimeoutSeconds;
        public bool RestartThroughSteamInRelease => restartThroughSteamInRelease;
        public bool IsValid => appId != 0 && !string.IsNullOrWhiteSpace(gameKey)
            && protocolVersion > 0 && connectionTimeoutSeconds >= 5f;
    }
}
