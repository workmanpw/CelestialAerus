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
    /// Everscream as a Brigitte's Charm boss minion (2026-11-15). The Frost
    /// Moon tree with its real attacks, retargeted at enemies: the Pine
    /// Needle firehose (36 needles in 3s) and the Ornament lob barrage
    /// (which bursts into shrapnel on fuse expiry). Half scale, flat 6000
    /// typeless damage, local immunity frames.
    ///
    /// Attack data from the decompiled Terraria NPC AI (aiStyle 57, shared
    /// with Mourning Wood — Everscream is the type == 344 branches):
    /// see ~/workspace/tmp/boss_attacks_event.md, Everscream section.
    /// Frame data: FindFrame case 344 (10 frames, continuous 0-9 loop,
    /// 6 ticks/frame standing, speed-scaled while moving). NOTE: the
    /// boss_frames.md file mislabels this case "Ice Queen" — SetDefaults
    /// (line 13497) proves type 344 is Everscream (172x130, aiStyle 57);
    /// the walker animation in boss_frames.md §15 belongs to the Yeti
    /// (type 343), not Everscream. This file uses the case-344 loop.
    ///
    /// ADAPTATIONS (v1):
    /// - Life tiers map to the OWNER's life fraction (the minion has no life
    ///   of its own): idle shortens below 50% / 25% owner life, and the
    ///   walk speed rises (2 / 3 / 4). Everscream never enters states 3-4
    ///   (vanilla: the low-life picker requires type != 344).
    /// - The ground-hugging hover walk is replaced with free flight: the
    ///   minion drifts toward a firing position ~320px from its target at
    ///   the vanilla walk speed, then stands still to fire (flag62).
    /// - Daytime despawn / flee is NOT ported (do-not-port).
    ///
    /// SOUNDS: Everscream's AI plays NO sounds (grep-verified). Its
    /// projectiles do: PineNeedleHostile and OrnamentHostile play
    /// SoundID.Item1 once at spawn, and OrnamentHostile plays SoundID.Item27
    /// when it bursts — those come free from the vanilla projectile AI.
    /// At 12 needles/second the Item1 clicks will be near-continuous; that
    /// is the boss's real sound profile, kept as-is.
    ///
    /// FIXES (2026-11-18, vs decompiled aiStyle 57): in FireNeedle the
    /// upward bias (-|dx| * rand(0-20%)) was computed from the PRE-jitter
    /// horizontal distance. Vanilla adds the +/-50 px jitter to the aim
    /// FIRST and computes the bias from the POST-jitter |dx|. Reordered.
    /// (The Ornament and Greek Fire lobs use pre-jitter dx in vanilla —
    /// those were already correct.) Follow/recall updated to the
    /// 2026-11-18 weapon-minion pattern (2000 px teleport, ramped
    /// follow speed).
    /// </summary>
    public class EverscreamMinion : ModProjectile
    {
        public override string Texture => "Terraria/Images/NPC_344";
        // DisplayName comes from the localization file
        // (Mods.CelestialAerus.Projectiles.EverscreamMinion.DisplayName).

        // --- Constants (vanilla aiStyle 57 values, type == 344 branches) ---
        private const int MinionDamage = 6000;
        private const float TargetRange = 1000f;
        private const int FrameCount = 10;

        // Attack states (Projectile.ai[0]); Projectile.ai[1] is the timer.
        private const int StateIdle = 0;
        private const int StateNeedles = 1;
        private const int StateOrnaments = 2;

        private const int IdlePickAt = 300;      // pick an attack at ai[1] >= 300
        private const int NeedleDuration = 180;
        private const int OrnamentDuration = 300;

        private const float NeedleSpeed = 12.5f;
        private const float FireDistance = 320f; // stand and fire within this range

        // Distinct hover offset so the boss minions don't stack on one point.
        private static readonly Vector2 HoverOffset = new Vector2(260f, -60f);

        // Animation counter (own field — the vanilla logic accumulates a
        // fractional, speed-scaled counter; Projectile.frameCounter is an
        // int in tML 1.4.4).
        private float animCounter;

        public override void SetStaticDefaults()
        {
            Main.projFrames[Type] = FrameCount;
        }

        public override void SetDefaults()
        {
            // Hitbox matches the 172x130 vanilla hitbox at 0.5 scale.
            Projectile.width = 86;
            Projectile.height = 65;
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
            // 50%). The tree stands still while firing either attack (flag62).
            float walkSpeed = 2f;
            if (ownerLifeFrac < 0.5f)
                walkSpeed = 4f;
            else if (ownerLifeFrac < 0.75f)
                walkSpeed = 3f;
            bool firing = state == StateNeedles || state == StateOrnaments;

            if (target == null)
            {
                HoverToward(owner.Center + HoverOffset, 6f, 0.1f);
            }
            else if (firing)
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

            // --- Animation (vanilla FindFrame case 344) ---
            UpdateAnimation();

            // Cosmetic: during the needle spray, each tick has a 1-in-5
            // chance to spawn a dust-245 mote somewhere on the body.
            if (state == StateNeedles && Main.rand.Next(5) == 0)
            {
                int d = Dust.NewDust(Projectile.position, Projectile.width, Projectile.height, 245);
                if (d >= 0)
                {
                    Main.dust[d].velocity *= 0.2f;
                    Main.dust[d].scale = 0.25f;
                }
            }

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
                    // Everscream ALWAYS picks 1-2 (the low-life picker
                    // requires type != 344, so states 3-4 are unreachable).
                    SetState(Main.rand.Next(1, 3));
                }
                return;
            }

            if (target == null)
                return;

            // Attack cadences (vanilla ai[1] windows and moduli, verbatim).
            switch (state)
            {
                case StateNeedles:
                    if (Projectile.ai[1] % 5 == 0)
                        FireNeedle(target);
                    if (Projectile.ai[1] >= NeedleDuration)
                        SetState(StateIdle);
                    break;

                case StateOrnaments:
                    if (Projectile.ai[1] > 60 && Projectile.ai[1] < 240 && Projectile.ai[1] % 15 == 0)
                        FireOrnament(target);
                    if (Projectile.ai[1] >= OrnamentDuration)
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
            // Vanilla FindFrame case 344: one unconditional 0-9 loop.
            // Standing still: +1/tick, advance at 6 ticks/frame. Moving:
            // +Max(1, |vX| * 0.75)/tick — faster movement animates faster.
            if (Projectile.velocity.X == 0f)
                animCounter += 1f;
            else
                animCounter += Math.Max(1f, Math.Abs(Projectile.velocity.X) * 0.75f);
            if (animCounter > 5.0f)
            {
                animCounter = 0f;
                Projectile.frame++;
            }
            if (Projectile.frame > 9)
                Projectile.frame = 0;
        }

        private void FireNeedle(NPC target)
        {
            // Spray from a random point inside the body (vanilla: 20px inset,
            // scaled to half size), aimed at the target's top-center with
            // +/-50px jitter per axis and an upward bias of -|dx| * rand(0-20%).
            Vector2 spawn = Projectile.position + new Vector2(
                10f + Main.rand.Next(Math.Max(1, Projectile.width - 20)),
                10f + Main.rand.Next(Math.Max(1, Projectile.height - 20)));
            Vector2 aimPoint = new Vector2(target.Center.X, target.position.Y);
            Vector2 aim = aimPoint - spawn;
            aim.X += Main.rand.Next(-50, 51);
            aim.Y += Main.rand.Next(-50, 51);
            // Vanilla order: the jitter is added FIRST, and the upward
            // bias uses the POST-jitter |dx| (aiStyle 57: num996 -=
            // |num995| * rand(0-20%) after num995 += rand(-50, 51)).
            float dx = Math.Abs(aim.X);
            aim.Y -= dx * (Main.rand.Next(0, 21) * 0.01f);
            Vector2 vel = aim.SafeNormalize(Vector2.UnitX) * NeedleSpeed;
            // Loosest spread in the file: per-axis jitter x(0.60-1.40).
            vel.X *= 1f + Main.rand.Next(-20, 21) * 0.02f;
            vel.Y *= 1f + Main.rand.Next(-20, 21) * 0.02f;
            // FAITHFUL QUIRK: ai[0] is a random 0-30 seed. A needle that
            // rolls ai[0] == 1 (~1 in 31) falls with gravity 0.41/tick
            // instead of flying straight (AI_001's generic tail branch).
            // Kept verbatim — it is the boss's real behavior.
            SpawnFriendlyProjectile(spawn, vel, ProjectileID.PineNeedleHostile, Main.rand.Next(0, 31));
        }

        private void FireOrnament(NPC target)
        {
            // High lob from mid-body (vanilla: random X inset 20px, Y at
            // position.Y + 60 + rand(height - 80) — scaled to half size).
            Vector2 spawn = new Vector2(
                Projectile.position.X + 10f + Main.rand.Next(Math.Max(1, Projectile.width - 20)),
                Projectile.position.Y + 30f + Main.rand.Next(Math.Max(1, Projectile.height - 40)));
            Vector2 aimPoint = new Vector2(target.Center.X, target.position.Y);
            float dx = Math.Abs(target.Center.X - spawn.X);
            // Base 4.5 + |dx| * 0.004 with NO cap (unlike Greek Fire).
            float speed = 4.5f + dx * 0.004f;

            Vector2 aim = aimPoint - spawn;
            aim.Y -= dx * 0.3f;
            aim.Y -= Main.rand.Next(50, 201);
            aim.X += Main.rand.Next(-50, 51);
            Vector2 vel = aim.SafeNormalize(Vector2.UnitX) * speed;
            vel.X *= 1f + Main.rand.Next(-30, 31) * 0.01f;
            vel.Y *= 1f + Main.rand.Next(-30, 31) * 0.01f;

            // ai[1] is the variant: frame 0 glows warm, frame 1 neutral.
            // The shrapnel burst (5x OrnamentHostileShrapnel on fuse expiry)
            // comes free from the vanilla projectile AI.
            SpawnFriendlyProjectile(spawn, vel, ProjectileID.OrnamentHostile, 0f, Main.rand.Next(2));
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
            // Frame count is HARDCODED (10 — Everscream's texture has 10
            // frames; FindFrame case 344 wraps at frame 9, confirmed against
            // the Main.npcFrameCount binary table). NEVER Main.npcFrameCount
            // at runtime: it returns 0/1 for unloaded NPCs, which draws the
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
