using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Gameplay
{
    public sealed class ArenaLayoutConfig : MonoBehaviour
    {
        [SerializeField] private Rect _playableAirspace = new(-18f, -2f, 36f, 16f);

        [Header("Team A Spawns")]
        [SerializeField] private Transform _teamAHqSpawn;
        [SerializeField] private Transform _teamAAirshipSpawn;
        [SerializeField] private Transform _teamATerritoryOrigin;

        [Header("Team B Spawns")]
        [SerializeField] private Transform _teamBHqSpawn;
        [SerializeField] private Transform _teamBAirshipSpawn;
        [SerializeField] private Transform _teamBTerritoryOrigin;

        [Header("Neutral Arena Anchors")]
        [SerializeField] private Transform _centerNeutralNode;
        [SerializeField] private Transform[] _goldDepositSpawns;

        public Rect PlayableAirspace => _playableAirspace;
        public Transform CenterNeutralNode => _centerNeutralNode;
        public IReadOnlyList<Transform> GoldDepositSpawns => _goldDepositSpawns;

        public Transform GetHqSpawn(TeamId team)
        {
            switch (team)
            {
                case TeamId.TeamA: return _teamAHqSpawn;
                case TeamId.TeamB:
                case TeamId.EnemyAI: return _teamBHqSpawn;
                default: return null;
            }
        }

        public Transform GetAirshipSpawn(TeamId team)
        {
            switch (team)
            {
                case TeamId.TeamA: return _teamAAirshipSpawn;
                case TeamId.TeamB:
                case TeamId.EnemyAI: return _teamBAirshipSpawn;
                default: return null;
            }
        }

        public Transform GetTerritoryOrigin(TeamId team)
        {
            switch (team)
            {
                case TeamId.TeamA: return _teamATerritoryOrigin;
                case TeamId.TeamB:
                case TeamId.EnemyAI: return _teamBTerritoryOrigin;
                default: return null;
            }
        }

        public bool IsValidLayout()
        {
            return _playableAirspace.width > 0f
                && _playableAirspace.height > 0f
                && _teamAHqSpawn != null
                && _teamAAirshipSpawn != null
                && _teamATerritoryOrigin != null
                && _teamBHqSpawn != null
                && _teamBAirshipSpawn != null
                && _teamBTerritoryOrigin != null
                && _centerNeutralNode != null;
        }

        public void ConfigureForSetup(
            Rect playableAirspace,
            Transform teamAHqSpawn,
            Transform teamAAirshipSpawn,
            Transform teamATerritoryOrigin,
            Transform teamBHqSpawn,
            Transform teamBAirshipSpawn,
            Transform teamBTerritoryOrigin,
            Transform centerNeutralNode)
        {
            _playableAirspace = playableAirspace;
            _teamAHqSpawn = teamAHqSpawn;
            _teamAAirshipSpawn = teamAAirshipSpawn;
            _teamATerritoryOrigin = teamATerritoryOrigin;
            _teamBHqSpawn = teamBHqSpawn;
            _teamBAirshipSpawn = teamBAirshipSpawn;
            _teamBTerritoryOrigin = teamBTerritoryOrigin;
            _centerNeutralNode = centerNeutralNode;
        }

        public void SetGoldDepositSpawns(Transform[] spawns)
        {
            _goldDepositSpawns = spawns;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            Vector3 center = new Vector3(_playableAirspace.center.x, _playableAirspace.center.y, 0f);
            Vector3 size = new Vector3(_playableAirspace.width, _playableAirspace.height, 0.1f);
            Gizmos.DrawWireCube(center, size);

            DrawPointGizmo(_teamAHqSpawn, new Color(0.25f, 0.6f, 1f), 0.7f);
            DrawPointGizmo(_teamAAirshipSpawn, new Color(0.55f, 0.85f, 1f), 0.55f);
            DrawPointGizmo(_teamBHqSpawn, new Color(1f, 0.35f, 0.35f), 0.7f);
            DrawPointGizmo(_teamBAirshipSpawn, new Color(1f, 0.65f, 0.55f), 0.55f);
            DrawPointGizmo(_centerNeutralNode, Color.yellow, 0.5f);
        }

        private static void DrawPointGizmo(Transform target, Color color, float radius)
        {
            if (target == null)
            {
                return;
            }

            Gizmos.color = color;
            Gizmos.DrawWireSphere(target.position, radius);
        }
    }
}
