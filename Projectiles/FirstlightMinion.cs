using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CelestialAerus.Projectiles
{
    /// <summary>
    /// Firstlight minion (Brigitte's Charm, 2026-11-15; renamed from
    /// Test Weapon 2026-11-18): Firstlight as a living weapon. Unique
    /// attack "Firstlight Spread": every 60 ticks it fires a 5-bolt
    /// spread of TrialBolts at the target — a 50-degree fan at speed
    /// 16. Rapid, simple, relentless. All
    /// damage is flat 6000, typeless (DamageClass.Generic), per the
    /// charm's design. Local immunity frames on the minion and on
    /// every projectile it spawns.
    /// </summary>
    public class FirstlightMinion : ModProjectile
    {
        // Firstlight's own texture (see Items/Firstlight.cs).
        public override string Texture => "CelestialAerus/Textures/Firstlight";

        private const float TargetRange = 1000f;
        private const int MinionDamage = 6000; // flat, typeless (charm design)
        private const int SpreadInterval = 60; // ticks between spreads
        private const int BoltCount = 5;
        private const float BoltSpeed = 16f;
        private const float SpreadDegrees = 50f; // total fan width
        private const int AbilityInterval = 240; // ticks between ability casts (4s)

        // Idle hover: upper RIGHT of the player.
        private static readonly Vector2 IdleOffset = new Vector2(120f, -320f);

        private NPC target;
        private int retargetTimer;
        private int attackTimer;
        private int abilityTimer;
        private int abilityIndex;

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

            // Stagger the ability cycle by the hover offset so the weapon
            // minions don't all fire their abilities on the same tick.
            abilityTimer = (int)((IdleOffset.X + 400f) / 800f * AbilityInterval);
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

            // Re-scan for a target every 15 ticks (or if the current one died).
            retargetTimer++;
            if (retargetTimer >= 15 || target == null || !target.active || !target.CanBeChasedBy())
            {
                retargetTimer = 0;
                target = FindTarget(TargetRange);
            }

            if (target == null)
            {
                SteerToward(owner.Center + IdleOffset, 8f, 0.15f);
                Projectile.rotation += 0.05f; // slow idle spin
                attackTimer = 0;
                return;
            }

            // Face the target and hold a firing position near it.
            Projectile.rotation = (target.Center - Projectile.Center).ToRotation();
            SteerToward(target.Center + new Vector2(0f, -180f), 10f, 0.12f);

            // Firstlight Spread: a 5-bolt, 50-degree fan every 60 ticks.
            attackTimer++;
            if (attackTimer >= SpreadInterval)
            {
                attackTimer = 0;
                if (Projectile.owner == Main.myPlayer)
                {
                    Vector2 dir = (target.Center - Projectile.Center).SafeNormalize(Vector2.UnitX);
                    float spreadRad = MathHelper.ToRadians(SpreadDegrees);
                    float step = spreadRad / (BoltCount - 1);
                    for (int i = 0; i < BoltCount; i++)
                    {
                        float angle = -spreadRad / 2f + step * i;
                        SpawnFriendlyProjectile(ModContent.ProjectileType<TrialBolt>(), Projectile.Center, dir.RotatedBy(angle) * BoltSpeed);
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
                    FirstlightSpiral();
                    break;
                case 1:
                    FirstlightRain();
                    break;
                default:
                    FirstlightRing();
                    break;
            }
        }

        // (a) Test Spiral: 10 TrialBolts in a spiral — evenly spaced
        // angles, speeds rising from 12 to 18.
        private void FirstlightSpiral()
        {
            int boltType = ModContent.ProjectileType<TrialBolt>();
            float baseAngle = (target.Center - Projectile.Center).ToRotation();
            for (int i = 0; i < 10; i++)
            {
                float angle = baseAngle + i * (MathHelper.TwoPi / 10f);
                float speed = 12f + i * (6f / 9f);
                Vector2 velocity = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * speed;
                SpawnFriendlyProjectile(boltType, Projectile.Center, velocity);
            }
        }

        // (b) Test Rain: 8 TrialBolts rain from 600px above the nearest
        // 8 enemies (round-robin if fewer than 8 targets).
        private void FirstlightRain()
        {
            List<NPC> targets = FindTargets(8, TargetRange);
            if (targets.Count == 0)
                return;
            int boltType = ModContent.ProjectileType<TrialBolt>();
            for (int i = 0; i < 8; i++)
            {
                NPC t = targets[i % targets.Count];
                Vector2 spawn = t.Center + new Vector2(Main.rand.NextFloat(-100f, 100f), -600f);
                Vector2 dir = (t.Center - spawn).SafeNormalize(Vector2.UnitY);
                SpawnFriendlyProjectile(boltType, spawn, dir * BoltSpeed);
            }
        }

        // (c) Test Ring: 12 TrialBolts in a 360-degree ring around the
        // minion, speed 14.
        private void FirstlightRing()
        {
            int boltType = ModContent.ProjectileType<TrialBolt>();
            for (int i = 0; i < 12; i++)
            {
                float angle = i * (MathHelper.TwoPi / 12f);
                Vector2 velocity = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * 14f;
                SpawnFriendlyProjectile(boltType, Projectile.Center, velocity);
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

        private void SteerToward(Vector2 point, float speed, float ease)
        {
            Vector2 desired = (point - Projectile.Center).SafeNormalize(Vector2.Zero) * speed;
            Projectile.velocity = Vector2.Lerp(Projectile.velocity, desired, ease);
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

        // Spawns a projectile owned by this minion's owner, flipped
        // friendly with local immunity frames (charm design).
        private void SpawnFriendlyProjectile(int type, Vector2 position, Vector2 velocity)
        {
            int p = Projectile.NewProjectile(Projectile.GetSource_FromThis(), position, velocity, type, MinionDamage, 0f, Projectile.owner);
            if (p >= 0 && p < Main.maxProjectiles)
            {
                Main.projectile[p].friendly = true;
                Main.projectile[p].hostile = false;
                Main.projectile[p].DamageType = DamageClass.Generic;
                Main.projectile[p].usesLocalNPCImmunity = true;
                Main.projectile[p].localNPCHitCooldown = 20;
            }
        }
    }
}
