using System;
using System.Collections;
using FishNet.Managing.Scened;
using PirateGame.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;
using SceneManager = UnityEngine.SceneManagement.SceneManager;

namespace PirateGame.Gameplay
{
    // Lives in MainMenu beside the existing session. MainMenu remains loaded for the entire match.
    public sealed class BattleLaunchService : MonoBehaviour
    {
        public OnlineSessionCoordinator network;
        public Camera menuCamera;
        public GameObject menuScreens;
        public BattleLaunchContext Context { get; private set; }
        public bool Busy => Context != null;
        public bool IsActive => Busy && !exiting && SceneManager.GetSceneByName(Context.MapId).isLoaded
            && (Context.Mode != BattleMode.OnlinePvP || onlineActive);
        public bool IsAuthority => IsActive && (Context.Mode != BattleMode.OnlinePvP || network.Session.IsServer);
        public event Action Returned;
        bool onlineActive;
        internal void ActivateOnline(string matchId)
        {
            if (Busy && Context.Mode == BattleMode.OnlinePvP && Context.MatchId == matchId) onlineActive = true;
        }
        bool exiting, leaveRequested, sceneOperation;
        float loadDeadline;
        AsyncOperation offlineLoad;
        void Start()
        {
            var scenes = network.Session.Manager.SceneManager;
            scenes.OnLoadStart += LoadStarted;
            scenes.OnLoadEnd += LoadEnded;
            scenes.OnUnloadStart += UnloadStarted;
            scenes.OnUnloadEnd += UnloadEnded;
        }
        // FishNet clears its queue on server stop without necessarily raising OnQueueEnd.
        void LoadStarted(SceneLoadStartEventArgs args) => sceneOperation = true;
        void LoadEnded(SceneLoadEndEventArgs args) => sceneOperation = false;
        void UnloadStarted(SceneUnloadStartEventArgs args) => sceneOperation = true;
        void UnloadEnded(SceneUnloadEndEventArgs args) => sceneOperation = false;
        void OnDestroy()
        {
            if (!network || !network.Session || !network.Session.Manager) return;
            var scenes = network.Session.Manager.SceneManager;
            scenes.OnLoadStart -= LoadStarted;
            scenes.OnLoadEnd -= LoadEnded;
            scenes.OnUnloadStart -= UnloadStarted;
            scenes.OnUnloadEnd -= UnloadEnded;
        }
        public void LaunchOffline(BattleLaunchContext context)
        {
            if (Busy || context.Mode == BattleMode.OnlinePvP) return;
            Begin(context);
            try
            {
                offlineLoad = SceneManager.LoadSceneAsync(Context.MapId, LoadSceneMode.Additive);
                offlineLoad.completed += OfflineLoaded;
            }
            catch (Exception ex) { Debug.LogError(ex.Message); Leave(); }
        }
        void OfflineLoaded(AsyncOperation operation)
        {
            operation.completed -= OfflineLoaded;
            if (!Busy || exiting) return;
            var level = SceneManager.GetSceneByName(Context.MapId);
            if (level.isLoaded) SceneManager.SetActiveScene(level);
        }
        public void AcceptOnline(BattleLaunchContext context)
        {
            if (!Busy && context.Mode == BattleMode.OnlinePvP) Begin(context);
        }
        void Begin(BattleLaunchContext context)
        {
            Context = context;
            loadDeadline = Time.unscaledTime + 50;
            menuScreens.SetActive(false);
            menuCamera.gameObject.SetActive(false);
        }
        void Update()
        {
            if (!Busy || exiting) return;
            bool online = Context.Mode == BattleMode.OnlinePvP;
            if (leaveRequested || online && (!network.Session.IsAuthenticated || network.Lobby?.LobbyId != Context.LobbyId)
                || Time.unscaledTime > loadDeadline && !SceneManager.GetSceneByName(Context.MapId).isLoaded)
                Leave();
        }
        public void RequestLeave() { if (Busy) leaveRequested = true; }
        public void Leave()
        {
            if (Busy && !exiting) StartCoroutine(Close());
        }
        IEnumerator Close()
        {
            exiting = true;
            if (Context.Mode == BattleMode.OnlinePvP) network.LeaveLobby();
            if (offlineLoad != null) yield return offlineLoad;
            yield return null;
            while (sceneOperation) yield return null;
            var level = SceneManager.GetSceneByName(Context.MapId);
            if (level.isLoaded)
            {
                SceneManager.SetActiveScene(gameObject.scene);
                var unload = SceneManager.UnloadSceneAsync(level);
                if (unload != null) yield return unload;
            }
            Context = null; onlineActive = false; offlineLoad = null; exiting = leaveRequested = false;
            menuCamera.gameObject.SetActive(true);
            menuScreens.SetActive(true);
            Returned?.Invoke();
        }
    }
}
