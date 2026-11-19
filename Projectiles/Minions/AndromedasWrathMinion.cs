using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CelestialAerus.Projectiles
{
    /// <summary>
    /// Andromeda's Wrath weapon minion (Brigitte's Charm, 2026-11-15):
    /// the mod's own Andromeda's Wrath as a living weapon. Unique
    /// attack — "Andromeda Spiral": it hovers 300 px above its target
    /// and every 40 ticks fires a volley of 3 AndromedaSeekers in a
    /// rotating spiral — the base angle advances 40 degrees per volley,
    /// so the three-seeker fan sweeps around the target over time.
    ///
    /// Abilities (2026-11-15, Peyton's order): three extra attacks on a
    /// 240-tick cycle — Starfall (TempestStars above 5 random enemies),
    /// Beam Sweep (3 AndromedaBeams in a fan), Nebula Prison (6 seekers
    /// in a ring around the target, aimed inward). All hit for the same
    /// flat 6000 typeless damage as the primary attack.
    ///
    /// Charm spec: 40x40 hitbox, scale 1.0 (item-sized), flat 6000
    /// typeless damage, local immunity frames (20-tick cooldown),
    /// tileCollide false, timeLeft 2 (refreshed by the charm).
    /// </summary>
    public class AndromedasWrathMinion : ModProjectile
    {
        public override string Texture => "CelestialAerus/Assets/Textures/AndromedasWrath";

        private const float TargetRange = 1000f;
        private const int MinionDamage = 6000;     // flat, typeless (charm design)

        private static readonly Vector2 IdleOffset = new Vector2(120f, -100f);

        // Fight hover: 300 px above the target.
        private const float FightHoverHeight = 300f;

        // Andromeda Spiral: 3 seekers per volley, 40 ticks apart, base
        // angle advances 40 degrees (0.698 rad) per volley.
        private const int VolleyInterval = 40;
        private const int SeekersPerVolley = 3;
        private const float SeekerSpeed = 12f;
        private const float SpiralStep = 0.6981f;  // 40 degrees in radians

        // Ability cycle: one ability every 240 ticks (4 seconds).
        private const int AbilityInterval = 240;

        // --- State ---
        private NPC target = null;
        private int retargetTimer = 0;
        private int volleyTimer = 0;
        private float spiralAngle = 0f;

        // Stagger offset 48: each weapon starts its ability cycle at a
        // different point so all five don't fire on the same tick.
        private int abilityTimer = 48;
        private int abilityIndex = 0;

        public override void SetDefaults()
        {
            Projectile.width = 40;
            Projectile.height = 40;
            Projectile.scale = 1.0f;

            Projectile.friendly = true;
            Projectile.hostile = false;
            Projectile.minion = true;
            Projectile.minionSlots = 1;
            Projectile.damage = MinionDamage;
            Projectile.DamageType = DamageClass.Generic;
            Projectile.knockBack = 0f;
            Projectile.penetrate = -1;

            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 20;

            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;

            // The charm's ModPlayer refreshes timeLeft while equipped.
            Projectile.timeLeft = 2;
            Projectile.netImportant = true;
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

            // Ability cycle: every 240 ticks, fire the next ability — but
            // only when a target exists.
            abilityTimer++;
            if (abilityTimer >= AbilityInterval)
            {
                abilityTimer = 0;
                if (target != null)
                {
                    FireAbility(abilityIndex);
                    abilityIndex = (abilityIndex + 1) % 3;
                }
            }

            // Slow spin at all times (the weapon idles like a sentinel).
            Projectile.rotation += 0.05f;

            if (target == null)
            {
                SteerToward(owner.Center + IdleOffset, 8f, 0.2f);
                return;
            }

            // Hover 300 px above the target.
            SteerToward(target.Center + new Vector2(0f, -FightHoverHeight), 10f, 0.25f);

            // Andromeda Spiral: a 3-seeker volley every 40 ticks, the
            // fan rotating 40 degrees per volley.
            volleyTimer++;
            if (volleyTimer >= VolleyInterval)
            {
                volleyTimer = 0;
                FireSpiralVolley();
                spiralAngle += SpiralStep;
                if (spiralAngle >= MathHelper.TwoPi)
                    spiralAngle -= MathHelper.TwoPi;
            }
        }

        private void FireSpiralVolley()
        {
            if (Projectile.owner != Main.myPlayer)
                return;
            for (int i = 0; i < SeekersPerVolley; i++)
            {
                float angle = spiralAngle + MathHelper.TwoPi * i / SeekersPerVolley;
                Vector2 vel = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * SeekerSpeed;
                SpawnFriendlyProjectile(ModContent.ProjectileType<AndromedaSeeker>(), Projectile.Center, vel);
            }
        }

        // --- Abilities ---

        private void FireAbility(int index)
        {
            if (Projectile.owner != Main.myPlayer)
                return;
            if (target == null || !target.active)
                return;

            switch (index)
            {
                case 0:
                    // Starfall: a TempestStar 400 px above each of 5 random
                    // nearby enemies (one per enemy if fewer than 5).
                    {
                        List<NPC> inRange = FindTargetsInRange(TargetRange);
                        int stars = Math.Min(5, inRange.Count);
                        for (int i = 0; i < stars; i++)
                        {
                            int pick = Main.rand.Next(inRange.Count);
                            NPC victim = inRange[pick];
                            inRange.RemoveAt(pick);
                            Vector2 spawnPos = victim.Center + new Vector2(0f, -400f);
                            SpawnAbilityProjectile(ModContent.ProjectileType<TempestStar>(), spawnPos, Vector2.Zero);
                        }
                    }
                    break;
                case 1:
                    // Beam Sweep: 3 AndromedaBeams in a 60-degree fan at
                    // the target, speed 14.
                    {
                        float baseAngle = (target.Center - Projectile.Center).ToRotation();
                        for (int i = 0; i < 3; i++)
                        {
                            // -30, 0, +30 degrees around the aim direction.
                            float angle = baseAngle - 0.5236f + 0.5236f * i;
                            Vector2 vel = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * 14f;
                            SpawnAbilityProjectile(ModContent.ProjectileType<AndromedaBeam>(), Projectile.Center, vel);
                        }
                    }
                    break;
                case 2:
                    // Nebula Prison: 6 AndromedaSeekers in a ring around
                    // the target, spawned 300 px out and aimed inward.
                    for (int i = 0; i < 6; i++)
                    {
                        float angle = MathHelper.TwoPi * i / 6;
                        Vector2 offset = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * 300f;
                        Vector2 spawnPos = target.Center + offset;
                        Vector2 vel = (target.Center - spawnPos).SafeNormalize(Vector2.UnitY) * 12f;
                        SpawnAbilityProjectile(ModContent.ProjectileType<AndromedaSeeker>(), spawnPos, vel);
                    }
                    break;
            }
        }

        // Every enemy in range (for random picks).
        private List<NPC> FindTargetsInRange(float maxRange)
        {
            var list = new List<NPC>();
            float maxDistSq = maxRange * maxRange;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (npc == null || !npc.active || !npc.CanBeChasedBy())
                    continue;
                if (Vector2.DistanceSquared(Projectile.Center, npc.Center) <= maxDistSq)
                    list.Add(npc);
            }
            return list;
        }

        // Ability spawns: same as SpawnFriendlyProjectile, plus typeless
        // (Generic) damage so abilities hit exactly as hard as the rest.
        private int SpawnAbilityProjectile(int type, Vector2 position, Vector2 velocity)
        {
            int p = SpawnFriendlyProjectile(type, position, velocity);
            if (p >= 0 && p < Main.maxProjectiles)
                Main.projectile[p].DamageType = DamageClass.Generic;
            return p;
        }

        // --- Shared helpers ---

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

        private void SteerToward(Vector2 targetPoint, float maxSpeed, float accel)
        {
            Vector2 dir = targetPoint - Projectile.Center;
            if (dir.LengthSquared() < 4f)
            {
                Projectile.velocity *= 0.95f;
                return;
            }
            dir = dir.SafeNormalize(Vector2.Zero);
            Projectile.velocity += dir * accel;
            if (Projectile.velocity.Length() > maxSpeed)
                Projectile.velocity = Projectile.velocity.SafeNormalize(Vector2.Zero) * maxSpeed;
        }

        // Spawns a mod projectile as a friendly charm attack: owner-gated,
        // flipped friendly, local immunity frames (20-tick cooldown).
        private int SpawnFriendlyProjectile(int type, Vector2 position, Vector2 velocity)
        {
            if (Projectile.owner != Main.myPlayer)
                return -1;
            int p = Projectile.NewProjectile(Projectile.GetSource_FromThis(), position, velocity,
                type, MinionDamage, 0f, Projectile.owner);
            if (p >= 0 && p < Main.maxProjectiles)
            {
                Main.projectile[p].friendly = true;
                Main.projectile[p].hostile = false;
                Main.projectile[p].usesLocalNPCImmunity = true;
                Main.projectile[p].localNPCHitCooldown = 20;
            }
            return p;
        }
    }
}
