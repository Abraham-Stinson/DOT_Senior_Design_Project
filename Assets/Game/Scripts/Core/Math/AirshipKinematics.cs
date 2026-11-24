using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// Pure deterministic 2D kinematic movement step for airships (no dynamic Rigidbody2D drift).
    /// </summary>
    public static class AirshipKinematics
    {
        public static void Step2D(
            ref Vector2 position,
            ref Vector2 velocity,
            Vector2 moveInput,
            float maxSpeed,
            float acceleration,
            float drag,
            float deltaTime,
            Rect bounds)
        {
            if (deltaTime <= 0f)
            {
                ClampToBounds(ref position, ref velocity, bounds);
                return;
            }

            Vector2 clampedInput = Vector2.ClampMagnitude(moveInput, 1f);
            if (clampedInput.sqrMagnitude > 0.0001f)
            {
                Vector2 targetVelocity = clampedInput * Mathf.Max(0f, maxSpeed);
                velocity = Vector2.MoveTowards(velocity, targetVelocity, Mathf.Max(0f, acceleration) * deltaTime);
            }
            else
            {
                velocity = Vector2.MoveTowards(velocity, Vector2.zero, Mathf.Max(0f, drag) * deltaTime);
            }

            position += velocity * deltaTime;
            ClampToBounds(ref position, ref velocity, bounds);
        }

        public static void ClampToBounds(ref Vector2 position, ref Vector2 velocity, Rect bounds)
        {
            if (bounds.width <= 0f || bounds.height <= 0f)
            {
                return;
            }

            if (position.x <= bounds.xMin)
            {
                position.x = bounds.xMin;
                if (velocity.x < 0f) velocity.x = 0f;
            }
            else if (position.x >= bounds.xMax)
            {
                position.x = bounds.xMax;
                if (velocity.x > 0f) velocity.x = 0f;
            }

            if (position.y <= bounds.yMin)
            {
                position.y = bounds.yMin;
                if (velocity.y < 0f) velocity.y = 0f;
            }
            else if (position.y >= bounds.yMax)
            {
                position.y = bounds.yMax;
                if (velocity.y > 0f) velocity.y = 0f;
            }
        }
    }
}
