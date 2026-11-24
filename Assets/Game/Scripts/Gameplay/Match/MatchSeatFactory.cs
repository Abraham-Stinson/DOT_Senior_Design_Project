using FishNet.Connection;
using FishNet.Managing;
using Game.Core;
using UnityEngine;

namespace Game.Gameplay
{
    public static class MatchSeatFactory
    {
        public static void SpawnSeatsServer(
            NetworkManager manager,
            PlayerSeatController seatPrefab,
            MatchMode mode,
            NetworkConnection hostConnection,
            NetworkConnection remoteConnection,
            MatchContext context,
            int startingGold)
        {
            if (manager == null || !manager.IsServerStarted || seatPrefab == null)
            {
                return;
            }

            context?.ClearSeats();

            SpawnSingleSeat(manager, seatPrefab, 0, TeamId.TeamA, SeatControlType.HumanLocal, hostConnection, context, startingGold);

            switch (mode)
            {
                case MatchMode.LocalPvP:
                    // Local PvP routes both Player 1 and Player 2 through the single local host connection.
                    SpawnSingleSeat(manager, seatPrefab, 1, TeamId.TeamB, SeatControlType.HumanLocal, hostConnection, context, startingGold);
                    break;
                case MatchMode.OnlinePvP:
                    SpawnSingleSeat(manager, seatPrefab, 1, TeamId.TeamB, SeatControlType.HumanRemote, remoteConnection, context, startingGold);
                    break;
                case MatchMode.Story:
                    SpawnSingleSeat(manager, seatPrefab, 1, TeamId.EnemyAI, SeatControlType.AI, null, context, startingGold);
                    break;
            }
        }

        private static void SpawnSingleSeat(
            NetworkManager manager,
            PlayerSeatController seatPrefab,
            byte seatId,
            TeamId teamId,
            SeatControlType controlType,
            NetworkConnection ownerConnection,
            MatchContext context,
            int startingGold)
        {
            PlayerSeatController instance = Object.Instantiate(seatPrefab);
            instance.name = $"PlayerSeat_{seatId}_{teamId}";
            manager.ServerManager.Spawn(instance.NetworkObject, ownerConnection);
            instance.InitializeSeatServer(seatId, teamId, controlType, startingGold);
            context?.RegisterSeat(instance);
        }
    }
}
