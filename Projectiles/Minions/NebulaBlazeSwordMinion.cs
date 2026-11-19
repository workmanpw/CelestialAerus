using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CelestialAerus.Projectiles
{
    /// <summary>
    /// Nebula Blaze Sword weapon minion (Brigitte's Charm, 2026-11-15):
    /// the Nebula Blaze Sword as a living weapon. Unique attack "Nebula
    /// Convergence": every 150 ticks it calls down the weapon's
    /// signature pattern on the target — 4 NebulaBlazeBolts rain from
    /// the sky (spawned 600px above, random X offsets) plus 3 bolts
    /// that converge from the sides (spawned 400px left, right, and
    /// above, aimed at the target). All damage is flat 6000, typeless
    /// (DamageClass.Generic), per the charm's design. Local immunity
    /// frames on the minion and on every projectile it spawns.
    /// </summary>
    public class NebulaBlazeSwordMinion : ModProjectile
    {
        public override string Texture => "CelestialAerus/Assets/Textures/NebulaBlazeSword";

        private const float TargetRange = 1000f;
        private const int MinionDamage = 6000; // flat, typeless (charm design)
        private const int ConvergenceInterval = 150; // ticks between barrages
        private const float BoltSpeed = 16f;
        private const float RainHeight = 600f;   // rain bolts spawn this far above the target
        private const float RainSpread = 300f;   // random X offset for rain bolts
        private const float ConvergeDistance = 400f; // side bolts spawn this far out
        private const int AbilityInterval = 240; // ticks between ability casts (4s)

        // Idle hover: far RIGHT of the player.
        private static readonly Vector2 IdleOffset = new Vector2(360f, -60f);

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
            SteerToward(target.Center + new Vector2(200f, -150f), 10f, 0.12f);

            // Nebula Convergence: one full barrage every 150 ticks.
            attackTimer++;
            if (attackTimer >= ConvergenceInterval)
            {
                attackTimer = 0;
                if (Projectile.owner == Main.myPlayer)
                    FireConvergence(target.Center);
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
                    NebulaRing(target.Center);
                    break;
                case 1:
                    BlazeBarrage(target.Center);
                    break;
                default:
                    NebulaSpiral(target.Center);
                    break;
            }
        }

        // (a) Nebula Ring: 8 NebulaBlazeBolts in a ring around the
        // target — spawned 250px out, all aimed inward.
        private void NebulaRing(Vector2 targetCenter)
        {
            int boltType = ModContent.ProjectileType<NebulaBlazeBolt>();
            for (int i = 0; i < 8; i++)
            {
                float angle = i * (MathHelper.TwoPi / 8f);
                Vector2 spawn = targetCenter + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * 250f;
                Vector2 dir = (targetCenter - spawn).SafeNormalize(Vector2.UnitX);
                SpawnFriendlyProjectile(boltType, spawn, dir * BoltSpeed);
            }
        }

        // (b) Blaze Barrage: 6 bolts rain from 600px above in a line
        // across the target (X offsets -300 to +300).
        private void BlazeBarrage(Vector2 targetCenter)
        {
            int boltType = ModContent.ProjectileType<NebulaBlazeBolt>();
            for (int i = 0; i < 6; i++)
            {
                float xOffset = -300f + i * 120f;
                Vector2 spawn = targetCenter + new Vector2(xOffset, -RainHeight);
                Vector2 dir = (targetCenter - spawn).SafeNormalize(Vector2.UnitY);
                SpawnFriendlyProjectile(boltType, spawn, dir * BoltSpeed);
            }
        }

        // (c) Nebula Spiral: 10 bolts in a spiral converging on the
        // target — spawned at increasing radii, all aimed at it.
        private void NebulaSpiral(Vector2 targetCenter)
        {
            int boltType = ModContent.ProjectileType<NebulaBlazeBolt>();
            for (int i = 0; i < 10; i++)
            {
                float angle = i * (MathHelper.TwoPi / 10f);
                float radius = 100f + i * 30f;
                Vector2 spawn = targetCenter + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius;
                Vector2 dir = (targetCenter - spawn).SafeNormalize(Vector2.UnitX);
                SpawnFriendlyProjectile(boltType, spawn, dir * BoltSpeed);
            }
        }

        private void FireConvergence(Vector2 targetCenter)
        {
            int boltType = ModContent.ProjectileType<NebulaBlazeBolt>();

            // 4 bolts rain from the sky: spawned 600px above the target
            // with random X offsets, falling onto it.
            for (int i = 0; i < 4; i++)
            {
                Vector2 spawn = targetCenter + new Vector2(Main.rand.NextFloat(-RainSpread, RainSpread), -RainHeight);
                Vector2 dir = (targetCenter - spawn).SafeNormalize(Vector2.UnitY);
                SpawnFriendlyProjectile(boltType, spawn, dir * BoltSpeed);
            }

            // 3 bolts converge from the sides: left, right, and above,
            // each 400px out, aimed at the target.
            Vector2[] convergeOffsets =
            {
                new Vector2(-ConvergeDistance, 0f),
                new Vector2(ConvergeDistance, 0f),
                new Vector2(0f, -ConvergeDistance)
            };
            foreach (Vector2 offset in convergeOffsets)
            {
                Vector2 spawn = targetCenter + offset;
                Vector2 dir = (targetCenter - spawn).SafeNormalize(Vector2.UnitX);
                SpawnFriendlyProjectile(boltType, spawn, dir * BoltSpeed);
            }
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
