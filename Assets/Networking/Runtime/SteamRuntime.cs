using System;
using Steamworks;
using UnityEngine;

namespace PirateGame.Networking
{
    [DefaultExecutionOrder(-30000)]
    [DisallowMultipleComponent]
    public sealed class SteamRuntime : MonoBehaviour
    {
        [SerializeField] private SteamNetworkSettings settings;
        private static SteamRuntime instance;
        private bool ownsApi;
        private bool closing;
        private float nextLoginCheck;
        public bool IsReady { get; private set; }
        public string UserName { get; private set; } = "-";
        public ulong UserId { get; private set; }
        public string Status { get; private set; } = "Steam başlatılıyor.";
        public string LastError { get; private set; } = "";
        public SteamNetworkSettings Settings => settings;
        public event Action Changed;
        public event Action BeforeShutdown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { instance = null; }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Status = LastError = "İkinci SteamRuntime başlatılamaz.";
                enabled = false;
                return;
            }
            instance = this;
            TryInitialize();
        }

        public bool TryInitialize()
        {
            if (IsReady) return true;
            if (instance != this || closing) return false;
            LastError = "";
            try
            {
                if (settings == null || !settings.IsValid)
                    return Fail("Steam Network Settings eksik veya geçersiz.");
                if (!Packsize.Test()) return Fail("Steamworks.NET veri yapısı boyutları uyumsuz.");
                if (!SteamAPI.IsSteamRunning())
                    return Fail("Steam açık değil. Steam'i açın, hesabınıza giriş yapın ve Retry Steam'e basın.");

                if (Application.isEditor || Debug.isDebugBuild)
                {
                    // The ScriptableObject is authoritative. No duplicated App ID in code.
                    string id = settings.AppId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    Environment.SetEnvironmentVariable("SteamAppId", id);
                    Environment.SetEnvironmentVariable("SteamGameId", id);
                }
                else if (settings.RestartThroughSteamInRelease
                    && SteamAPI.RestartAppIfNecessary(new AppId_t(settings.AppId)))
                {
                    Status = "Oyun Steam üzerinden yeniden başlatılıyor.";
                    Application.Quit();
                    return false;
                }

                var result = SteamAPI.InitEx(out string details);
                if (result != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
                    return Fail($"Steam API başlatılamadı ({result}). Steam oturumunu, App ID'yi ve steam_api64.dll dosyasını kontrol edin. {details}");
                ownsApi = true;
                if (SteamUtils.GetAppID().m_AppId != settings.AppId)
                    return Fail("Steam App ID ile merkezî ayar uyuşmuyor. Steam'i ve oyunu aynı kullanıcıyla başlatın.");
                if (!SteamUser.BLoggedOn()) return Fail("Steam kullanıcısı çevrimiçi oturum açmamış. Steam'e giriş yapıp yeniden deneyin.");
                UserId = SteamUser.GetSteamID().m_SteamID;
                UserName = SteamFriends.GetPersonaName();
                IsReady = true;
                Status = "Steam hazır; kullanıcı oturumu açık.";
                nextLoginCheck = Time.unscaledTime + 1f;
                Changed?.Invoke();
                Debug.Log("[M0-A] Steam API ready.");
                return true;
            }
            catch (DllNotFoundException ex) { return Fail("Steam native kütüphanesi yüklenemedi: " + ex.Message); }
            catch (BadImageFormatException ex) { return Fail("Steam native kütüphanesinin işlemci mimarisi uyumsuz: " + ex.Message); }
            catch (Exception ex) { return Fail("Steam başlatma hatası: " + ex.Message); }
        }

        private bool Fail(string error)
        {
            Shutdown();
            Status = LastError = error;
            Changed?.Invoke();
            Debug.LogWarning("[M0-A] " + error);
            return false;
        }

        private void Update()
        {
            if (!ownsApi || !IsReady) return;
            try
            {
                SteamAPI.RunCallbacks();
                if (Time.unscaledTime < nextLoginCheck) return;
                nextLoginCheck = Time.unscaledTime + 1f;
                if (!SteamAPI.IsSteamRunning() || !SteamUser.BLoggedOn())
                    Fail("Steam bağlantısı veya kullanıcı oturumu kapandı. Yeniden giriş yapıp Retry Steam'e basın.");
                else UserName = SteamFriends.GetPersonaName();
            }
            catch (Exception ex) { Fail("Steam callback hatası: " + ex.Message); }
        }

        public void Shutdown()
        {
            if (!ownsApi || closing) return;
            closing = true;
            try { BeforeShutdown?.Invoke(); }
            finally
            {
                SteamAPI.Shutdown();
                ownsApi = false;
                IsReady = false;
                UserId = 0;
                UserName = "-";
                closing = false;
                Status = "Steam API kapatıldı.";
                Changed?.Invoke();
                Debug.Log("[M0-A] Steam API shutdown completed.");
            }
        }

        private void OnDisable() { Shutdown(); }
        private void OnDestroy() { Shutdown(); if (instance == this) instance = null; }
        private void OnApplicationQuit() { Shutdown(); }
    }
}
