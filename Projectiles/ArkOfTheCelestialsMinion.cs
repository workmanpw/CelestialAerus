using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CelestialAerus.Projectiles
{
    /// <summary>
    /// Ark of the Celestials weapon minion (Brigitte's Charm, 2026-11-15):
    /// the mod's own Ark of the Celestials as a living weapon. Unique
    /// attack — "Ark Volley": every 120 ticks it fires a fan of 5
    /// CelestialFireballs (60-degree spread centered on the target,
    /// speed 14); each fireball homes independently on its own AI.
    ///
    /// Abilities (2026-11-15, Peyton's order): three extra attacks on a
    /// 240-tick cycle — Celestial Rain (10 fireballs rain on random
    /// enemies), Ark Barrage (3 volleys of 5 fireballs, 15 total),
    /// Nova Burst (12 fireballs in a ring). All hit for the same flat
    /// 6000 typeless damage as the primary attack.
    ///
    /// Charm spec: 40x40 hitbox, scale 1.0 (item-sized), flat 6000
    /// typeless damage, local immunity frames (20-tick cooldown),
    /// tileCollide false, timeLeft 2 (refreshed by the charm).
    /// </summary>
    public class ArkOfTheCelestialsMinion : ModProjectile
    {
        public override string Texture => "CelestialAerus/Textures/ArkOfTheCelestials";

        private const float TargetRange = 1000f;
        private const int MinionDamage = 6000;     // flat, typeless (charm design)

        private static readonly Vector2 IdleOffset = new Vector2(-240f, -180f);

        // Fight hover: 200 px above the target.
        private const float FightHoverHeight = 200f;

        // Ark Volley: 5 fireballs, 60-degree fan, every 120 ticks.
        private const int VolleyInterval = 120;
        private const int FireballsPerVolley = 5;
        private const float FanSpread = 1.0472f;   // 60 degrees in radians
        private const float FireballSpeed = 14f;

        // Ability cycle: one ability every 240 ticks (4 seconds).
        private const int AbilityInterval = 240;

        // --- State ---
        private NPC target = null;
        private int retargetTimer = 0;
        private int volleyTimer = 0;

        // Stagger offset 96: each weapon starts its ability cycle at a
        // different point so all five don't fire on the same tick.
        private int abilityTimer = 96;
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

            // RECALL + FAST FOLLOW: teleport back if too far; fly fast
            // toward the player if lagging behind. NOTE: the else-if is
            // OUTSIDE the recall if-block's closing brace (a previous
            // script nested it inside and broke the build — CS8641).
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

            // Hover 200 px above the target.
            SteerToward(target.Center + new Vector2(0f, -FightHoverHeight), 10f, 0.25f);

            // Ark Volley: a 5-fireball fan every 120 ticks, centered on
            // the target. The fireballs home independently.
            volleyTimer++;
            if (volleyTimer >= VolleyInterval)
            {
                volleyTimer = 0;
                FireFanVolley();
            }
        }

        private void FireFanVolley()
        {
            if (Projectile.owner != Main.myPlayer)
                return;
            if (target == null || !target.active)
                return;
            float baseAngle = (target.Center - Projectile.Center).ToRotation();
            for (int i = 0; i < FireballsPerVolley; i++)
            {
                // Fan from -30 to +30 degrees around the aim direction.
                float t = FireballsPerVolley == 1 ? 0.5f : i / (float)(FireballsPerVolley - 1);
                float angle = baseAngle - FanSpread / 2f + FanSpread * t;
                Vector2 vel = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * FireballSpeed;
                SpawnFriendlyProjectile(ModContent.ProjectileType<CelestialFireball>(), Projectile.Center, vel);
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
                    // Celestial Rain: 10 CelestialFireballs rain from
                    // 600 px above random nearby enemies.
                    {
                        List<NPC> inRange = FindTargetsInRange(TargetRange);
                        for (int i = 0; i < 10; i++)
                        {
                            NPC victim = inRange[Main.rand.Next(inRange.Count)];
                            Vector2 spawnPos = victim.Center + new Vector2(Main.rand.Next(-200, 201), -600f);
                            SpawnAbilityProjectile(ModContent.ProjectileType<CelestialFireball>(), spawnPos, new Vector2(0f, 12f));
                        }
                    }
                    break;
                case 1:
                    // Ark Barrage: 3 rapid volleys of 5 fireballs — 15
                    // total, in three fans with different spreads (60, 90,
                    // 30 degrees), all centered on the target.
                    {
                        float baseAngle = (target.Center - Projectile.Center).ToRotation();
                        float[] spreads = { 1.0472f, 1.5708f, 0.5236f };
                        for (int fan = 0; fan < 3; fan++)
                        {
                            float spread = spreads[fan];
                            for (int i = 0; i < 5; i++)
                            {
                                float t = i / 4f;
                                float angle = baseAngle - spread / 2f + spread * t;
                                Vector2 vel = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * FireballSpeed;
                                SpawnAbilityProjectile(ModContent.ProjectileType<CelestialFireball>(), Projectile.Center, vel);
                            }
                        }
                    }
                    break;
                case 2:
                    // Nova Burst: 12 CelestialFireballs in a 360-degree ring.
                    for (int i = 0; i < 12; i++)
                    {
                        float angle = MathHelper.TwoPi * i / 12;
                        Vector2 vel = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * 12f;
                        SpawnAbilityProjectile(ModContent.ProjectileType<CelestialFireball>(), Projectile.Center, vel);
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
