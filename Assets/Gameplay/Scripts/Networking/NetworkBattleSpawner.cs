
using System.Collections.Generic;
using UnityEngine;

using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;

using PirateGame.Networking;

[RequireComponent(typeof(NetworkManager))]
public class NetworkBattleSpawner : MonoBehaviour
{
    [Header("Network Prefabs")]
    [SerializeField] private NetworkObject blueZeppelinPrefab;
    [SerializeField] private NetworkObject redZeppelinPrefab;

    [Header("Spawn Positions")]
    [SerializeField] private Vector3 blueSpawnPosition =
        new Vector3(-7.2f, 0.8f, 0f);

    [SerializeField] private Vector3 redSpawnPosition =
        new Vector3(7.2f, 0.8f, 0f);

    private NetworkManager manager;
    private OnlineSessionCoordinator coordinator;

    private readonly HashSet<int> spawnedClients =
        new HashSet<int>();

    private void Start()
    {
        manager = GetComponent<NetworkManager>();
        coordinator = GetComponent<OnlineSessionCoordinator>();

        if (manager == null ||
            coordinator == null ||
            blueZeppelinPrefab == null ||
            redZeppelinPrefab == null)
        {
            Debug.LogError(
                "[BattleSpawner] Missing references.");

            enabled = false;
            return;
        }

        manager.SceneManager.OnClientLoadedStartScenes +=
            OnClientLoadedStartScenes;

        manager.ServerManager.OnRemoteConnectionState +=
            OnRemoteConnectionState;

        manager.ServerManager.OnServerConnectionState +=
            OnServerConnectionState;
    }

    private void OnClientLoadedStartScenes(
        NetworkConnection connection,
        bool asServer)
    {
        if (!asServer || !manager.ServerManager.Started)
            return;

        if (!connection.IsActive ||
            !connection.IsAuthenticated)
            return;

        if (spawnedClients.Contains(connection.ClientId))
            return;

        // Identity comes from the authenticated transport.
        string address = manager.TransportManager
            .Transport.GetConnectionAddress(
                connection.ClientId);

        if (!ulong.TryParse(address, out ulong steamId))
        {
            Debug.LogError(
                "[BattleSpawner] Invalid Steam identity.");

            return;
        }

        bool isHost =
            steamId == coordinator.Steam.UserId;

        NetworkObject prefab = isHost
            ? blueZeppelinPrefab
            : redZeppelinPrefab;

        Vector3 position = isHost
            ? blueSpawnPosition
            : redSpawnPosition;

        // Both players start in the same test scene.
        if (!connection.Scenes.Contains(gameObject.scene))
        {
            manager.SceneManager.AddConnectionToScene(
                connection, gameObject.scene);
        }

        NetworkObject zeppelin = Instantiate(
            prefab,
            position,
            Quaternion.identity);

        manager.ServerManager.Spawn(
            zeppelin,
            connection);

        spawnedClients.Add(connection.ClientId);

        Debug.Log(
            $"[BattleSpawner] Spawned " +
            $"{(isHost ? "BLUE" : "RED")} zeppelin. " +
            $"Client: {connection.ClientId}");
    }

    private void OnRemoteConnectionState(
        NetworkConnection connection,
        RemoteConnectionStateArgs args)
    {
        if (args.ConnectionState !=
            RemoteConnectionState.Stopped)
            return;

        spawnedClients.Remove(args.ConnectionId);
    }

    private void OnServerConnectionState(
        ServerConnectionStateArgs args)
    {
        if (args.ConnectionState ==
            LocalConnectionState.Stopped)
        {
            spawnedClients.Clear();
        }
    }

    private void OnDestroy()
    {
        if (manager == null)
            return;

        manager.SceneManager.OnClientLoadedStartScenes -=
            OnClientLoadedStartScenes;

        manager.ServerManager.OnRemoteConnectionState -=
            OnRemoteConnectionState;

        manager.ServerManager.OnServerConnectionState -=
            OnServerConnectionState;

        spawnedClients.Clear();
    }
}
