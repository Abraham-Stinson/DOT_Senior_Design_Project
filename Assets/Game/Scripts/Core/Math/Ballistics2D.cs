using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// Deterministic 2D ballistic integrator, launch solver, splash damage falloff,
    /// and continuous segment hit-testing helpers (no scene or FishNet dependencies).
    /// </summary>
    public static class Ballistics2D
    {
        public const float DefaultGravity = 9.81f;

        public static void StepProjectile(ref Vector2 position, ref Vector2 velocity, float gravity, float dt)
        {
            if (dt <= 0f)
            {
                return;
            }

            velocity.y -= gravity * dt;
            position += velocity * dt;
        }

        public static bool TrySolveLaunchVelocity(
            Vector2 origin,
            Vector2 target,
            float speed,
            float gravity,
            bool preferHighArc,
            float minAngleDegrees,
            float maxAngleDegrees,
            out Vector2 launchVelocity,
            out float angleDegrees)
        {
            if (speed <= 0.01f)
            {
                launchVelocity = Vector2.zero;
                angleDegrees = 0f;
                return false;
            }

            Vector2 delta = target - origin;
            float absDx = Mathf.Abs(delta.x);
            float dirX = delta.x >= 0f ? 1f : -1f;

            if (gravity <= 0.01f || absDx < 0.05f)
            {
                Vector2 dir = delta.sqrMagnitude > 0.0001f ? delta.normalized : new Vector2(dirX, 0f);
                launchVelocity = dir * speed;
                angleDegrees = Mathf.Atan2(launchVelocity.y, launchVelocity.x) * Mathf.Rad2Deg;
                return true;
            }

            float minElev = Mathf.Clamp(Mathf.Min(minAngleDegrees, maxAngleDegrees), -85f, 89f);
            float maxElev = Mathf.Clamp(Mathf.Max(minAngleDegrees, maxAngleDegrees), minElev, 89f);

            float v2 = speed * speed;
            float v4 = v2 * v2;
            float x = absDx;
            float y = delta.y;
            float discriminant = v4 - gravity * (gravity * x * x + 2f * y * v2);

            if (discriminant < 0f)
            {
                // Beyond exact range for this speed: fall back to optimal ~45 deg arc clamped within elevation bounds.
                float fallbackElev = Mathf.Clamp(45f, minElev, maxElev);
                float fallbackRad = fallbackElev * Mathf.Deg2Rad;
                launchVelocity = new Vector2(dirX * Mathf.Cos(fallbackRad) * speed, Mathf.Sin(fallbackRad) * speed);
                angleDegrees = Mathf.Atan2(launchVelocity.y, launchVelocity.x) * Mathf.Rad2Deg;
                return false;
            }

            float sqrtDisc = Mathf.Sqrt(discriminant);
            float denom = gravity * x;
            float lowDeg = Mathf.Atan((v2 - sqrtDisc) / denom) * Mathf.Rad2Deg;
            float highDeg = Mathf.Atan((v2 + sqrtDisc) / denom) * Mathf.Rad2Deg;

            float primaryDeg = preferHighArc ? highDeg : lowDeg;
            float secondaryDeg = preferHighArc ? lowDeg : highDeg;

            float chosenDeg;
            if (primaryDeg >= minElev && primaryDeg <= maxElev)
            {
                chosenDeg = primaryDeg;
            }
            else if (secondaryDeg >= minElev && secondaryDeg <= maxElev)
            {
                chosenDeg = secondaryDeg;
            }
            else
            {
                chosenDeg = Mathf.Clamp(primaryDeg, minElev, maxElev);
            }

            float chosenRad = chosenDeg * Mathf.Deg2Rad;
            launchVelocity = new Vector2(dirX * Mathf.Cos(chosenRad) * speed, Mathf.Sin(chosenRad) * speed);
            angleDegrees = Mathf.Atan2(launchVelocity.y, launchVelocity.x) * Mathf.Rad2Deg;
            return true;
        }

        public static float ComputeSplashDamage(float baseDamage, float distance, float splashRadius)
        {
            if (baseDamage <= 0f)
            {
                return 0f;
            }

            if (splashRadius <= 0.0001f)
            {
                return distance <= 0.0001f ? baseDamage : 0f;
            }

            if (distance > splashRadius)
            {
                return 0f;
            }

            float t = Mathf.Clamp01(Mathf.Max(0f, distance) / splashRadius);
            return baseDamage * Mathf.Lerp(1f, 0.35f, t);
        }

        public static bool TrySegmentIntersectsRect(
            Vector2 p0,
            Vector2 p1,
            Vector2 rectMin,
            Vector2 rectMax,
            out float hitT,
            out Vector2 hitPoint)
        {
            Vector2 d = p1 - p0;
            float tMin = 0f;
            float tMax = 1f;

            if (!ClipSegmentAxis(p0.x, d.x, rectMin.x, rectMax.x, ref tMin, ref tMax)
                || !ClipSegmentAxis(p0.y, d.y, rectMin.y, rectMax.y, ref tMin, ref tMax))
            {
                hitT = 0f;
                hitPoint = p0;
                return false;
            }

            hitT = Mathf.Clamp01(tMin);
            hitPoint = Vector2.Lerp(p0, p1, hitT);
            return true;
        }

        public static bool TrySegmentIntersectsCircle(
            Vector2 p0,
            Vector2 p1,
            Vector2 center,
            float radius,
            out float hitT,
            out Vector2 hitPoint)
        {
            float r = Mathf.Max(0f, radius);
            Vector2 m = p0 - center;
            float rSq = r * r;

            if (m.sqrMagnitude <= rSq)
            {
                hitT = 0f;
                hitPoint = p0;
                return true;
            }

            Vector2 d = p1 - p0;
            float a = Vector2.Dot(d, d);
            if (a <= 1e-8f)
            {
                hitT = 0f;
                hitPoint = p0;
                return false;
            }

            float b = Vector2.Dot(m, d);
            float c = Vector2.Dot(m, m) - rSq;

            if (c > 0f && b > 0f)
            {
                hitT = 0f;
                hitPoint = p0;
                return false;
            }

            float disc = b * b - a * c;
            if (disc < 0f)
            {
                hitT = 0f;
                hitPoint = p0;
                return false;
            }

            float t = (-b - Mathf.Sqrt(disc)) / a;
            if (t < 0f)
            {
                t = 0f;
            }

            if (t > 1f)
            {
                hitT = 0f;
                hitPoint = p0;
                return false;
            }

            hitT = t;
            hitPoint = p0 + d * t;
            return true;
        }

        public static float DistancePointToRect(Vector2 point, Vector2 rectMin, Vector2 rectMax)
        {
            float cx = Mathf.Clamp(point.x, rectMin.x, rectMax.x);
            float cy = Mathf.Clamp(point.y, rectMin.y, rectMax.y);
            return Vector2.Distance(point, new Vector2(cx, cy));
        }

        private static bool ClipSegmentAxis(float origin, float delta, float min, float max, ref float tMin, ref float tMax)
        {
            if (Mathf.Abs(delta) < 1e-6f)
            {
                return origin >= min && origin <= max;
            }

            float inv = 1f / delta;
            float t1 = (min - origin) * inv;
            float t2 = (max - origin) * inv;
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
            }

            tMin = Mathf.Max(tMin, t1);
            tMax = Mathf.Min(tMax, t2);
            return tMin <= tMax;
        }
    }
}
