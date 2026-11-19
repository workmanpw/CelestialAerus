using CelestialAerus.Projectiles.Weapons;
using CelestialAerus.Items.Weapons;
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CelestialAerus.Projectiles
{
    /// <summary>
    /// Space Laser Remote weapon minion (Brigitte's Charm, 2026-11-15):
    /// the Space Laser Remote as a living weapon. Unique attack
    /// "Orbital Strike": every 90 ticks it marks the target with a
    /// brief telegraph (5 teal dust at the target's position), then
    /// 30 ticks later a SpaceLaserBeam fires from 800px above the
    /// marked spot straight down onto it. Single-target, high-impact.
    /// The beam strikes the MARKED position, not the target's current
    /// position — a fast enemy can dodge it. All damage is flat 6000,
    /// typeless (DamageClass.Generic), per the charm's design. Local
    /// immunity frames on the minion and on every projectile it spawns.
    /// </summary>
    public class SpaceLaserRemoteMinion : ModProjectile
    {
        public override string Texture => "CelestialAerus/Assets/Textures/SpaceLaserRemote";

        private const float TargetRange = 1000f;
        private const int MinionDamage = 6000; // flat, typeless (charm design)
        private const int StrikeInterval = 90; // ticks between marks
        private const int TelegraphDelay = 30; // ticks from mark to beam
        private const float BeamDropHeight = 800f; // beam spawns this far above the mark
        private const float BeamSpeed = 20f;       // straight down
        private const int AbilityInterval = 240; // ticks between ability casts (4s)

        // Idle hover: upper LEFT of the player.
        private static readonly Vector2 IdleOffset = new Vector2(-120f, -320f);

        private NPC target;
        private int retargetTimer;
        private int attackTimer;
        private int abilityTimer;
        private int abilityIndex;
        private Vector2 strikePos; // the marked position (set at telegraph time)

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
            SteerToward(target.Center + new Vector2(-150f, -200f), 10f, 0.12f);

            // Orbital Strike: mark at tick 0, beam at tick 30.
            attackTimer++;
            if (attackTimer == 1)
            {
                strikePos = target.Center;
                for (int i = 0; i < 5; i++)
                    Dust.NewDust(target.position, target.width, target.height, DustID.BlueCrystalShard);
            }
            else if (attackTimer == 1 + TelegraphDelay && Projectile.owner == Main.myPlayer)
            {
                Vector2 spawn = strikePos + new Vector2(0f, -BeamDropHeight);
                SpawnFriendlyProjectile(ModContent.ProjectileType<SpaceLaserBeam>(), spawn, new Vector2(0f, BeamSpeed));
            }
            if (attackTimer >= StrikeInterval)
                attackTimer = 0;

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
                    TripleStrike();
                    break;
                case 1:
                    LaserSweep(target.Center);
                    break;
                default:
                    OrbitalBarrage();
                    break;
            }
        }

        // (a) Triple Strike: orbital strikes on the 3 nearest enemies —
        // telegraph dust at each, then a SpaceLaserBeam from 800px above
        // each, all at once (fast patch: immediate beams, no stagger).
        private void TripleStrike()
        {
            List<NPC> targets = FindTargets(3, TargetRange);
            int beamType = ModContent.ProjectileType<SpaceLaserBeam>();
            foreach (NPC t in targets)
            {
                for (int i = 0; i < 5; i++)
                    Dust.NewDust(t.position, t.width, t.height, DustID.BlueCrystalShard);
                Vector2 spawn = t.Center + new Vector2(0f, -BeamDropHeight);
                SpawnFriendlyProjectile(beamType, spawn, new Vector2(0f, BeamSpeed));
            }
        }

        // (b) Laser Sweep: 5 SpaceLaserBeams in a horizontal line —
        // X offsets -400 to +400 around the target, all falling from
        // 800px above.
        private void LaserSweep(Vector2 targetCenter)
        {
            int beamType = ModContent.ProjectileType<SpaceLaserBeam>();
            for (int i = 0; i < 5; i++)
            {
                float xOffset = -400f + i * 200f;
                Vector2 spawn = targetCenter + new Vector2(xOffset, -BeamDropHeight);
                SpawnFriendlyProjectile(beamType, spawn, new Vector2(0f, BeamSpeed));
            }
        }

        // (c) Orbital Barrage: 8 lasers rain down on the nearest 8
        // enemies (round-robin if fewer than 8 targets).
        private void OrbitalBarrage()
        {
            List<NPC> targets = FindTargets(8, TargetRange);
            if (targets.Count == 0)
                return;
            int beamType = ModContent.ProjectileType<SpaceLaserBeam>();
            for (int i = 0; i < 8; i++)
            {
                NPC t = targets[i % targets.Count];
                Vector2 spawn = t.Center + new Vector2(Main.rand.NextFloat(-60f, 60f), -BeamDropHeight);
                SpawnFriendlyProjectile(beamType, spawn, new Vector2(0f, BeamSpeed));
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
