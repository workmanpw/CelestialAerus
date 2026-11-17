using System;
using Microsoft.Xna.Framework;
using Terraria;

namespace CelestialAerus.Effects
{
    /// <summary>
    /// Shared homing steering for CelestialAerus projectiles (2026-11-15).
    /// Design: aggressive (fast lock-on, fast turn rate) but smooth — the
    /// turn rate is CAPPED, so the projectile always curves toward its
    /// target and can NEVER make a sudden sharp direction change. No snaps,
    /// no U-turns, no jitter: a fast, clean arc, every time.
    /// </summary>
    public static class HomingHelper
    {
        // Max turn per AI call (radians). AI runs twice per tick, so 0.15 =
        // ~18 rad/s: a full 180° reversal takes ~0.17 s — fast enough to
        // feel aggressive, slow enough to always read as a smooth curve.
        public const float DefaultMaxTurnRate = 0.15f;

        // How quickly speed eases toward the homing speed (per AI call).
        // Keeps speed changes smooth — no sudden acceleration jumps.
        private const float SpeedEase = 0.1f;

        /// <summary>
        /// Steers velocity toward targetPos, turning at most maxTurnRate
        /// radians this call and easing speed toward homingSpeed. The turn
        /// is always a smooth arc — never a snap.
        /// </summary>
        public static Vector2 SteerToward(Vector2 velocity, Vector2 from, Vector2 targetPos, float homingSpeed, float maxTurnRate = DefaultMaxTurnRate)
        {
            Vector2 toTarget = targetPos - from;
            if (toTarget == Vector2.Zero)
                return velocity;
            Vector2 desiredDir = Vector2.Normalize(toTarget);

            // Ease speed toward homingSpeed (smooth, no speed snap).
            float speed = velocity.Length();
            if (speed < 0.01f)
                speed = homingSpeed;
            else
                speed = MathHelper.Lerp(speed, homingSpeed, SpeedEase);

            // Stationary (e.g. just unstuck): launch in the desired
            // direction. Not a "turn" — there is no prior direction.
            if (velocity == Vector2.Zero)
                return desiredDir * speed;

            float currentAngle = (float)Math.Atan2(velocity.Y, velocity.X);
            float desiredAngle = (float)Math.Atan2(desiredDir.Y, desiredDir.X);
            float diff = MathHelper.WrapAngle(desiredAngle - currentAngle);
            float turn = MathHelper.Clamp(diff, -maxTurnRate, maxTurnRate);
            float newAngle = currentAngle + turn;
            return new Vector2((float)Math.Cos(newAngle), (float)Math.Sin(newAngle)) * speed;
        }

        /// <summary>
        /// Predicts where a moving target will be when the projectile
        /// arrives, so fast targets get intercepted instead of chased.
        /// Lookahead is clamped so a far, fast target can't send the
        /// projectile wildly off-course.
        /// </summary>
        public static Vector2 PredictPosition(NPC target, Vector2 from, float projectileSpeed)
        {
            float dist = Vector2.Distance(from, target.Center);
            float timeToArrive = dist / Math.Max(projectileSpeed, 0.01f);
            timeToArrive = Math.Min(timeToArrive, 30f);
            return target.Center + target.velocity * timeToArrive;
        }

        /// <summary>
        /// Finds the nearest chasable NPC within range of a point, optionally
        /// excluding up to two NPCs. Uses squared distances (no sqrt per NPC).
        /// </summary>
        public static NPC FindNearestTarget(Projectile projectile, Vector2 from, float range, NPC exclude1 = null, NPC exclude2 = null)
        {
            NPC best = null;
            float bestDistSq = range * range;
            foreach (NPC npc in Main.npc)
            {
                if (npc == exclude1 || npc == exclude2)
                    continue;
                if (!npc.CanBeChasedBy(projectile))
                    continue;
                float dSq = Vector2.DistanceSquared(from, npc.Center);
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    best = npc;
                }
            }
            return best;
        }

        /// <summary>
        /// Checks whether a locked-on target is still valid: alive, active,
        /// chasable, and within range. Used to decide when to re-scan.
        /// </summary>
        public static bool IsTargetValid(Projectile projectile, NPC target, Vector2 from, float range)
        {
            if (target == null || !target.active || target.life <= 0)
                return false;
            if (!target.CanBeChasedBy(projectile))
                return false;
            return Vector2.DistanceSquared(from, target.Center) <= range * range;
        }
    }
}
