using System;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Game.Core;
using UnityEngine;

namespace Game.Gameplay
{
    public static class ActiveTerritoryControllerRegistry
    {
        public static TerritoryController Current { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            Current = null;
        }

        public static void Register(TerritoryController controller)
        {
            if (controller != null)
            {
                Current = controller;
            }
        }

        public static void Unregister(TerritoryController controller)
        {
            if (ReferenceEquals(Current, controller))
            {
                Current = null;
            }
        }
    }

    /// <summary>
    /// Replicated server-authoritative territory zones (HQ origins + Territory Balloon expansions)
    /// with pooled circular disc and perimeter ring visuals.
    /// </summary>
    public sealed class TerritoryController : NetworkBehaviour, ITerritoryService
    {
        private readonly SyncList<TerritoryZone> _zones = new();
        private readonly List<TerritoryCircleVisual> _zoneVisuals = new();
        private Transform _visualRoot;

        public IReadOnlyList<TerritoryZone> Zones => _zones;

        public event Action TerritoryChanged;

        private void Awake()
        {
            _zones.OnChange += HandleZonesChanged;
        }

        private void OnEnable()
        {
            ActiveTerritoryControllerRegistry.Register(this);
        }

        private void OnDisable()
        {
            ActiveTerritoryControllerRegistry.Unregister(this);
        }

        private void OnDestroy()
        {
            _zones.OnChange -= HandleZonesChanged;
            _zoneVisuals.Clear();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ActiveArenaBootstrapRegistry.FindFor(NetworkManager)?.RegisterReplicatedTerritoryController(this);
            RefreshZoneVisuals();
        }

        public override void OnStopClient()
        {
            ActiveArenaBootstrapRegistry.FindFor(NetworkManager)?.UnregisterReplicatedTerritoryController(this);
            base.OnStopClient();
        }

        public void InitializeServer(
            MatchContext context,
            Vector2 teamAOrigin,
            Vector2 teamBOrigin,
            float initialRadius = 8.5f)
        {
            if (!IsServerInitialized || context == null)
            {
                return;
            }

            _zones.Clear();
            float clampedRadius = Mathf.Max(0f, initialRadius);
            if (clampedRadius > 0f)
            {
                _zones.Add(new TerritoryZone(-1, TeamId.TeamA, teamAOrigin, clampedRadius));
                _zones.Add(new TerritoryZone(-2, TeamId.TeamB, teamBOrigin, clampedRadius));
            }

            context.Territory = this;
            RefreshZoneVisuals();
        }

        public bool IsInsideTeamTerritory(TeamId team, Vector2 worldPoint)
        {
            return TerritoryMath.IsPointInsideTeamTerritory(team, worldPoint, _zones);
        }

        public int GetZoneCount(TeamId team)
        {
            if (team == TeamId.Neutral)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < _zones.Count; i++)
            {
                if (_zones[i].Team == team && _zones[i].Radius > 0f)
                {
                    count++;
                }
            }

            return count;
        }

        public bool TryGetHorizontalExtent(TeamId team, out float minX, out float maxX)
        {
            return TerritoryMath.TryGetCombinedHorizontalExtent(team, _zones, out minX, out maxX);
        }

        public void AddOrUpdateZoneServer(int sourceId, TeamId team, Vector2 center, float radius)
        {
            if (!IsServerInitialized || team == TeamId.Neutral || radius <= 0f)
            {
                return;
            }

            var zone = new TerritoryZone(sourceId, team, center, radius);
            for (int i = 0; i < _zones.Count; i++)
            {
                if (_zones[i].SourceId == sourceId)
                {
                    _zones[i] = zone;
                    return;
                }
            }

            _zones.Add(zone);
        }

        public void RemoveZoneServer(int sourceId)
        {
            if (!IsServerInitialized)
            {
                return;
            }

            for (int i = _zones.Count - 1; i >= 0; i--)
            {
                if (_zones[i].SourceId == sourceId)
                {
                    _zones.RemoveAt(i);
                }
            }
        }

        private void HandleZonesChanged(
            SyncListOperation op,
            int index,
            TerritoryZone oldItem,
            TerritoryZone newItem,
            bool asServer)
        {
            if (asServer && (IsHostInitialized || (NetworkManager != null && NetworkManager.IsClientStarted)))
            {
                return;
            }

            RefreshZoneVisuals();
            TerritoryChanged?.Invoke();
        }

        private void RefreshZoneVisuals()
        {
            if (_visualRoot == null)
            {
                var rootGo = new GameObject("TerritoryZoneVisuals");
                rootGo.transform.SetParent(transform, false);
                _visualRoot = rootGo.transform;
            }

            int activeVisualCount = 0;
            for (int i = 0; i < _zones.Count; i++)
            {
                TerritoryZone zone = _zones[i];
                if (zone.Team == TeamId.Neutral || zone.Radius <= 0.001f)
                {
                    continue;
                }

                TerritoryCircleVisual visual = GetOrCreateZoneVisual(activeVisualCount);
                activeVisualCount++;

                Color teamColor = zone.Team == TeamId.TeamA
                    ? new Color(0.25f, 0.62f, 0.96f)
                    : new Color(0.96f, 0.36f, 0.34f);

                visual.Configure(zone.Center, zone.Radius, teamColor, fillAlpha: 0.11f, borderAlpha: 0.72f, borderWidth: 0.07f);
            }

            for (int i = activeVisualCount; i < _zoneVisuals.Count; i++)
            {
                if (_zoneVisuals[i] != null)
                {
                    _zoneVisuals[i].gameObject.SetActive(false);
                }
            }
        }

        private TerritoryCircleVisual GetOrCreateZoneVisual(int index)
        {
            while (_zoneVisuals.Count <= index)
            {
                var go = new GameObject($"ZoneCircle_{_zoneVisuals.Count}");
                go.transform.SetParent(_visualRoot, false);
                _zoneVisuals.Add(go.AddComponent<TerritoryCircleVisual>());
            }

            if (_zoneVisuals[index] == null)
            {
                var go = new GameObject($"ZoneCircle_{index}");
                go.transform.SetParent(_visualRoot, false);
                _zoneVisuals[index] = go.AddComponent<TerritoryCircleVisual>();
            }

            return _zoneVisuals[index];
        }
    }
}
