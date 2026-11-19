using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace CelestialAerus.Projectiles
{
    /// <summary>
    /// Mourning Wood as a Brigitte's Charm boss minion (2026-11-15). The
    /// Pumpkin Moon tree with its real attacks, retargeted at enemies:
    /// Flaming Wood barrages and Greek Fire lob barrages, plus the low-life
    /// rapid variants. Half scale, flat 6000 typeless damage, local
    /// immunity frames.
    ///
    /// Attack data from the decompiled Terraria NPC AI (aiStyle 57, shared
    /// with Everscream): see ~/workspace/tmp/boss_attacks_event.md,
    /// Mourning Wood section. Frame data from boss_frames.md §13 (case 325:
    /// 10 frames, idle 0-4 / walk 5-9, 5 ticks per frame).
    ///
    /// ADAPTATIONS (v1):
    /// - Life tiers map to the OWNER's life fraction (the minion has no life
    ///   of its own): idle shortens below 50% / 25% owner life, and the
    ///   rapid states 3-4 are only picked below 25% owner life. The tree
    ///   "enrages" when its owner is hurt.
    /// - The ground-hugging hover walk is replaced with free flight: the
    ///   minion drifts toward a firing position ~320px from its target at
    ///   the vanilla walk speed (2, or 3/4 by owner-life tier), then stands
    ///   still to fire, exactly as the boss does (flag62).
    /// - The body-slam descend (vanilla: drops onto the player when directly
    ///   above) is NOT ported — a flying minion has no ground to hover over.
    /// - Daytime despawn / flee is NOT ported (do-not-port: a minion has no
    ///   event to flee from).
    /// - Splinterlings are NOT spawned — they come from the Pumpkin Moon
    ///   wave table, not from the boss's AI (verified in the decompile).
    ///
    /// SOUNDS: Mourning Wood's AI plays NO sounds (grep-verified). Its
    /// projectiles are also silent. No sounds are invented here; there are
    /// intentionally no sound calls in this file.
    ///
    /// FIXES (2026-11-18, vs decompiled aiStyle 57): the fire spawn point
    /// was Center + (0, height/2 + 15) — 38.5 px too low, below the tree's
    /// feet. Vanilla spawns at Center + (0, 30) full scale, i.e. (0, 15)
    /// at half scale. Fixed in both fire methods. Follow/recall updated
    /// to the 2026-11-18 weapon-minion pattern (2000 px teleport, ramped
    /// follow speed).
    /// </summary>
    public class MourningWoodMinion : ModProjectile
    {
        public override string Texture => "Terraria/Images/NPC_325";
        // DisplayName comes from the localization file
        // (Mods.CelestialAerus.Projectiles.MourningWoodMinion.DisplayName).

        // --- Constants (vanilla aiStyle 57 values) ---
        private const int MinionDamage = 6000;
        private const float TargetRange = 1000f;
        private const int FrameCount = 10;

        // Attack states (Projectile.ai[0]); Projectile.ai[1] is the timer.
        private const int StateIdle = 0;
        private const int StateFlamingWood = 1;
        private const int StateGreekFire = 2;
        private const int StateRapidWood = 3;
        private const int StateRapidGreekFire = 4;

        private const int IdlePickAt = 300;      // pick an attack at ai[1] >= 300
        private const int FlamingWoodDuration = 120;
        private const int GreekFireDuration = 300;
        private const int RapidWoodDuration = 120;
        private const int RapidGreekFireDuration = 240;

        private const float FlamingWoodSpeed = 10f;
        private const float RapidWoodSpeed = 16f;
        private const float FireDistance = 320f; // stand and fire within this range

        // Distinct hover offset so the boss minions don't stack on one point.
        private static readonly Vector2 HoverOffset = new Vector2(-260f, -60f);

        // Animation timer (own field: Projectile.frameCounter is an int in
        // tML 1.4.4 and the vanilla logic needs a plain counter anyway).
        private int animTimer;

        public override void SetStaticDefaults()
        {
            Main.projFrames[Type] = FrameCount;
        }

        public override void SetDefaults()
        {
            // Hitbox matches the 164x154 vanilla hitbox at 0.5 scale.
            Projectile.width = 82;
            Projectile.height = 77;
            Projectile.scale = 0.5f;

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

            Projectile.light = 0.35f;
            Projectile.netImportant = true;
        }

        public override void AI()
        {
            Player owner = Main.player[Projectile.owner];
            if (owner == null || !owner.active || owner.dead)
            {
                Projectile.velocity *= 0.95f;
                return;
            }

            // --- FOLLOW / RECALL (2026-11-18 weapon-minion pattern): the
            // minion flies back with speed that INCREASES with distance,
            // so it catches up smoothly instead of snapping -- teleport is
            // only the last resort when it is far off-screen (2000px).
            float distToOwner = Vector2.Distance(Projectile.Center, owner.Center);
            if (distToOwner > 2000f)
            {
                Projectile.Center = owner.Center;
                Projectile.velocity = Vector2.Zero;
                Projectile.netUpdate = true;
                for (int i = 0; i < 10; i++)
                {
                    Dust.NewDust(Projectile.position, Projectile.width, Projectile.height, DustID.GoldFlame);
                }
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

            NPC target = FindTarget(TargetRange);
            float ownerLifeFrac = owner.statLifeMax2 > 0 ? (float)owner.statLife / owner.statLifeMax2 : 1f;
            int state = (int)Projectile.ai[0];

            // No target: drop back to idle and hover near the owner.
            if (target == null && state != StateIdle)
            {
                SetState(StateIdle);
                state = StateIdle;
            }

            // Face the target (or the owner when idle).
            Vector2 faceDir = target != null
                ? target.Center - Projectile.Center
                : owner.Center - Projectile.Center;
            Projectile.spriteDirection = faceDir.X < 0f ? -1 : 1;

            // --- Movement ---
            // Walk speed by owner-life tier (vanilla: 2, 3 below 75%, 4 below
            // 50%; states 3-4 force 4).
            float walkSpeed = 2f;
            if (ownerLifeFrac < 0.5f)
                walkSpeed = 4f;
            else if (ownerLifeFrac < 0.75f)
                walkSpeed = 3f;
            bool firingStill = state == StateFlamingWood || state == StateGreekFire;
            if (state == StateRapidWood || state == StateRapidGreekFire)
                walkSpeed = 4f;

            if (target == null)
            {
                HoverToward(owner.Center + HoverOffset, 6f, 0.1f);
            }
            else if (firingStill)
            {
                // flag62: stand still while firing.
                Projectile.velocity *= 0.9f;
                if (Projectile.velocity.LengthSquared() < 0.01f)
                    Projectile.velocity = Vector2.Zero;
            }
            else if (Vector2.Distance(Projectile.Center, target.Center) > FireDistance)
            {
                HoverToward(target.Center, walkSpeed, 0.08f);
            }
            else
            {
                Projectile.velocity *= 0.9f;
                if (Projectile.velocity.LengthSquared() < 0.01f)
                    Projectile.velocity = Vector2.Zero;
            }

            // --- Animation (vanilla FindFrame case 325) ---
            UpdateAnimation();

            // --- Attacks (owner only) ---
            if (Projectile.owner != Main.myPlayer)
                return;

            Projectile.ai[1]++;

            if (state == StateIdle)
            {
                // Idle timer accelerates at low owner life (vanilla: +1/tick
                // below 50% life, +1 more below 25%): idle lasts 300 / 150 /
                // 100 ticks by tier.
                if (ownerLifeFrac < 0.5f)
                    Projectile.ai[1]++;
                if (ownerLifeFrac < 0.25f)
                    Projectile.ai[1]++;

                if (Projectile.ai[1] >= IdlePickAt)
                {
                    int pick;
                    if (ownerLifeFrac < 0.25f)
                        pick = Main.rand.Next(3, 5); // rapid states 3-4
                    else
                        pick = Main.rand.Next(1, 3); // states 1-2
                    SetState(pick);
                }
                return;
            }

            if (target == null)
                return;

            // Attack cadences (vanilla ai[1] windows and moduli, verbatim).
            switch (state)
            {
                case StateFlamingWood:
                    if (Projectile.ai[1] % 15 == 0)
                        FireFlamingWood(target, FlamingWoodSpeed, 20);
                    if (Projectile.ai[1] >= FlamingWoodDuration)
                        SetState(StateIdle);
                    break;

                case StateGreekFire:
                    if (Projectile.ai[1] > 60 && Projectile.ai[1] < 240 && Projectile.ai[1] % 8 == 0)
                        FireGreekFire(target, 10f, 0.004f, 14f, 0.3f, 30);
                    if (Projectile.ai[1] >= GreekFireDuration)
                        SetState(StateIdle);
                    break;

                case StateRapidWood:
                    if (Projectile.ai[1] % 30 == 0)
                        FireFlamingWood(target, RapidWoodSpeed, 2);
                    if (Projectile.ai[1] >= RapidWoodDuration)
                        SetState(StateIdle);
                    break;

                case StateRapidGreekFire:
                    if (Projectile.ai[1] % 10 == 0)
                        FireGreekFire(target, 12f, 0.002f, 16f, 0.2f, 15);
                    if (Projectile.ai[1] >= RapidGreekFireDuration)
                        SetState(StateIdle);
                    break;
            }
        }

        private void SetState(int state)
        {
            Projectile.ai[0] = state;
            Projectile.ai[1] = 0;
            Projectile.netUpdate = true;
        }

        private void UpdateAnimation()
        {
            // Vanilla FindFrame case 325: idle (velocity.X == 0) loops frames
            // 0-4; walking loops frames 5-9. Both advance at 5 ticks/frame.
            animTimer++;
            if (animTimer > 4)
            {
                animTimer = 0;
                Projectile.frame++;
            }
            if (Projectile.velocity.X == 0f)
            {
                if (Projectile.frame > 4)
                    Projectile.frame = 0;
            }
            else
            {
                if (Projectile.frame < 5)
                    Projectile.frame = 5;
                if (Projectile.frame > 9)
                    Projectile.frame = 5;
            }
        }

        private void FireFlamingWood(NPC target, float speed, int jitterPercent)
        {
            // Spawn from low on the trunk (vanilla aiStyle 57: position +
            // (width/2, height/2 + 30) = Center + (0, 30) full scale;
            // halved to (0, 15) for the 0.5-scale minion), aimed at the
            // target's top-center.
            Vector2 spawn = Projectile.Center + new Vector2(0f, 15f);
            Vector2 aimPoint = new Vector2(target.Center.X, target.position.Y);
            Vector2 vel = (aimPoint - spawn).SafeNormalize(Vector2.UnitX) * speed;
            // Per-axis jitter: x(0.80-1.20) for Attack 1, x(0.98-1.02) for
            // Attack 3 (jitterPercent is the +/- range in percent).
            vel.X *= 1f + Main.rand.Next(-jitterPercent, jitterPercent + 1) * 0.01f;
            vel.Y *= 1f + Main.rand.Next(-jitterPercent, jitterPercent + 1) * 0.01f;
            SpawnFriendlyProjectile(spawn, vel, ProjectileID.FlamingWood);
        }

        private void FireGreekFire(NPC target, float baseSpeed, float distScale, float maxSpeed, float arcScale, int jitterPercent)
        {
            // Same spawn as Flaming Wood (vanilla: Center + (0, 30) full
            // scale, halved to (0, 15)).
            Vector2 spawn = Projectile.Center + new Vector2(0f, 15f);
            Vector2 aimPoint = new Vector2(target.Center.X, target.position.Y);
            float dx = Math.Abs(target.Center.X - spawn.X);
            float speed = Math.Min(baseSpeed + dx * distScale, maxSpeed);

            // Arcing lob: upward bias scaled by horizontal distance plus a
            // random extra lift, and +/-50px horizontal jitter. Copy the
            // formula verbatim — the shots land short/past by design.
            Vector2 aim = aimPoint - spawn;
            aim.Y -= dx * arcScale;
            aim.Y -= Main.rand.Next(50, 201);
            aim.X += Main.rand.Next(-50, 51);
            Vector2 vel = aim.SafeNormalize(Vector2.UnitX) * speed;
            vel.X *= 1f + Main.rand.Next(-jitterPercent, jitterPercent + 1) * 0.01f;
            vel.Y *= 1f + Main.rand.Next(-jitterPercent, jitterPercent + 1) * 0.01f;

            int type = Main.rand.Next(ProjectileID.GreekFire1, ProjectileID.GreekFire3 + 1);
            SpawnFriendlyProjectile(spawn, vel, type);
        }

        private void HoverToward(Vector2 target, float maxSpeed, float accel)
        {
            Vector2 to = target - Projectile.Center;
            if (to.LengthSquared() > 1f)
            {
                to.Normalize();
                Projectile.velocity += to * accel;
                if (Projectile.velocity.Length() > maxSpeed)
                    Projectile.velocity = Vector2.Normalize(Projectile.velocity) * maxSpeed;
            }
            else
            {
                Projectile.velocity *= 0.95f;
            }
        }

        private NPC FindTarget(float range)
        {
            NPC best = null;
            float bestDistSq = range * range;
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

        /// <summary>
        /// Spawns a projectile owned by this minion's owner, flipped to
        /// friendly with local immunity frames — vanilla boss projectiles
        /// default to hostile, which would damage the player instead of
        /// enemies. ai0/ai1/ai2 are passed through to the spawn call.
        /// </summary>
        private int SpawnFriendlyProjectile(Vector2 position, Vector2 velocity, int type, float ai0 = 0f, float ai1 = 0f, float ai2 = 0f)
        {
            if (Projectile.owner != Main.myPlayer)
                return -1;
            int p = Projectile.NewProjectile(Projectile.GetSource_FromThis(), position, velocity, type, MinionDamage, 0f, Projectile.owner, ai0, ai1, ai2);
            if (p >= 0 && p < Main.maxProjectiles)
            {
                Main.projectile[p].friendly = true;
                Main.projectile[p].hostile = false;
                Main.projectile[p].usesLocalNPCImmunity = true;
                Main.projectile[p].localNPCHitCooldown = 20;
            }
            return p;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D texture = TextureAssets.Projectile[Projectile.type].Value;
            // Frame count is HARDCODED (10 — Mourning Wood's texture has 10
            // frames; see boss_frames.md §13, confirmed against the
            // Main.npcFrameCount binary table). NEVER Main.npcFrameCount at
            // runtime: it returns 0/1 for unloaded NPCs, which draws the
            // whole sprite sheet.
            int frameHeight = texture.Height / FrameCount;
            Rectangle frame = new Rectangle(0, Projectile.frame * frameHeight, texture.Width, frameHeight);
            Vector2 origin = frame.Size() / 2f;
            SpriteEffects effects = Projectile.spriteDirection == -1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            Main.EntitySpriteDraw(texture, Projectile.Center - Main.screenPosition, frame, lightColor, Projectile.rotation, origin, Projectile.scale, effects, 0);
            return false;
        }
    }
}
