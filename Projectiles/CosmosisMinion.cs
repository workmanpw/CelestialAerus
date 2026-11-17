using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CelestialAerus.Projectiles
{
    /// <summary>
    /// Cosmosis weapon minion (Brigitte's Charm, 2026-11-15): the
    /// Cosmosis spellbook as a living weapon. Unique attack "Cosmic
    /// Split": every 50 ticks it fires a CosmoBeam at the target, and
    /// 10 ticks later three CosmoSeekers fan out at 120-degree angles
    /// (one on-target, two flanking), as if the beam had split.
    /// All damage is flat 6000, typeless (DamageClass.Generic), per the
    /// charm's design. Local immunity frames on the minion and on every
    /// projectile it spawns.
    /// </summary>
    public class CosmosisMinion : ModProjectile
    {
        public override string Texture => "CelestialAerus/Textures/Cosmosis";

        private const float TargetRange = 1000f;
        private const int MinionDamage = 6000; // flat, typeless (charm design)
        private const int BeamInterval = 50;   // ticks between beams
        private const int SplitDelay = 10;     // ticks after the beam, the seekers fan out
        private const float BeamSpeed = 14f;
        private const float SeekerSpeed = 12f;
        private const int AbilityInterval = 240; // ticks between ability casts (4s)

        // Idle hover: far LEFT of the player.
        private static readonly Vector2 IdleOffset = new Vector2(-360f, -60f);

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

            // RECALL + FAST FOLLOW: teleport back if too far; fly fast
            // toward the player if lagging behind. NOTE: the else-if is
            // OUTSIDE the recall if-block's closing brace (a previous
            // script put it inside and broke the build — CS8641).
            float distToOwner = Vector2.Distance(Projectile.Center, owner.Center);
            if (distToOwner > 800f)
            {
                Projectile.Center = owner.Center;
                Projectile.velocity = Vector2.Zero;
                Projectile.netUpdate = true;
                for (int i = 0; i < 10; i++)
                    Dust.NewDust(Projectile.position, Projectile.width, Projectile.height, DustID.GoldFlame);
            }
            else if (distToOwner > 400f)
            {
                Vector2 toOwner = (owner.Center - Projectile.Center).SafeNormalize(Vector2.Zero);
                Projectile.velocity = Vector2.Lerp(Projectile.velocity, toOwner * 20f, 0.2f);
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
            SteerToward(target.Center + new Vector2(-200f, -150f), 10f, 0.12f);

            // Cosmic Split: beam at tick 0, seeker fan at tick 10.
            attackTimer++;
            if (attackTimer == 1 && Projectile.owner == Main.myPlayer)
            {
                Vector2 dir = (target.Center - Projectile.Center).SafeNormalize(Vector2.UnitX);
                SpawnFriendlyProjectile(ModContent.ProjectileType<CosmoBeam>(), Projectile.Center, dir * BeamSpeed);
            }
            else if (attackTimer == 1 + SplitDelay && Projectile.owner == Main.myPlayer)
            {
                Vector2 dir = (target.Center - Projectile.Center).SafeNormalize(Vector2.UnitX);
                for (int i = -1; i <= 1; i++)
                {
                    Vector2 fanDir = dir.RotatedBy(i * (MathHelper.TwoPi / 3f));
                    SpawnFriendlyProjectile(ModContent.ProjectileType<CosmoSeeker>(), Projectile.Center, fanDir * SeekerSpeed);
                }
            }
            if (attackTimer >= BeamInterval)
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
                    CosmicCross(target.Center);
                    break;
                case 1:
                    SeekerSwarm();
                    break;
                default:
                    CosmoSpiral();
                    break;
            }
        }

        // (a) Cosmic Cross: 4 CosmoBeams in a cross pattern — spawned
        // 200px out in the 4 cardinal directions, all aimed at the target.
        private void CosmicCross(Vector2 targetCenter)
        {
            int beamType = ModContent.ProjectileType<CosmoBeam>();
            Vector2[] offsets =
            {
                new Vector2(0f, -200f),
                new Vector2(200f, 0f),
                new Vector2(0f, 200f),
                new Vector2(-200f, 0f)
            };
            foreach (Vector2 offset in offsets)
            {
                Vector2 spawn = targetCenter + offset;
                Vector2 dir = (targetCenter - spawn).SafeNormalize(Vector2.UnitX);
                SpawnFriendlyProjectile(beamType, spawn, dir * BeamSpeed);
            }
        }

        // (b) Seeker Swarm: 8 CosmoSeekers spread across the nearest 8
        // enemies (round-robin if fewer than 8 targets).
        private void SeekerSwarm()
        {
            List<NPC> targets = FindTargets(8, TargetRange);
            if (targets.Count == 0)
                return;
            int seekerType = ModContent.ProjectileType<CosmoSeeker>();
            for (int i = 0; i < 8; i++)
            {
                NPC t = targets[i % targets.Count];
                Vector2 dir = (t.Center - Projectile.Center).SafeNormalize(Vector2.UnitX);
                SpawnFriendlyProjectile(seekerType, Projectile.Center, dir * SeekerSpeed);
            }
        }

        // (c) Cosmo Spiral: 6 CosmoSeekers in a spiral — evenly spaced
        // angles, speeds rising from 10 to 14.
        private void CosmoSpiral()
        {
            int seekerType = ModContent.ProjectileType<CosmoSeeker>();
            float baseAngle = (target.Center - Projectile.Center).ToRotation();
            for (int i = 0; i < 6; i++)
            {
                float angle = baseAngle + i * (MathHelper.TwoPi / 6f);
                float speed = 10f + i * 0.8f;
                Vector2 velocity = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * speed;
                SpawnFriendlyProjectile(seekerType, Projectile.Center, velocity);
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
