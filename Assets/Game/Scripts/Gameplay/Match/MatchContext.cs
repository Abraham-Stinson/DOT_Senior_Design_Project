using System;
using System.Collections.Generic;
using FishNet.Object;
using Game.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Gameplay
{
    public sealed class DockableRegistration
    {
        public readonly IDockable Dockable;
        public readonly Object UnityObject;
        public readonly NetworkObject NetworkObject;
        public readonly int NetworkObjectId;

        public DockableRegistration(IDockable dockable, Object unityObject, NetworkObject networkObject)
        {
            Dockable = dockable;
            UnityObject = unityObject;
            NetworkObject = networkObject;
            NetworkObjectId = networkObject != null ? networkObject.ObjectId : -1;
        }
    }

    /// <summary>
    /// Scene-scoped composition root shared across match systems without static singletons.
    /// </summary>
    public sealed class MatchContext : IMatchRuleContext
    {
        private readonly List<PlayerSeatController> _seats = new();
        private readonly List<DockableRegistration> _dockables = new();
        private readonly List<AirshipController> _airships = new();
        private readonly List<IStructure> _structures = new();
        private readonly List<IGoldDeposit> _goldDeposits = new();
        private readonly Dictionary<TeamId, IStructure> _hqByTeam = new();

        public MatchMode Mode { get; set; }
        public ICardCatalogLookup CardCatalog { get; set; }
        public IUnlockedCardProvider LocalUnlockedCards { get; set; }
        public ITerrainService Terrain { get; set; }
        public ITerritoryService Territory { get; set; }
        public IEconomyService Economy { get; set; }
        public BallisticProjectile ProjectilePrefab { get; set; }
        public Rect PlayableAirspace { get; set; } = new(-18f, -2f, 36f, 16f);
        public Func<bool> CanPilotAirships { get; set; }
        public IReadOnlyList<PlayerSeatController> Seats => _seats;
        public IReadOnlyList<DockableRegistration> Dockables => _dockables;
        public IReadOnlyList<AirshipController> Airships => _airships;
        public IReadOnlyList<IStructure> Structures => _structures;
        public IReadOnlyList<IGoldDeposit> GoldDeposits => _goldDeposits;

        public void RegisterSeat(PlayerSeatController seat)
        {
            if (seat != null && !_seats.Contains(seat))
            {
                _seats.Add(seat);
            }
        }

        public void UnregisterSeat(PlayerSeatController seat)
        {
            if (seat != null)
            {
                _seats.Remove(seat);
            }
        }

        public bool TryGetSeat(byte seatId, out PlayerSeatController seat)
        {
            for (int i = 0; i < _seats.Count; i++)
            {
                PlayerSeatController candidate = _seats[i];
                if (candidate != null && candidate.SeatId == seatId)
                {
                    seat = candidate;
                    return true;
                }
            }

            seat = null;
            return false;
        }

        public void RegisterDockable(IDockable dockable, Object unityObject, NetworkObject networkObject)
        {
            if (dockable == null || unityObject == null)
            {
                return;
            }

            for (int i = 0; i < _dockables.Count; i++)
            {
                if (ReferenceEquals(_dockables[i].Dockable, dockable))
                {
                    return;
                }
            }

            _dockables.Add(new DockableRegistration(dockable, unityObject, networkObject));
        }

        public void UnregisterDockable(IDockable dockable)
        {
            if (dockable == null)
            {
                return;
            }

            for (int i = _dockables.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_dockables[i].Dockable, dockable) || _dockables[i].UnityObject == null)
                {
                    _dockables.RemoveAt(i);
                }
            }
        }

        public void RegisterAirship(AirshipController airship)
        {
            if (airship != null && !_airships.Contains(airship))
            {
                _airships.Add(airship);
            }
        }

        public void UnregisterAirship(AirshipController airship)
        {
            if (airship != null)
            {
                _airships.Remove(airship);
            }
        }

        public bool TryGetAirship(byte seatId, out AirshipController airship)
        {
            for (int i = 0; i < _airships.Count; i++)
            {
                AirshipController candidate = _airships[i];
                if (candidate != null && candidate.SeatId == seatId)
                {
                    airship = candidate;
                    return true;
                }
            }

            airship = null;
            return false;
        }

        public void RegisterStructure(IStructure structure)
        {
            if (structure != null && !_structures.Contains(structure))
            {
                _structures.Add(structure);
            }
        }

        public void UnregisterStructure(IStructure structure)
        {
            if (structure == null)
            {
                return;
            }

            for (int i = _structures.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_structures[i], structure) || (_structures[i] is Object uo && uo == null))
                {
                    _structures.RemoveAt(i);
                }
            }
        }

        public void RegisterGoldDeposit(IGoldDeposit deposit)
        {
            if (deposit != null && !_goldDeposits.Contains(deposit))
            {
                _goldDeposits.Add(deposit);
            }
        }

        public void UnregisterGoldDeposit(IGoldDeposit deposit)
        {
            if (deposit == null)
            {
                return;
            }

            for (int i = _goldDeposits.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_goldDeposits[i], deposit) || (_goldDeposits[i] is Object uo && uo == null))
                {
                    _goldDeposits.RemoveAt(i);
                }
            }
        }

        public void ClearSeats()
        {
            _seats.Clear();
            _dockables.Clear();
            _airships.Clear();
            _structures.Clear();
            _goldDeposits.Clear();
            _hqByTeam.Clear();
        }

        public void RegisterHq(TeamId team, IStructure hq)
        {
            if (team == TeamId.Neutral || hq == null)
            {
                return;
            }

            _hqByTeam[team] = hq;
        }

        public bool HasRegisteredHq(TeamId team)
        {
            return team != TeamId.Neutral && _hqByTeam.ContainsKey(team);
        }

        public bool TryGetHq(TeamId team, out IStructure hq)
        {
            if (team != TeamId.Neutral && _hqByTeam.TryGetValue(team, out hq) && hq != null)
            {
                if (hq is Object uo && uo == null)
                {
                    hq = null;
                    return false;
                }

                return true;
            }

            hq = null;
            return false;
        }

        public bool IsHqAlive(TeamId team)
        {
            return TryGetHq(team, out IStructure hq) && hq.IsAlive;
        }
    }
}
