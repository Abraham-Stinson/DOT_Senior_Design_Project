using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Gameplay
{
    public static class DockingEvaluator
    {
        public static bool TryFindBestDockable(
            Vector2 airshipPosition,
            byte seatId,
            TeamId team,
            float maxRange,
            IReadOnlyList<DockableRegistration> candidates,
            out DockableRegistration bestCandidate)
        {
            bestCandidate = null;
            if (candidates == null || maxRange <= 0f)
            {
                return false;
            }

            float maxDistSq = maxRange * maxRange;
            float bestDistSq = float.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                DockableRegistration candidate = candidates[i];
                if (candidate == null || candidate.UnityObject == null || candidate.Dockable == null)
                {
                    continue;
                }

                if (candidate.Dockable is IDamageable damageable && !damageable.IsAlive)
                {
                    continue;
                }

                if (!candidate.Dockable.CanDock(seatId, team))
                {
                    continue;
                }

                float distSq = (candidate.Dockable.DockAnchorWorld - airshipPosition).sqrMagnitude;
                if (distSq <= maxDistSq && distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestCandidate = candidate;
                }
            }

            return bestCandidate != null;
        }

        public static bool IsTargetStillValid(DockableRegistration target)
        {
            if (target == null || target.UnityObject == null || target.Dockable == null)
            {
                return false;
            }

            if (target.NetworkObject != null && !target.NetworkObject.IsSpawned)
            {
                return false;
            }

            if (target.Dockable is IDamageable damageable && !damageable.IsAlive)
            {
                return false;
            }

            return true;
        }
    }
}
