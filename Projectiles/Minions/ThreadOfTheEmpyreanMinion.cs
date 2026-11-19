using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CelestialAerus.Projectiles
{
    /// <summary>
    /// Thread of the Empyrean weapon minion (Brigitte's Charm,
    /// 2026-11-15): the Empyrean yoyo as a living weapon. Unique
    /// attack "Empyrean Orbit": it orbits the player at 200px at a
    /// constant angular speed, damaging enemies on contact via its own
    /// hitbox (penetrate -1, local immunity frames), and every 240
    /// ticks it launches a ThreadOfTheEmpyreanProjectile at the
    /// nearest enemy in fireball mode (aiStyle = 0, timeLeft = 300 —
    /// the same trick the Aerus armor used; the yoyo's AI has a
    /// homing branch for aiStyle 0, so the launched yoyo steers
    /// itself to the enemy instead of returning). All damage is flat
    /// 6000, typeless (DamageClass.Generic), per the charm's design.
    /// </summary>
    public class ThreadOfTheEmpyreanMinion : ModProjectile
    {
        public override string Texture => "CelestialAerus/Assets/Textures/ThreadOfTheEmpyrean";

        private const float TargetRange = 1000f;
        private const int MinionDamage = 6000; // flat, typeless (charm design)
        private const float OrbitRadius = 200f;
        private const float OrbitAngularSpeed = 0.05f; // radians per tick (~1 rev / 2s)
        private const float OrbitChaseSpeed = 16f;     // max speed toward the orbit point
        private const int LaunchInterval = 240;        // ticks between yoyo launches
        private const float LaunchSpeed = 14f;
        private const int AbilityInterval = 240; // ticks between ability casts (4s)

        private NPC target;
        private int retargetTimer;
        private int attackTimer;
        private int abilityTimer;
        private int abilityIndex;
        private float orbitAngle;

        public override void SetDefaults()
        {
            Projectile.width = 40;
            Projectile.height = 40;
            Projectile.friendly = true;
            Projectile.hostile = false;
            Projectile.minion = true;
            Projectile.minionSlots = 1;
            Projectile.penetrate = -1;
            Projectile.damage = MinionDamage;
            Projectile.DamageType = DamageClass.Generic;
            Projectile.knockBack = 0f;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 20;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.timeLeft = 2; // the charm refreshes this every tick
            Projectile.netImportant = true;

            // Stagger the ability cycle so the weapon minions don't all
            // fire their abilities on the same tick. (The yoyo orbits
            // instead of hovering, so it has no hover offset to seed
            // from — 192 keeps it off the other minions' ticks.)
            abilityTimer = 192;
        }

        public override void AI()
        {
            Player owner = Main.player[Projectile.owner];
            if (owner == null || !owner.active)
            {
                Projectile.velocity *= 0.95f;
                return;
            }

            // RECALL + RAMPED FOLLOW (2026-11-18, Peyton's order): the
            // minion flies back with speed that INCREASES with distance,
            // so it catches up smoothly instead of snapping -- teleport is
            // only the last resort when it is far off-screen (2000px).
            // NOTE: the else-if is OUTSIDE the recall if-block's closing
            // brace (a previous script nested it inside and broke the
            // build -- CS8641).
            float distToOwner = Vector2.Distance(Projectile.Center, owner.Center);
            if (distToOwner > 2000f)
            {
                Projectile.Center = owner.Center;
                Projectile.velocity = Vector2.Zero;
                Projectile.netUpdate = true;
                for (int i = 0; i < 10; i++)
                    Dust.NewDust(Projectile.position, Projectile.width, Projectile.height, DustID.GoldFlame);
            }
            else if (distToOwner > 400f)
            {
                // Speed ramps with distance: 20 at 400px, 40 at 800px,
                // 60 past 1200px -- fast enough to catch a sprinting or
                // flying player, and the lerp keeps the acceleration
                // smooth instead of a sudden snap.
                float followSpeed = MathHelper.Clamp(distToOwner * 0.05f, 16f, 60f);
                Vector2 toOwner = (owner.Center - Projectile.Center).SafeNormalize(Vector2.Zero);
                Projectile.velocity = Vector2.Lerp(Projectile.velocity, toOwner * followSpeed, 0.2f);
            }

            // Empyrean Orbit: advance around the player at a constant
            // angular speed and steer toward the current orbit point.
            // The orbit runs even with no target (it IS the idle).
            orbitAngle += OrbitAngularSpeed;
            Vector2 orbitPoint = owner.Center + new Vector2((float)Math.Cos(orbitAngle), (float)Math.Sin(orbitAngle)) * OrbitRadius;
            Vector2 toOrbit = orbitPoint - Projectile.Center;
            float orbitDist = toOrbit.Length();
            if (orbitDist > 4f)
            {
                Vector2 desired = toOrbit.SafeNormalize(Vector2.Zero) * Math.Min(orbitDist * 0.25f, OrbitChaseSpeed);
                Projectile.velocity = Vector2.Lerp(Projectile.velocity, desired, 0.3f);
            }
            Projectile.rotation += 0.3f; // the yoyo spins as it orbits

            // Re-scan for a target every 15 ticks (or if the current one died).
            retargetTimer++;
            if (retargetTimer >= 15 || target == null || !target.active || !target.CanBeChasedBy())
            {
                retargetTimer = 0;
                target = FindTarget(TargetRange);
            }

            if (target == null)
            {
                attackTimer = 0;
                return;
            }

            // Launch a fireball-mode yoyo at the nearest enemy every 240 ticks.
            attackTimer++;
            if (attackTimer >= LaunchInterval)
            {
                attackTimer = 0;
                if (Projectile.owner == Main.myPlayer)
                {
                    Vector2 dir = (target.Center - Projectile.Center).SafeNormalize(Vector2.UnitX);
                    int yoyoType = ModContent.ProjectileType<ThreadOfTheEmpyrean.ThreadOfTheEmpyreanProjectile>();
                    int p = Projectile.NewProjectile(Projectile.GetSource_FromThis(), Projectile.Center, dir * LaunchSpeed, yoyoType, MinionDamage, 0f, Projectile.owner);
                    if (p >= 0 && p < Main.maxProjectiles)
                    {
                        Projectile yoyo = Main.projectile[p];
                        yoyo.friendly = true;
                        yoyo.hostile = false;
                        yoyo.DamageType = DamageClass.Generic;
                        yoyo.usesLocalNPCImmunity = true;
                        yoyo.localNPCHitCooldown = 20;
                        // Fireball mode (the Aerus armor trick): free-flying,
                        // NOT tethered — the yoyo's AI homes when aiStyle is 0.
                        yoyo.aiStyle = 0;
                        yoyo.timeLeft = 300;
                    }
                }
            }

            // Ability cycle: a new attack every 240 ticks, cycling
            // through the three abilities (only with a target).
            abilityTimer++;
            if (abilityTimer >= AbilityInterval)
            {
                abilityTimer = 0;
                if (Projectile.owner == Main.myPlayer)
                    FireAbility(abilityIndex);
                abilityIndex = (abilityIndex + 1) % 3;
            }
        }

        // The three abilities (2026-11-15, Peyton's order): each is a new
        // attack using the weapon's own projectiles, at the same flat
        // 6000 damage as everything else the charm summons.
        private void FireAbility(int index)
        {
            switch (index)
            {
                case 0:
                    EmpyreanBurst();
                    break;
                case 1:
                    ThreadSpiral();
                    break;
                default:
                    EmpyreanNova();
                    break;
            }
        }

        // (a) Empyrean Burst: launch 3 yoyos at the 3 nearest enemies,
        // in fireball mode (aiStyle = 0, timeLeft = 300 — same as the
        // primary launch, so they home instead of returning).
        private void EmpyreanBurst()
        {
            List<NPC> targets = FindTargets(3, TargetRange);
            foreach (NPC t in targets)
            {
                Vector2 dir = (t.Center - Projectile.Center).SafeNormalize(Vector2.UnitX);
                LaunchYoyo(Projectile.Center, dir * LaunchSpeed);
            }
        }

        // (b) Thread Spiral: launch 5 yoyos in a spiral — evenly
        // spaced angles, speeds rising from 12 to 16.
        private void ThreadSpiral()
        {
            float baseAngle = (target.Center - Projectile.Center).ToRotation();
            for (int i = 0; i < 5; i++)
            {
                float angle = baseAngle + i * (MathHelper.TwoPi / 5f);
                float speed = 12f + i;
                Vector2 velocity = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * speed;
                LaunchYoyo(Projectile.Center, velocity);
            }
        }

        // (c) Empyrean Nova: launch 6 yoyos in a 360-degree ring
        // around the minion, speed 12.
        private void EmpyreanNova()
        {
            for (int i = 0; i < 6; i++)
            {
                float angle = i * (MathHelper.TwoPi / 6f);
                Vector2 velocity = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * 12f;
                LaunchYoyo(Projectile.Center, velocity);
            }
        }

        // Launches a ThreadOfTheEmpyreanProjectile in fireball mode
        // (aiStyle = 0, timeLeft = 300 — the Aerus armor trick), flipped
        // friendly with local immunity frames (charm design).
        private void LaunchYoyo(Vector2 position, Vector2 velocity)
        {
            int yoyoType = ModContent.ProjectileType<ThreadOfTheEmpyrean.ThreadOfTheEmpyreanProjectile>();
            int p = Projectile.NewProjectile(Projectile.GetSource_FromThis(), position, velocity, yoyoType, MinionDamage, 0f, Projectile.owner);
            if (p >= 0 && p < Main.maxProjectiles)
            {
                Projectile yoyo = Main.projectile[p];
                yoyo.friendly = true;
                yoyo.hostile = false;
                yoyo.DamageType = DamageClass.Generic;
                yoyo.usesLocalNPCImmunity = true;
                yoyo.localNPCHitCooldown = 20;
                yoyo.aiStyle = 0;
                yoyo.timeLeft = 300;
            }
        }

        // The nearest N valid targets within range, closest first.
        private List<NPC> FindTargets(int maxCount, float maxRange)
        {
            List<NPC> found = new List<NPC>();
            float maxDistSq = maxRange * maxRange;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (npc == null || !npc.active || !npc.CanBeChasedBy())
                    continue;
                if (Vector2.DistanceSquared(Projectile.Center, npc.Center) > maxDistSq)
                    continue;
                found.Add(npc);
            }
            found.Sort((a, b) => Vector2.DistanceSquared(Projectile.Center, a.Center).CompareTo(Vector2.DistanceSquared(Projectile.Center, b.Center)));
            if (found.Count > maxCount)
                found.RemoveRange(maxCount, found.Count - maxCount);
            return found;
        }

        private NPC FindTarget(float maxRange)
        {
            NPC best = null;
            float bestDistSq = maxRange * maxRange;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (npc == null || !npc.active || !npc.CanBeChasedBy())
                    continue;
                float distSq = Vector2.DistanceSquared(Projectile.Center, npc.Center);
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = npc;
                }
            }
            return best;
        }
    }
}
