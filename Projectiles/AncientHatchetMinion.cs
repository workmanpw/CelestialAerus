using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CelestialAerus.Projectiles
{
    /// <summary>
    /// Ancient Hatchet weapon minion (Brigitte's Charm, 2026-11-15): the
    /// mod's own Ancient Hatchet as a living weapon. Unique attack —
    /// "Scourge Burst": it charges the nearest enemy, and on contact it
    /// explodes into a ring of 6 TinyHatchets that home to nearby
    /// enemies, then reforms at the player's side. The minion itself does
    /// NOT die — it teleports to its hover offset and re-arms after a
    /// 60-tick cooldown before charging again. (The burst mirrors the
    /// hatchet's Scourge rework: the parent explodes into TinyHatchets
    /// on hit; here the parent is a minion, so it reforms instead of
    /// dying.)
    ///
    /// Abilities (2026-11-15, Peyton's order): three extra attacks on a
    /// 240-tick cycle — Ember Ring (8 fireballs in a ring), Hatchet
    /// Storm (a hatchet at each of the 3 nearest enemies), Daybreak
    /// Spiral (12 TinyHatchets in a spiral). All hit for the same flat
    /// 6000 typeless damage as the primary attack.
    ///
    /// Charm spec: 40x40 hitbox, scale 1.0 (item-sized), flat 6000
    /// typeless damage, local immunity frames (20-tick cooldown),
    /// tileCollide false, timeLeft 2 (refreshed by the charm).
    /// </summary>
    public class AncientHatchetMinion : ModProjectile
    {
        public override string Texture => "CelestialAerus/Textures/AncientHatchet";

        private const float TargetRange = 1000f;
        private const int MinionDamage = 6000;     // flat, typeless (charm design)

        private static readonly Vector2 IdleOffset = new Vector2(-120f, -100f);

        // Charge movement.
        private const float ChargeSpeed = 14f;
        private const float ChargeAccel = 0.5f;

        // Scourge Burst: ring of TinyHatchets on contact.
        private const int BurstCount = 6;
        private const float BurstSpeed = 10f;
        private const int RearmTime = 60;          // ticks before the next charge

        // States (Projectile.ai[0], synced).
        private const int StateCharge = 0;
        private const int StateRearm = 1;

        // Ability cycle: one ability every 240 ticks (4 seconds).
        private const int AbilityInterval = 240;

        // --- State ---
        private NPC target = null;
        private int retargetTimer = 0;
        private int rearmTimer = 0;

        // Stagger offset 0: each weapon starts its ability cycle at a
        // different point so all five don't fire on the same tick.
        private int abilityTimer = 0;
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

            int state = (int)Projectile.ai[0];

            if (state == StateRearm)
            {
                // Re-forming at the player's side: drift to the hover
                // offset, then re-arm after the cooldown.
                SteerToward(owner.Center + IdleOffset, 10f, 0.3f);
                Projectile.rotation += 0.05f;
                rearmTimer++;
                if (rearmTimer >= RearmTime)
                {
                    rearmTimer = 0;
                    Projectile.ai[0] = StateCharge;
                    Projectile.netUpdate = true;
                }
                return;
            }

            if (target == null)
            {
                // No enemy: hover near the player, spinning lazily.
                SteerToward(owner.Center + IdleOffset, 8f, 0.2f);
                Projectile.rotation += 0.1f;
                return;
            }

            // Charge: fly at the target, spinning fast. Contact triggers
            // the Scourge Burst in OnHitNPC.
            Vector2 toTarget = (target.Center - Projectile.Center).SafeNormalize(Vector2.Zero);
            Projectile.velocity = Vector2.Lerp(Projectile.velocity, toTarget * ChargeSpeed, ChargeAccel / ChargeSpeed);
            if (Projectile.velocity.Length() > ChargeSpeed)
                Projectile.velocity = Projectile.velocity.SafeNormalize(Vector2.Zero) * ChargeSpeed;
            Projectile.rotation += 0.35f;
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            // The burst is owner-only (spawn authority + teleport).
            if (Projectile.owner != Main.myPlayer)
                return;
            if ((int)Projectile.ai[0] != StateCharge)
                return;

            // Scourge Burst: a ring of TinyHatchets, spread evenly.
            for (int i = 0; i < BurstCount; i++)
            {
                float angle = MathHelper.TwoPi * i / BurstCount;
                Vector2 vel = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * BurstSpeed;
                SpawnFriendlyProjectile(ModContent.ProjectileType<TinyHatchet>(), Projectile.Center, vel);
            }

            // Reform at the player's side and re-arm.
            Player owner = Main.player[Projectile.owner];
            Projectile.Center = owner.Center + IdleOffset;
            Projectile.velocity = Vector2.Zero;
            Projectile.ai[0] = StateRearm;
            rearmTimer = 0;
            Projectile.netUpdate = true;
            for (int i = 0; i < 15; i++)
                Dust.NewDust(Projectile.position, Projectile.width, Projectile.height, DustID.GoldFlame);
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
                    // Ember Ring: 8 CelestialFireballs in a 360-degree ring.
                    for (int i = 0; i < 8; i++)
                    {
                        float angle = MathHelper.TwoPi * i / 8;
                        Vector2 vel = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * 10f;
                        SpawnAbilityProjectile(ModContent.ProjectileType<CelestialFireball>(), Projectile.Center, vel);
                    }
                    break;
                case 1:
                    // Hatchet Storm: throw an AncientHatchetProjectile at
                    // each of the 3 nearest enemies (they explode into
                    // TinyHatchets on hit via their own AI).
                    {
                        List<NPC> nearest = FindNearestTargets(3, TargetRange);
                        foreach (NPC enemy in nearest)
                        {
                            Vector2 vel = (enemy.Center - Projectile.Center).SafeNormalize(Vector2.UnitY) * 14f;
                            SpawnAbilityProjectile(ModContent.ProjectileType<AncientHatchetProjectile>(), Projectile.Center, vel);
                        }
                    }
                    break;
                case 2:
                    // Daybreak Spiral: 12 TinyHatchets in a spiral — evenly
                    // spaced angles, speed rising from 8 to 14.
                    for (int i = 0; i < 12; i++)
                    {
                        float angle = MathHelper.TwoPi * i / 12;
                        float speed = 8f + 6f * (i / 11f);
                        Vector2 vel = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * speed;
                        SpawnAbilityProjectile(ModContent.ProjectileType<TinyHatchet>(), Projectile.Center, vel);
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
