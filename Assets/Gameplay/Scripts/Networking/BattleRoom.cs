using System;
using System.Linq;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing.Scened;
using FishNet.Object;
using FishNet.Transporting;
using PirateGame.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PirateGame.Gameplay
{
    public struct PreparationRequest : IBroadcast
    {
        public ulong Lobby;
        public ushort Cards;
        public string Map;
        public bool Ready;
    }

    public struct PreparationState : IBroadcast
    {
        public ulong Lobby;
        public string Map, Match;
        public int Blue, Red;
        public ushort BlueCards, RedCards;
        public bool BlueReady, RedReady, Active;
        public bool CanLaunch => Lobby != 0 && Map == BattleLaunchContext.DefaultMap && Blue >= 0 && Red >= 0 && Blue != Red
            && BlueReady && RedReady && BattleCards.Valid(BlueCards) && BattleCards.Valid(RedCards) && string.IsNullOrEmpty(Match);
    }

    public sealed class BattleRoom : MonoBehaviour
    {
        public OnlineSessionCoordinator network;
        public BattleLaunchService launch;
        public NetworkObject bluePrefab, redPrefab;
        public PreparationState View { get; private set; }
        public bool CanStart => !launch.Busy && network.Session.IsServer && network.Lobby?.IsOwner == true
            && network.Lobby.Members.Count == 2 && Auth(blue) && Auth(red) && authority.CanLaunch;
        public bool LocalReady => network.Session.IsServer ? View.BlueReady : View.RedReady;
        public event Action Changed;
        PreparationState authority;
        NetworkConnection blue, red;
        float deadline;
        bool bluePresent, redPresent;
        FishNet.Managing.NetworkManager Manager => network.Session.Manager;
        ulong Lobby => network.Lobby?.LobbyId ?? 0;

        void Start()
        {
            Manager.ServerManager.RegisterBroadcast<PreparationRequest>(ReceiveRequest, true);
            Manager.ClientManager.RegisterBroadcast<PreparationState>(ReceiveState);
            Manager.SceneManager.OnClientPresenceChangeEnd += Presence;
            Manager.ServerManager.OnRemoteConnectionState += Remote;
        }
        void OnDestroy()
        {
            if (!network || !network.Session || !Manager) return;
            Manager.ServerManager.UnregisterBroadcast<PreparationRequest>(ReceiveRequest);
            Manager.ClientManager.UnregisterBroadcast<PreparationState>(ReceiveState);
            Manager.SceneManager.OnClientPresenceChangeEnd -= Presence;
            Manager.ServerManager.OnRemoteConnectionState -= Remote;
        }
        static bool Auth(NetworkConnection c) => c != null && c.IsActive && c.IsAuthenticated;
        void Update()
        {
            if (Lobby == 0 || !network.Session.IsAuthenticated)
            {
                if (View.Lobby != 0) { View = default; Changed?.Invoke(); }
                authority = default; blue = red = null;
                return;
            }
            if (!network.Session.IsServer) return;
            RefreshRoster();
            if (!string.IsNullOrEmpty(authority.Match) && !authority.Active && Time.unscaledTime > deadline)
                launch.Leave();
        }
        void Remote(NetworkConnection c, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped) return;
            if (!ReferenceEquals(c,red) && !ReferenceEquals(c,blue)) return;
            if (!string.IsNullOrEmpty(authority.Match)) launch.RequestLeave();
            if (ReferenceEquals(c,red))
            {
                red = null;
                authority.Red = -1;
                authority.RedCards = BattleCards.Starters;
            }
            else { blue = null; authority.Blue = -1; }
            authority.BlueReady = authority.RedReady = false;
            if (string.IsNullOrEmpty(authority.Match)) Publish();
        }
        void RefreshRoster()
        {
            if (authority.Lobby != Lobby)
                authority = new PreparationState { Lobby = Lobby, Map = BattleLaunchContext.DefaultMap, Blue = -1, Red = -1,
                    BlueCards = BattleCards.Starters, RedCards = BattleCards.Starters };
            NetworkConnection host = Manager.ClientManager.Connection;
            Manager.ServerManager.Clients.TryGetValue(host.ClientId, out var local);
            var guest = Manager.ServerManager.Clients.Values.FirstOrDefault(c => Auth(c) && c.ClientId != host.ClientId);
            if (!Auth(local)) local = null;
            if (ReferenceEquals(local,blue) && ReferenceEquals(guest,red)) return;
            if (!string.IsNullOrEmpty(authority.Match)) { launch.RequestLeave(); return; }
            if (!ReferenceEquals(guest,red)) authority.RedCards = BattleCards.Starters;
            blue = local; red = guest; authority.Blue = blue?.ClientId ?? -1; authority.Red = red?.ClientId ?? -1;
            authority.BlueReady = authority.RedReady = false;
            Publish();
        }
        void Publish()
        {
            if (Auth(blue)) Manager.ServerManager.Broadcast(blue, authority);
            if (Auth(red)) Manager.ServerManager.Broadcast(red, authority);
        }
        public void Submit(ushort hand, bool ready)
        {
            if (network.Session.IsAuthenticated && View.Lobby == Lobby && Lobby != 0)
                Manager.ClientManager.Broadcast(new PreparationRequest { Lobby = Lobby, Map = View.Map, Cards = hand, Ready = ready });
        }
        void ReceiveRequest(NetworkConnection c, PreparationRequest req, Channel channel)
        {
            if (!Auth(c) || req.Lobby != Lobby || req.Map != authority.Map || !string.IsNullOrEmpty(authority.Match)
                || !BattleCards.Valid(req.Cards)) return;
            if (ReferenceEquals(c,blue))
            {
                authority.BlueReady = req.Ready && authority.BlueCards == req.Cards;
                authority.BlueCards = req.Cards;
            }
            else if (ReferenceEquals(c,red))
            {
                authority.RedReady = req.Ready && authority.RedCards == req.Cards;
                authority.RedCards = req.Cards;
            }
            else return;
            Publish();
        }
        public void SelectMap(string map)
        {
            if (!network.Session.IsServer || !string.IsNullOrEmpty(authority.Match) || map != BattleLaunchContext.DefaultMap || map == authority.Map) return;
            authority.Map = map; authority.BlueReady = authority.RedReady = false; Publish();
        }
        public void StartMatch()
        {
            if (!network.Session.IsServer || network.Lobby?.IsOwner != true || launch.Busy) return;
            RefreshRoster();
            if (!CanStart) return;
            authority.Match = Guid.NewGuid().ToString("N");
            bluePresent = redPresent = false;
            deadline = Time.unscaledTime + 45;
            Apply(authority);
            Publish();
            try { Manager.SceneManager.LoadConnectionScenes(new[]{blue,red}, new SceneLoadData(authority.Map)); }
            catch (Exception ex) { Debug.LogException(ex); launch.RequestLeave(); }
        }
        void ReceiveState(PreparationState state, Channel channel)
        {
            if (state.Lobby != Lobby || state.Map != BattleLaunchContext.DefaultMap) return;
            Apply(state);
        }
        void Apply(PreparationState state)
        {
            View = state;
            if (!string.IsNullOrEmpty(state.Match) && !launch.Busy)
                launch.AcceptOnline(new BattleLaunchContext(BattleMode.OnlinePvP,0,0,state.BlueCards,state.RedCards,
                    state.Lobby,state.Match,state.Blue,state.Red,state.Map));
            if (state.Active) launch.ActivateOnline(state.Match);
            Changed?.Invoke();
        }
        void Presence(ClientPresenceChangeEventArgs e)
        {
            if (!e.Added || e.Scene.name != authority.Map || string.IsNullOrEmpty(authority.Match) || authority.Active
                || !Auth(blue) || !Auth(red)) return;
            if (ReferenceEquals(e.Connection,blue)) bluePresent = true;
            if (ReferenceEquals(e.Connection,red)) redPresent = true;
            if (!bluePresent || !redPresent) return;
            var field = e.Scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<BattleSessionBootstrap>(true)).FirstOrDefault();
            if (!field || !bluePrefab || !redPrefab) { launch.RequestLeave(); return; }
            Spawn(bluePrefab,blue,field.BluePosition,e.Scene);
            Spawn(redPrefab,red,field.RedPosition,e.Scene);
            authority.Active = true;
            Publish();
        }
        void Spawn(NetworkObject prefab, NetworkConnection owner, Vector3 position, Scene scene)
        {
            var actor = Instantiate(prefab,position,Quaternion.identity);
            Manager.ServerManager.Spawn(actor,owner,scene);
        }
    }
}
