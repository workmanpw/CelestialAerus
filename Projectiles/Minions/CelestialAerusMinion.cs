using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CelestialAerus.Projectiles
{
    /// <summary>
    /// Celestial Aerus weapon minion (Brigitte's Charm, 2026-11-15): the
    /// mod's own Celestial Aerus as a living weapon. Unique attack —
    /// "Dual Mode": it alternates every 300 ticks between Fireball mode
    /// (a CelestialFireball at the target every 30 ticks) and Boomerang
    /// mode (a ThrownCelestialAerus at the target every 60 ticks, which
    /// sticks, homes, and returns on its own AI). Visual cue: the
    /// weapon spins FAST in Boomerang mode and SLOW in Fireball mode.
    ///
    /// Abilities (2026-11-15, Peyton's order): three extra attacks on a
    /// 240-tick cycle — Twin Orbit (a thrown Aerus at each of the 2
    /// nearest enemies), Fireball Spiral (8 fireballs in a spiral),
    /// Aerus Rush (the minion itself dashes at the target 3 times).
    /// All hit for the same flat 6000 typeless damage as the primary
    /// attack (the rush deals contact damage through the minion's own
    /// hitbox).
    ///
    /// Charm spec: 40x40 hitbox, scale 1.0 (item-sized), flat 6000
    /// typeless damage, local immunity frames (20-tick cooldown),
    /// tileCollide false, timeLeft 2 (refreshed by the charm).
    /// </summary>
    public class CelestialAerusMinion : ModProjectile
    {
        public override string Texture => "CelestialAerus/Assets/Textures/CelestialAerus";

        private const float TargetRange = 1000f;
        private const int MinionDamage = 6000;     // flat, typeless (charm design)

        private static readonly Vector2 IdleOffset = new Vector2(240f, -180f);

        // Fight hover: 150 px above the target (close enough that the
        // fireball's 750 px homing pickup and the TCA's 1000 px homing
        // both engage reliably).
        private const float FightHoverHeight = 150f;

        // Dual Mode: alternate every 300 ticks.
        private const int ModeDuration = 300;
        private const int FireballInterval = 30;
        private const float FireballSpeed = 14f;
        private const int BoomerangInterval = 60;
        private const float BoomerangSpeed = 14f;

        // Rotation cue: fast spin in Boomerang mode, slow in Fireball.
        private const float FireballSpin = 0.05f;
        private const float BoomerangSpin = 0.4f;

        // Modes (Projectile.ai[0], synced).
        private const int ModeFireball = 0;
        private const int ModeBoomerang = 1;

        // Ability cycle: one ability every 240 ticks (4 seconds).
        private const int AbilityInterval = 240;

        // Aerus Rush (ability 3): 3 dashes, 20 ticks each — 10 ticks of
        // full-speed dash at 24 px/tick, then 10 ticks coasting. The
        // remaining dash count lives in Projectile.ai[1] so it syncs;
        // the per-dash timer is local.
        private const int RushDashCount = 3;
        private const int RushDashTicks = 20;
        private const int RushDashActiveTicks = 10;
        private const float RushSpeed = 24f;

        // --- State ---
        private NPC target = null;
        private int retargetTimer = 0;
        private int modeTimer = 0;
        private int attackTimer = 0;
        private int rushTimer = 0;

        // Stagger offset 144: each weapon starts its ability cycle at a
        // different point so all five don't fire on the same tick.
        private int abilityTimer = 144;
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

            // Mode timer runs regardless of target (the mode keeps
            // alternating even while idle, so the switch is never
            // "wasted" waiting for an enemy).
            int mode = (int)Projectile.ai[0];
            modeTimer++;
            if (modeTimer >= ModeDuration)
            {
                modeTimer = 0;
                attackTimer = 0;
                mode = mode == ModeFireball ? ModeBoomerang : ModeFireball;
                Projectile.ai[0] = mode;
                Projectile.netUpdate = true;
            }

            // Rotation cue: fast spin in Boomerang mode, slow in Fireball.
            Projectile.rotation += mode == ModeBoomerang ? BoomerangSpin : FireballSpin;

            // Aerus Rush (ability 3): while dashes remain, the rush
            // overrides normal movement. If the target died mid-rush,
            // the rush cancels and normal behavior resumes.
            if ((int)Projectile.ai[1] > 0)
            {
                HandleRush();
                if ((int)Projectile.ai[1] > 0)
                    return;
            }

            if (target == null)
            {
                SteerToward(owner.Center + IdleOffset, 8f, 0.2f);
                return;
            }

            // Hover 150 px above the target.
            SteerToward(target.Center + new Vector2(0f, -FightHoverHeight), 10f, 0.25f);

            // Attack on the mode's cadence.
            attackTimer++;
            if (mode == ModeFireball)
            {
                if (attackTimer >= FireballInterval)
                {
                    attackTimer = 0;
                    FireAtTarget(ModContent.ProjectileType<CelestialFireball>(), FireballSpeed);
                }
            }
            else
            {
                if (attackTimer >= BoomerangInterval)
                {
                    attackTimer = 0;
                    FireAtTarget(ModContent.ProjectileType<ThrownCelestialAerus>(), BoomerangSpeed);
                }
            }
        }

        private void FireAtTarget(int type, float speed)
        {
            if (Projectile.owner != Main.myPlayer)
                return;
            if (target == null || !target.active)
                return;
            Vector2 vel = (target.Center - Projectile.Center).SafeNormalize(Vector2.UnitY) * speed;
            SpawnFriendlyProjectile(type, Projectile.Center, vel);
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
                    // Twin Orbit: throw a ThrownCelestialAerus at each of
                    // the 2 nearest enemies.
                    {
                        List<NPC> nearest = FindNearestTargets(2, TargetRange);
                        foreach (NPC enemy in nearest)
                        {
                            Vector2 vel = (enemy.Center - Projectile.Center).SafeNormalize(Vector2.UnitY) * BoomerangSpeed;
                            SpawnAbilityProjectile(ModContent.ProjectileType<ThrownCelestialAerus>(), Projectile.Center, vel);
                        }
                    }
                    break;
                case 1:
                    // Fireball Spiral: 8 CelestialFireballs in a spiral —
                    // evenly spaced angles, speed rising from 10 to 16.
                    for (int i = 0; i < 8; i++)
                    {
                        float angle = MathHelper.TwoPi * i / 8;
                        float speed = 10f + 6f * (i / 7f);
                        Vector2 vel = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * speed;
                        SpawnAbilityProjectile(ModContent.ProjectileType<CelestialFireball>(), Projectile.Center, vel);
                    }
                    break;
                case 2:
                    // Aerus Rush: three quick dashes at the target — the
                    // dash count goes in ai[1] (synced) and AI() runs it.
                    Projectile.ai[1] = RushDashCount;
                    rushTimer = 0;
                    Projectile.netUpdate = true;
                    break;
            }
        }

        // One tick of the Aerus Rush: 10 ticks of full-speed dash toward
        // the target, then 10 ticks coasting, then the next dash. Contact
        // damage is the minion's own hitbox (6000, local frames).
        private void HandleRush()
        {
            if (target == null || !target.active)
            {
                Projectile.ai[1] = 0;
                Projectile.netUpdate = true;
                return;
            }
            rushTimer++;
            if (rushTimer <= RushDashActiveTicks)
            {
                Vector2 dir = (target.Center - Projectile.Center).SafeNormalize(Vector2.Zero);
                Projectile.velocity = dir * RushSpeed;
            }
            else
            {
                Projectile.velocity *= 0.85f;
            }
            Projectile.rotation += 0.5f;
            if (rushTimer >= RushDashTicks)
            {
                rushTimer = 0;
                Projectile.ai[1] -= 1;
                Projectile.netUpdate = true;
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
