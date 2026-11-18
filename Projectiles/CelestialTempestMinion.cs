using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CelestialAerus.Projectiles
{
    /// <summary>
    /// Celestial Tempest weapon minion (Brigitte's Charm, 2026-11-15):
    /// the mod's own Celestial Tempest as a living weapon. Unique
    /// attack — "Tempest Rain": every 180 ticks it summons a
    /// TempestStar 400 px above a RANDOM nearby enemy; the star falls
    /// and rains its biome projectiles on its own AI. (The TempestStar
    /// is a friendly projectile with its own AI — the minion just
    /// spawns it.)
    ///
    /// Abilities (2026-11-15, Peyton's order): three extra attacks on a
    /// 240-tick cycle — Beam Barrage (a TempestBeam at each of the 5
    /// nearest enemies), Star Cluster (3 TempestStars above the 3
    /// nearest enemies), Biome Storm (8 TempestBeams in a 90-degree
    /// fan). All hit for the same flat 6000 typeless damage as the
    /// primary attack.
    ///
    /// Charm spec: 40x40 hitbox, scale 1.0 (item-sized), flat 6000
    /// typeless damage, local immunity frames (20-tick cooldown),
    /// tileCollide false, timeLeft 2 (refreshed by the charm).
    /// </summary>
    public class CelestialTempestMinion : ModProjectile
    {
        public override string Texture => "CelestialAerus/Textures/CelestialTempest";

        private const float TargetRange = 1000f;
        private const int MinionDamage = 6000;     // flat, typeless (charm design)

        private static readonly Vector2 IdleOffset = new Vector2(0f, -260f);

        // Tempest Rain: a star every 180 ticks, 400 px above a random
        // nearby enemy, falling on its own.
        private const int RainInterval = 180;
        private const float StarSpawnHeight = 400f;

        // Ability cycle: one ability every 240 ticks (4 seconds).
        private const int AbilityInterval = 240;

        // --- State ---
        private NPC target = null;
        private int retargetTimer = 0;
        private int rainTimer = 0;

        // Stagger offset 192: each weapon starts its ability cycle at a
        // different point so all five don't fire on the same tick.
        private int abilityTimer = 192;
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

            // Slow spin at all times.
            Projectile.rotation += 0.04f;

            if (target == null)
            {
                SteerToward(owner.Center + IdleOffset, 8f, 0.2f);
                return;
            }

            // Hover high above the player (the storm hangs over the
            // fight, not in it) while the rain timer runs.
            SteerToward(owner.Center + IdleOffset, 8f, 0.2f);

            // Tempest Rain: every 180 ticks, a star above a random
            // nearby enemy.
            rainTimer++;
            if (rainTimer >= RainInterval)
            {
                rainTimer = 0;
                SummonStar();
            }
        }

        private void SummonStar()
        {
            if (Projectile.owner != Main.myPlayer)
                return;
            NPC victim = FindRandomTarget(TargetRange);
            if (victim == null)
                return;
            Vector2 spawnPos = victim.Center + new Vector2(0f, -StarSpawnHeight);
            SpawnFriendlyProjectile(ModContent.ProjectileType<TempestStar>(), spawnPos, Vector2.Zero);
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
                    // Beam Barrage: a TempestBeam at each of the 5 nearest
                    // enemies, one each, speed 14.
                    {
                        List<NPC> nearest = FindNearestTargets(5, TargetRange);
                        foreach (NPC enemy in nearest)
                        {
                            Vector2 vel = (enemy.Center - Projectile.Center).SafeNormalize(Vector2.UnitY) * 14f;
                            SpawnAbilityProjectile(ModContent.ProjectileType<TempestBeam>(), Projectile.Center, vel);
                        }
                    }
                    break;
                case 1:
                    // Star Cluster: a TempestStar above each of the 3
                    // nearest enemies, all at once.
                    {
                        List<NPC> nearest = FindNearestTargets(3, TargetRange);
                        foreach (NPC enemy in nearest)
                        {
                            Vector2 spawnPos = enemy.Center + new Vector2(0f, -StarSpawnHeight);
                            SpawnAbilityProjectile(ModContent.ProjectileType<TempestStar>(), spawnPos, Vector2.Zero);
                        }
                    }
                    break;
                case 2:
                    // Biome Storm: 8 TempestBeams in a 90-degree fan at
                    // the target, speed 14.
                    {
                        float baseAngle = (target.Center - Projectile.Center).ToRotation();
                        for (int i = 0; i < 8; i++)
                        {
                            // -45 to +45 degrees around the aim direction.
                            float t = i / 7f;
                            float angle = baseAngle - 0.7854f + 1.5708f * t;
                            Vector2 vel = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * 14f;
                            SpawnAbilityProjectile(ModContent.ProjectileType<TempestBeam>(), Projectile.Center, vel);
                        }
                    }
                    break;
            }
        }

        // Nearest N enemies in range, sorted nearest-first.
        private List<NPC> FindNearestTargets(int count, float maxRange)
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
            list.Sort((a, b) => Vector2.DistanceSquared(Projectile.Center, a.Center)
                .CompareTo(Vector2.DistanceSquared(Projectile.Center, b.Center)));
            if (list.Count > count)
                list.RemoveRange(count, list.Count - count);
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

        // Reservoir sampling: one pass, uniform random pick, no list alloc.
        private NPC FindRandomTarget(float maxRange)
        {
            NPC picked = null;
            int count = 0;
            float maxDistSq = maxRange * maxRange;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (npc == null || !npc.active || !npc.CanBeChasedBy())
                    continue;
                if (Vector2.DistanceSquared(Projectile.Center, npc.Center) > maxDistSq)
                    continue;
                count++;
                if (Main.rand.Next(count) == 0)
                    picked = npc;
            }
            return picked;
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
