using Game.Core;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Samples solid 2D arena ground colliders while ignoring airships and deployed structures,
    /// with a deterministic fallback elevation for empty/headless test scenes.
    /// </summary>
    public sealed class ArenaGroundTerrainService : ITerrainService
    {
        private readonly Rect _playableBounds;
        private readonly float _fallbackGroundY;

        public ArenaGroundTerrainService(Rect playableBounds, float? fallbackGroundY = null)
        {
            _playableBounds = playableBounds;
            _fallbackGroundY = fallbackGroundY ?? playableBounds.yMin;
        }

        public float SampleGroundSurfaceY(float worldX)
        {
            Vector2 origin = new Vector2(worldX, _playableBounds.yMax + 2f);
            float distance = _playableBounds.height + 10f;
            RaycastHit2D[] hits = Physics2D.RaycastAll(origin, Vector2.down, distance);

            bool found = false;
            float highestY = float.MinValue;
            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D col = hits[i].collider;
                if (col == null || col.isTrigger)
                {
                    continue;
                }

                if (col.GetComponentInParent<IStructure>() != null
                    || col.GetComponentInParent<AirshipController>() != null
                    || col.GetComponentInParent<DockableAnchorNode>() != null)
                {
                    continue;
                }

                if (hits[i].point.y > _playableBounds.yMax)
                {
                    continue;
                }

                if (!found || hits[i].point.y > highestY)
                {
                    found = true;
                    highestY = hits[i].point.y;
                }
            }

            return found ? highestY : _fallbackGroundY;
        }

        public bool IsSolid(Vector2 worldPoint)
        {
            float surfaceY = SampleGroundSurfaceY(worldPoint.x);
            return worldPoint.y <= surfaceY + 0.05f;
        }

        public bool HasGroundSupport(Bounds footprint, float minSupportRatio)
        {
            if (footprint.min.x < _playableBounds.xMin - 0.1f
                || footprint.max.x > _playableBounds.xMax + 0.1f)
            {
                return false;
            }

            float leftX = Mathf.Min(footprint.center.x, footprint.min.x + 0.1f);
            float centerX = footprint.center.x;
            float rightX = Mathf.Max(footprint.center.x, footprint.max.x - 0.1f);

            int supported = 0;
            if (IsSampleSupported(leftX, footprint.min.y)) supported++;
            if (IsSampleSupported(centerX, footprint.min.y)) supported++;
            if (IsSampleSupported(rightX, footprint.min.y)) supported++;

            float ratio = supported / 3f;
            return ratio >= Mathf.Clamp01(minSupportRatio);
        }

        private bool IsSampleSupported(float worldX, float bottomY)
        {
            float surfaceY = SampleGroundSurfaceY(worldX);
            return Mathf.Abs(bottomY - surfaceY) <= 0.35f;
        }
    }
}
