using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Core
{
    [Serializable]
    public struct WeaponConfig
    {
        public bool CanFire;
        public float Damage;
        public float FireCooldownSeconds;
        public float Range;
        public float ProjectileSpeed;
        public float GravityScale;
        public float SplashRadius;
        public bool CanTargetStructures;
        public bool CanTargetAirships;
        public bool PreferHighArc;
        public float MinAimAngleDegrees;
        public float MaxAimAngleDegrees;

        public static WeaponConfig None => new()
        {
            CanFire = false,
            Damage = 0f,
            FireCooldownSeconds = 10f,
            Range = 0f,
            ProjectileSpeed = 16f,
            GravityScale = 1f,
            SplashRadius = 0f,
            CanTargetStructures = false,
            CanTargetAirships = false,
            PreferHighArc = false,
            MinAimAngleDegrees = 5f,
            MaxAimAngleDegrees = 85f
        };
    }

    public static class ManualAimMath
    {
        public const float DefaultMinBarrelAngle = 5f;
        public const float DefaultMaxBarrelAngle = 175f;

        public static float ClampBarrelAngle(float angleDegrees)
        {
            float wrapped = Mathf.Repeat(angleDegrees + 180f, 360f) - 180f;
            if (wrapped < -90f)
            {
                return DefaultMaxBarrelAngle;
            }

            return Mathf.Clamp(wrapped, DefaultMinBarrelAngle, DefaultMaxBarrelAngle);
        }

        public static float StepBarrelAngle(float currentAngleDegrees, Vector2 aimMoveInput, float angularSpeedDegPerSec, float dt)
        {
            float current = ClampBarrelAngle(currentAngleDegrees);
            if (dt <= 0f || aimMoveInput.sqrMagnitude <= 0.0001f)
            {
                return current;
            }

            float delta = -aimMoveInput.x * angularSpeedDegPerSec * dt;
            float elevSign = current <= 90f ? 1f : -1f;
            delta += aimMoveInput.y * elevSign * angularSpeedDegPerSec * dt;
            return ClampBarrelAngle(current + delta);
        }

        public static float DirectionToBarrelAngle(Vector2 direction)
        {
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return 45f;
            }

            float deg = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            return ClampBarrelAngle(deg);
        }

        public static Vector2 BarrelAngleToVelocity(float angleDegrees, float speed)
        {
            float rad = ClampBarrelAngle(angleDegrees) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad) * speed, Mathf.Sin(rad) * speed);
        }
    }

    /// <summary>
    /// Ensures confirming 'Use' on a weapon does not immediately fire on the same press/hold;
    /// requires a full button release followed by a fresh rising-edge press.
    /// </summary>
    public sealed class WeaponFireInputGate
    {
        private bool _waitingForRelease;

        public bool IsWaitingForRelease => _waitingForRelease;

        public void LockUntilRelease()
        {
            _waitingForRelease = true;
        }

        public void Reset()
        {
            _waitingForRelease = false;
        }

        public bool EvaluateFireInput(bool firePressed, bool fireHeld, bool confirmPressed)
        {
            if (_waitingForRelease)
            {
                if (!fireHeld && !firePressed && !confirmPressed)
                {
                    _waitingForRelease = false;
                }

                return false;
            }

            return firePressed;
        }
    }

    public readonly struct CombatTargetCandidate
    {
        public readonly TeamId Team;
        public readonly bool IsAlive;
        public readonly bool IsAirship;
        public readonly Vector2 Position;
        public readonly int TargetIndex;

        public CombatTargetCandidate(TeamId team, bool isAlive, bool isAirship, Vector2 position, int targetIndex)
        {
            Team = team;
            IsAlive = isAlive;
            IsAirship = isAirship;
            Position = position;
            TargetIndex = targetIndex;
        }
    }

    public static class WeaponTargetEvaluator
    {
        public static bool TrySelectBestTarget(
            Vector2 origin,
            TeamId sourceTeam,
            in WeaponConfig config,
            IReadOnlyList<CombatTargetCandidate> candidates,
            out CombatTargetCandidate bestTarget)
        {
            bestTarget = default;

            if (!config.CanFire || sourceTeam == TeamId.Neutral || candidates == null || config.Range <= 0f)
            {
                return false;
            }

            float maxRangeSq = config.Range * config.Range;
            float bestDistSq = float.MaxValue;
            int bestIndex = int.MaxValue;
            bool found = false;

            for (int i = 0; i < candidates.Count; i++)
            {
                CombatTargetCandidate candidate = candidates[i];
                if (!candidate.IsAlive || candidate.Team == TeamId.Neutral || candidate.Team == sourceTeam)
                {
                    continue;
                }

                if (candidate.IsAirship || !config.CanTargetStructures)
                {
                    continue;
                }

                float distSq = (candidate.Position - origin).sqrMagnitude;
                if (distSq > maxRangeSq)
                {
                    continue;
                }

                if (!found || distSq < bestDistSq - 0.0001f || (Mathf.Abs(distSq - bestDistSq) <= 0.0001f && candidate.TargetIndex < bestIndex))
                {
                    found = true;
                    bestDistSq = distSq;
                    bestIndex = candidate.TargetIndex;
                    bestTarget = candidate;
                }
            }

            return found;
        }
    }

    public static class HqMatchOutcomeEvaluator
    {
        public static bool TryEvaluate(
            bool hasTeamAHq,
            bool teamAHqAlive,
            bool hasTeamBHq,
            bool teamBHqAlive,
            out MatchOutcome outcome)
        {
            if (!hasTeamAHq && !hasTeamBHq)
            {
                outcome = default;
                return false;
            }

            bool teamADestroyed = hasTeamAHq && !teamAHqAlive;
            bool teamBDestroyed = hasTeamBHq && !teamBHqAlive;

            if (teamADestroyed && teamBDestroyed)
            {
                outcome = new MatchOutcome(TeamId.Neutral, "İki üs aynı anda yıkıldı.");
                return true;
            }

            if (teamBDestroyed)
            {
                outcome = new MatchOutcome(TeamId.TeamA, "Kırmızı Üs yıkıldı!");
                return true;
            }

            if (teamADestroyed)
            {
                outcome = new MatchOutcome(TeamId.TeamB, "Mavi Üs yıkıldı!");
                return true;
            }

            outcome = default;
            return false;
        }
    }
}
