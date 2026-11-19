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
    /// Plantera as a Brigitte's Charm boss minion (2026-11-15). The bulb body
    /// with her real attacks, retargeted at enemies: Seed / Poison Seed /
    /// ThornBall volleys and a chasing charge plus Spore launches. Half scale,
    /// flat 6000 typeless damage, local immunity frames.
    ///
    /// Attack data from the decompiled Terraria 1.4.4.9 NPC AI (aiStyle 51):
    /// see ~/workspace/tmp/boss_attacks_batch1.md, Plantera section.
    ///
    /// SIMPLIFICATIONS (v1):
    /// - No Hooks (NPC 263): terrain-latching has no meaning for a flying
    ///   minion; the Hook-centroid movement tether is replaced with direct
    ///   enemy chasing.
    /// - No Tentacles (NPC 264): not spawned as separate minions.
    /// - Spores (NPC 265) are converted to friendly SporeCloud projectiles
    ///   (ProjectileID.SporeCloud, 228): the vanilla Spore NPC's aiStyle 50
    ///   homes at the PLAYER, so a friendly NPC port would chase its owner.
    ///   SporeCloud is the Spore Sac's friendly homing spore — it drifts
    ///   toward enemies and bursts on contact. Same role, same launch speed.
    /// - Phase thresholds (life fractions) are mapped to minion age: the
    ///   charge + Spores begin after 30 s active; the Poison Seed / ThornBall
    ///   unlock (vanilla: below 80% life) maps to age &gt; 10 s.
    /// - ADAPTATION: seeds keep firing in phase 2 (vanilla restricts them to
    ///   phase 1). A minion that stops shooting after 30 s would waste the
    ///   attack; the charge and Spores are added on top, not instead.
    /// - Enrage (Jungle / surface / Underworld checks) is NOT ported.
    ///
    /// SOUNDS: Plantera's AI plays NO sounds — verified in the decompile, the
    /// only PlaySound calls near her AI belong to the Brain of Cthulhu
    /// (aiStyle 54), not Plantera. No sounds are invented here; there are
    /// intentionally no sound calls in this file.
    ///
    /// DRAWING: vanilla Plantera never flips (aiStyle 51 never sets
    /// spriteDirection) and rotates so the bulb's mouth points at the
    /// player: rotation = atan2(player - Center) + PI/2. The minion
    /// reproduces that exactly — no horizontal flip, rotation offset
    /// by a quarter turn.
    /// </summary>
    public class PlanteraMinion : ModProjectile
    {
        public override string Texture => "Terraria/Images/NPC_262";
        // DisplayName comes from the localization file
        // (Mods.CelestialAerus.Projectiles.PlanteraMinion.DisplayName).

        // --- Constants (vanilla aiStyle 51 values) ---
        private const int MinionDamage = 6000;
        private const float TargetRange = 1000f;

        private const float HoverSpeed = 2.5f;   // vanilla phase-1 move speed
        private const float HoverAccel = 0.025f; // vanilla phase-1 accel
        private const float ChargeSpeed = 5f;    // vanilla phase-2 move speed
        private const float ChargeSpeedLate = 7f; // vanilla phase-2 below 25% life
        private const float ChargeAccel = 0.05f; // vanilla phase-2 accel

        private const int SeedInterval = 80;     // localAI[1] > 80 to fire
        private const float SeedSpeed = 15f;     // 17 Expert; Classic value used
        private const int SporeInterval = 350;   // localAI[1] >= 350 to launch
        private const float SporeSpeed = 8f;     // Spore launch speed

        // Phase mapping (minions have no life fractions, so phases run on age).
        private const int Phase2Age = 1800;      // 30 s: charge + Spores begin
        private const int LatePhase1Age = 600;   // 10 s: Poison Seeds / ThornBalls unlock

        // Distinct hover offset so the boss minions don't stack on one point.
        private static readonly Vector2 HoverOffset = new Vector2(-140f, -180f);

        // --- State (fields; only the owner makes spawn decisions) ---
        private int age;
        private int seedTimer;
        private int sporeTimer;

        public override void SetDefaults()
        {
            // Hitbox matches the 86x86 vanilla hitbox at 0.5 scale.
            Projectile.width = 43;
            Projectile.height = 43;
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
            Projectile.netImportant = true;

            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;

            // The charm's ModPlayer refreshes timeLeft while equipped.
            Projectile.timeLeft = 2;

            Projectile.light = 0.5f;
        }

        public override void AI()
        {
            Player player = Main.player[Projectile.owner];
            if (!player.active || player.dead)
            {
                // Owner gone: drift back to the player and idle.
                HoverToward(player.Center + HoverOffset, HoverSpeed, HoverAccel);
                return;
            }

            age++;

            // Too far from the owner: teleport back (2026-11-18 pattern,
            // shared with the weapon minions).
            if (Vector2.Distance(Projectile.Center, player.Center) > 2000f)
            {
                Projectile.Center = player.Center + HoverOffset;
                Projectile.velocity = Vector2.Zero;
                Projectile.netUpdate = true;
            }
            // FAST FOLLOW (2026-11-18 pattern): fly toward the player at
            // a distance-ramped speed (16-60 px/tick) when lagging behind.
            else if (Vector2.Distance(Projectile.Center, player.Center) > 400f)
            {
                float distToOwner = Vector2.Distance(Projectile.Center, player.Center);
                float followSpeed = MathHelper.Clamp(distToOwner * 0.05f, 16f, 60f);
                Vector2 toPlayer = (player.Center - Projectile.Center).SafeNormalize(Vector2.Zero);
                Projectile.velocity = Vector2.Lerp(Projectile.velocity, toPlayer * followSpeed, 0.2f);
            }

            NPC target = FindTarget(TargetRange);
            bool phase2 = age >= Phase2Age;

            // Animation (vanilla FindFrame, type 262 — 8 frames): frames
            // 0-3 loop at 7 ticks/frame in phase 1; frames 4-7 loop at 7
            // ticks/frame in phase 2, snapping straight to frame 4 on the
            // transition tick. (The minion's phase 2 runs on age, not life.)
            Projectile.frameCounter++;
            if (Projectile.frameCounter > 6)
            {
                Projectile.frameCounter = 0;
                Projectile.frame++;
            }
            if (!phase2)
            {
                if (Projectile.frame > 3)
                    Projectile.frame = 0;
            }
            else
            {
                if (Projectile.frame < 4)
                    Projectile.frame = 4;
                if (Projectile.frame > 7)
                    Projectile.frame = 4;
            }

            // --- Movement ---
            if (phase2 && target != null)
            {
                // Phase 2 charge: chase the target directly (vanilla steers
                // toward a Hook-centroid-anchored point; the tether is dropped
                // and the minion chases the enemy itself).
                float speed = age >= Phase2Age * 2 ? ChargeSpeedLate : ChargeSpeed;
                HoverToward(target.Center, speed, ChargeAccel);
            }
            else
            {
                // Phase 1 hover: drift toward the hover point near the player.
                HoverToward(player.Center + HoverOffset, HoverSpeed, HoverAccel);
            }

            // Face the target (vanilla Plantera's rotation faces the player).
            Vector2 faceDir = target != null
                ? target.Center - Projectile.Center
                : player.Center - Projectile.Center;
            FaceDirection(faceDir);

            // --- Attacks (owner only) ---
            if (Projectile.owner != Main.myPlayer)
                return;

            // Seeds: always available (see the adaptation note in the header).
            // Fire rate accelerates with age (vanilla: life thresholds at
            // 90% / 80% / 70% / 60% each add +1/tick to the timer, mapped
            // to age on the same linear scale as the 80% unlock at 10 s:
            // 90% -> 400, 80% -> 600, 70% -> 1000, 60% -> 1400 ticks).
            // Vanilla fires when the timer EXCEEDS 80 and resets it to 0
            // at the threshold BEFORE the line-of-sight check, so a
            // blocked shot is lost, not deferred to the instant sight
            // opens.
            seedTimer++;
            if (age >= 400)
                seedTimer++;
            if (age >= LatePhase1Age)
                seedTimer++;
            if (age >= 1000)
                seedTimer++;
            if (age >= 1400)
                seedTimer++;
            if (seedTimer > SeedInterval && target != null)
            {
                seedTimer = 0;
                // Vanilla seeds require line of sight.
                if (Collision.CanHit(Projectile.Center, 1, 1, target.Center, 1, 1))
                    FireSeed(target);
            }

            // Spores: phase 2 only. Launch rate accelerates with age
            // (vanilla: +1/tick at each of the 40% / 30% / 20% / 10% thresholds).
            if (phase2 && target != null)
            {
                sporeTimer++;
                if (age >= Phase2Age + 600)
                    sporeTimer++;
                if (age >= Phase2Age + 1200)
                    sporeTimer++;
                if (age >= Phase2Age + 1800)
                    sporeTimer++;
                if (age >= Phase2Age + 2400)
                    sporeTimer++;
                if (sporeTimer >= SporeInterval)
                {
                    LaunchSpore(target);
                    sporeTimer = 0;
                }
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            // The Plantera texture is a sprite sheet with 8 frames stacked
            // vertically (frame count read from the game's own
            // npcFrameCount table in the binary). The count is HARDCODED:
            // Main.npcFrameCount[type] is not reliable at runtime for NPCs
            // that have never spawned, and a 0/1 fallback draws the raw
            // sprite sheet. Projectile.frame is animated in AI() (vanilla
            // FindFrame logic: 0-3 phase 1, 4-7 phase 2).
            const int frameCount = 8;
            Texture2D texture = TextureAssets.Projectile[Projectile.type].Value;
            int frameHeight = texture.Height / frameCount;
            int frameIndex = (int)MathHelper.Clamp(Projectile.frame, 0, frameCount - 1);
            Rectangle frame = new Rectangle(0, frameIndex * frameHeight, texture.Width, frameHeight);
            Vector2 origin = frame.Size() / 2f;
            // Never flipped: vanilla Plantera never sets spriteDirection,
            // and her rotation (atan2 + PI/2, set in AI()) already points
            // the bulb's mouth at the target.
            Main.EntitySpriteDraw(texture, Projectile.Center - Main.screenPosition, frame, lightColor, Projectile.rotation, origin, Projectile.scale, SpriteEffects.None, 0);
            return false;
        }

        private void FireSeed(NPC target)
        {
            // The timer was already reset to 0 by the caller at the
            // threshold (vanilla order); Poison Seeds and ThornBalls push
            // it negative to delay the next shot.
            Vector2 dir = (target.Center - Projectile.Center).SafeNormalize( Vector2.UnitX);
            Vector2 vel = dir * SeedSpeed;
            // Spawned 3 px along the velocity from Center.
            Vector2 muzzle = Projectile.Center + dir * 3f;

            int type = ProjectileID.SeedPlantera;
            int timeLeft = 300;
            // Below 80% life (age > 10 s): 1/4 chance Poison Seed, else
            // 1/8 chance ThornBall, each replacing the Seed on that cycle.
            if (age >= LatePhase1Age)
            {
                if (Main.rand.Next(4) == 0)
                {
                    type = ProjectileID.PoisonSeedPlantera;
                    seedTimer = -30; // +30 ticks of delay before the next shot
                }
                else if (Main.rand.Next(8) == 0)
                {
                    type = ProjectileID.ThornBall;
                    // ThornBall has NO timeLeft override in vanilla — it
                    // bounces around until it expires on its own.
                    timeLeft = -1;
                    seedTimer = -120; // +120 ticks of delay before the next shot
                }
            }

            SpawnFriendlyProjectile(muzzle, vel, type, timeLeft);
        }

        private void LaunchSpore(NPC target)
        {
            // SUBSTITUTION: the vanilla Spore is NPC 265 with aiStyle 50,
            // which homes at the PLAYER — a friendly NPC port would chase its
            // owner. SporeCloud (228) is the Spore Sac's friendly homing
            // spore: it drifts toward enemies and bursts on contact.
            // Vanilla launch math: jitter +/-10 on each axis, then — if
            // the target is above — subtract |dx| * 0.2 from dy BEFORE
            // normalizing, so the lob grows with distance.
            float dx = target.Center.X - Projectile.Center.X + Main.rand.Next(-10, 11);
            float dy = target.Center.Y - Projectile.Center.Y + Main.rand.Next(-10, 11);
            float lob = Math.Abs(dx * 0.2f);
            if (dy > 0f)
                lob = 0f; // target below: no lob
            dy -= lob;
            Vector2 vel = new Vector2(dx, dy).SafeNormalize(Vector2.UnitX) * SporeSpeed;
            SpawnFriendlyProjectile(Projectile.Center, vel, ProjectileID.SporeCloud);
        }

        private void FaceDirection(Vector2 dir)
        {
            // Vanilla Plantera: rotation = atan2(player - Center) + PI/2
            // (the texture's top — the bulb's mouth — points at the
            // target), and she NEVER flips. No spriteDirection, no
            // keep-upright correction: the bulb rotates freely.
            Projectile.rotation = dir.ToRotation() + MathHelper.PiOver2;
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
        /// friendly — vanilla boss projectiles (Seed, ThornBall, ...) default
        /// to hostile, which would damage the player instead of enemies.
        /// Already-friendly projectiles are unaffected by the flip.
        /// Pass timeLeft &lt; 0 to leave the vanilla lifetime alone.
        /// </summary>
        private int SpawnFriendlyProjectile(Vector2 position, Vector2 velocity, int type, int timeLeft = -1)
        {
            if (Projectile.owner != Main.myPlayer)
                return -1;
            int p = Projectile.NewProjectile(Projectile.GetSource_FromThis(), position, velocity, type, MinionDamage, 0f, Projectile.owner);
            if (p >= 0 && p < Main.maxProjectiles)
            {
                Main.projectile[p].friendly = true;
                Main.projectile[p].hostile = false;
                Main.projectile[p].DamageType = DamageClass.Generic;
                Main.projectile[p].usesLocalNPCImmunity = true;
                Main.projectile[p].localNPCHitCooldown = 20;
                if (timeLeft > 0)
                    Main.projectile[p].timeLeft = timeLeft;
            }
            return p;
        }
    }
}
