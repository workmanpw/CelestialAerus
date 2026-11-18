using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace CelestialAerus.Projectiles
{
    /// <summary>
    /// Spazmatism boss minion (Brigitte's Charm, 2026-11-15): the fire eye
    /// of the Twins, rewritten as a friendly minion. It fires friendly
    /// versions of its real attacks (CursedFlameHostile in phase 1, the
    /// EyeFire flamethrower breath in phase 2) at the owner's enemies,
    /// dashes like the real boss, and plays the boss's sounds at the same
    /// moments (including the Item34 flamethrower loop and the roar on
    /// every phase-2 dash start).
    /// Attack data from the decompiled Terraria 1.4.4.9 AI_031
    /// (see ~/workspace/tmp/boss_attacks_batch1.md, Part 2 section 2).
    ///
    /// FIXED 2026-11-18 (vs decompiled vanilla Terraria 1.4.5 NPC.cs
    /// aiStyle 31, lines 32342-32979, and Main.DrawNPC):
    /// - CURSED FLAME SPAWN-AHEAD: vanilla spawns at center + VELOCITY x
    ///   4 (48-56 px ahead). The backup spawned at center + direction x
    ///   4 — 4 px, inside the eye. Fixed (using the final jittered
    ///   velocity like vanilla).
    /// - EYE FIRE SPAWN: vanilla spawns the flamethrower at center -
    ///   VELOCITY x 1 (6-10 px BEHIND the eye, the mouth trailing the
    ///   stream). The backup spawned at center - direction — 1 px.
    ///   Fixed (using the final velocity, including the eye-velocity
    ///   inheritance and jitter, like vanilla).
    /// - DASH ROTATIONS: vanilla sets rotation = atan2(velocity) - 1.57
    ///   during the dash windows (first 7 ticks of the P1 dash, first 49
    ///   of the P2 dash — mouth-first), decays after (P1 x0.9, P2 x0.93)
    ///   with near-zero components zeroed, and snaps to face the target
    ///   at dash end. The backup set +Pi/2 (180 degrees wrong) for the
    ///   whole window, never zeroed, never snapped, and never eased
    ///   during the decay (vanilla's top-of-AI easing at 0.15 applies).
    ///   Fixed in both dash recoveries.
    /// - P2 DASH EXPERT TIMER: vanilla's P2 dash timer runs 1.5x in
    ///   Expert (ai[2] += 0.5 extra per tick), so the decay starts at
    ///   tick 34 and the cycle ends at tick 54. The backup used the
    ///   right Expert cycle (54) but the WRONG decay boundary (fixed 50
    ///   ticks in both modes — 16 ticks too long in Expert). Now a float
    ///   dash timer advancing 1.5/tick in Expert, with the vanilla
    ///   thresholds (decay at 50, cycle at 80).
    /// - TRANSFORM GORE MISSING: vanilla spawns gore 144, 7, 6 (x2 each)
    ///   at the transform midpoint — note 144, not Retinazer's 143. The
    ///   backup's comment said "gore burst" but the code only did dust.
    ///   Added (at the hitbox top-left, velocity +/-3 halved, scale 0.5).
    ///   Also added the 1 dust/tick type-5 mote vanilla spawns during
    ///   the whole transform, and the velocity zeroing.
    /// - P2 HOVER TIERS: vanilla measures the 300-800 px Expert speed
    ///   tiers from the distance to the HOVER POINT (num578), not to the
    ///   target. The backup measured to the target's center. Fixed.
    /// - AFTERIMAGES MISSING: vanilla draws 5 ghosts for type 126
    ///   (oldPos[9,7,5,3,1], alpha (10-n)/20, current frame/rotation).
    ///   Added (TrailCacheLength = 10; oldPos cleared on recall
    ///   teleport). Spazmatism has NO glow overlay in vanilla (only
    ///   Retinazer gets the EyeLaser glow) — none added.
    /// - AMBIENT DUST: vanilla's 1-in-5 mote comes from the eye's middle
    ///   band (position.X, position.Y + height*0.25, width x height*0.5)
    ///   with velocity (vel.X, 2), dust velocity scaled x0.5 / x0.1 —
    ///   the same as the Eye of Cthulhu. The backup used the full body,
    ///   zero velocity, white color. Fixed.
    /// - P1 FIRE TIMER: vanilla zeroes ai[3] at the hover-&gt;dash
    ///   transition. The backup let it carry over. Fixed.
    /// - DRAW OFFSET: vanilla draws the sprite center 4 px below the
    ///   NPC center (Main.DrawNPC literal +4). Applied as +4 * scale.
    /// - Added Projectile.netImportant = true (weapon-minion pattern).
    ///
    /// VERIFIED CORRECT (no change): frame cycle 0-1-2 at 7 t/f shifting
    /// +3 in phase 2 (FindFrame 90898, shared with the Eye), 6 frames,
    /// 50x55 halved hitbox, P1 hover 12/0.4 (no Expert change) at +/-400
    /// X / same Y, P1 flame speed 12/14 with +/-2 jitter and 60-tick
    /// cooldown (no positional gate), P1 dash 13 speed (15.8 Expert max,
    /// life-based — the minion uses the max), 10 dashes, 42-tick cycle,
    /// 8-tick window, x0.9 decay, P2 hover 4/0.1 at +/-180 X with the
    /// Expert distance tiers (+0.5/+0.5/+0.55/+0.55/+0.6/+0.6), 400
    /// ticks, flamethrower speed 6 with +/-0.4 jitter and 50% eye
    /// velocity inheritance firing every 9 ticks at threshold 8, Item34
    /// every 23 ticks while breathing, P2 dash 14/16.5 speed, 6 dashes,
    /// 80-tick cycle, 50-tick window, x0.93 decay, Roar at each P2 dash
    /// start, P2 hover rotation easing at 0.15 toward +Pi/2 (vanilla has
    /// NO rotation override in Spazmatism's P2 — unlike Retinazer — so
    /// the easing is correct here), transform 2x100 ticks with spin ramp
    /// 0.005, NPCHit1 + Roar at the midpoint.
    ///
    /// Minion adaptations (the boss has no minion equivalent for these):
    /// - Phase transitions are on a TIMER (20 s per phase), not life
    ///   fraction — minions have no meaningful life total. The 200-tick
    ///   spin transformation (with the crunch + roar at the midpoint) is
    ///   kept when entering phase 2. Returning to phase 1 is a direct
    ///   switch (the real boss never goes back).
    /// - The Expert fire-rate/life-threshold accelerations are dropped
    ///   (no life fraction); base rates are used. The flamethrower fires
    ///   at the high-life base rate (1 per 9 ticks). Expert speeds are
    ///   used in Expert/Master worlds, Classic speeds otherwise.
    /// - Despawn-at-daylight and twin-coordination code are dropped.
    /// - All damage is flat 6000, typeless (DamageClass.Generic), per the
    ///   charm's design — the vanilla damage values in the spec are
    ///   informational only.
    /// - CursedFlameHostile and EyeFire keep their projectile-side Cursed
    ///   Inferno effect: as friendly projectiles the debuff applies to
    ///   enemies instead of the player (verified in Projectile.cs).
    /// - Sound correction: the spec's Part 2 resolved the transformation
    ///   crunch (legacy PlaySound(3, ...)) as "NPCKilled", but the
    ///   decompiled SoundID shows type 3 is the NPCHit bank (NPCHit1 =
    ///   LegacySoundStyle(3, 1)) and NPCKilled is the int const 4 (the
    ///   NPCDeath bank). Part 1 (Eye of Cthulhu, same call) resolved it
    ///   to NPCHit1 — that is what plays here.
    /// </summary>
    public class SpazmatismMinion : ModProjectile
    {
        // --- Spec constants (vanilla AI_031, Spazmatism) ---
        private const float TargetRange = 1000f;
        private const int PhaseDuration = 1200;    // 20 s per phase (minion adaptation)
        private const int TransformStageDuration = 100; // 100 ticks spin-up + 100 spin-down

        // Phase 1 hover
        private const float P1HoverSpeed = 12f;    // no Expert change
        private const float P1HoverAccel = 0.4f;
        private const int P1HoverDuration = 600;   // 10 s, then dash
        private const float P1HoverSideOffset = 400f;

        // Phase 1 Cursed Flame
        private const float P1FlameSpeed = 12f;
        private const float P1FlameSpeedExpert = 14f;
        private const int P1FlameCooldown = 60;    // 1 s
        private const float P1FlameJitter = 2f;    // +/- per axis
        private const float P1FlameSpawnAhead = 4f; // spawn = center + VELOCITY x 4

        // Phase 1 dash (10 rapid dashes)
        private const float P1DashSpeed = 13f;
        private const float P1DashSpeedExpert = 15.8f; // max at <50% life (Expert)
        private const int P1DashCount = 10;
        private const int P1DashCycle = 42;        // ticks per dash cycle
        private const int P1DashDecayAfter = 8;    // velocity decays from tick 8
        private const float P1DashDecay = 0.9f;

        // Phase 2 hover / flamethrower
        private const float P2HoverSpeed = 4f;     // slow — parks next to the target
        private const float P2HoverAccel = 0.1f;
        private const int P2HoverDuration = 400;   // ~6.7 s, then dash
        private const float P2HoverSideOffset = 180f;
        private const int P2FlameThreshold = 8;    // ~1 fireball per 9 ticks (base rate)
        private const float P2FlameSpeed = 6f;
        private const float P2FlameJitter = 0.4f;  // +/- per axis
        private const int P2FlameSoundInterval = 22; // Item34 every ~23 ticks while breathing

        // Phase 2 dash (6 slower dashes, roar at each start)
        private const float P2DashSpeed = 14f;
        private const float P2DashSpeedExpert = 16.5f;
        private const int P2DashCount = 6;
        // The P2 dash timer is a FLOAT (p2DashTimer): it advances 1/tick
        // in Classic and 1.5/tick in Expert (vanilla ai[2] += 0.5 extra
        // in Expert), so the decay (50) and cycle (80) land at tick 34 /
        // 54 in Expert instead of 50 / 80.
        private const float P2DashCycle = 80f;
        private const float P2DashDecayAfter = 50f;
        private const float P2DashDecay = 0.93f;

        private const float RotationTurnRate = 0.15f; // rad/tick (faster than Retinazer)

        private const int MinionDamage = 6000;     // flat, typeless (charm design)

        // Roar (legacy sound type 15, style 0). SoundID.Roar is a const int,
        // not a SoundStyle — use the path-based SoundStyle instead.
        private static readonly SoundStyle RoarSound = new SoundStyle("Terraria/Sounds/Roar_0");

        // Hover offset near the player (no target): each boss minion gets a
        // distinct offset so the entourage doesn't stack on one point.
        private static readonly Vector2 PlayerHoverOffset = new Vector2(-220f, -260f);

        // --- State ---
        // Projectile.ai[0] = phase: 0 = phase 1, 1 = transform stage 1,
        // 2 = transform stage 2, 3 = phase 2.
        // Projectile.ai[1] = sub-state within the phase.
        private int phaseTimer = 0;
        private int stateTimer = 0;
        private int fireTimer = 0;
        private int flameSoundTimer = 0;
        private int dashCounter = 0;
        private float p2DashTimer = 0f; // float timer for the P2 dash (1.5x in Expert)
        private int spinTimer = 0;
        private float spinRate = 0f;
        private int retargetTimer = 0;
        private NPC target = null;

        public override string Texture => "Terraria/Images/NPC_126";

        public override void SetStaticDefaults()
        {
            // Vanilla draws 5 afterimages for Spazmatism (Main.DrawNPC,
            // type 126) from oldPos[9,7,5,3,1] — the trail cache must be
            // long enough to hold them.
            ProjectileID.Sets.TrailCacheLength[Type] = 10;
        }

        public override void SetDefaults()
        {
            // Vanilla Spazmatism hitbox 100x110, halved for the minion (0.5 scale).
            Projectile.width = 50;
            Projectile.height = 55;
            Projectile.scale = 0.5f;

            Projectile.friendly = true;
            Projectile.hostile = false;
            Projectile.minion = true;
            Projectile.minionSlots = 1;
            Projectile.netImportant = true;
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

            Projectile.light = 0.5f;
        }

        public override void AI()
        {
            Player owner = Main.player[Projectile.owner];
            if (owner == null || !owner.active)
            {
                Projectile.velocity *= 0.95f;
                return;
            }

            // RECALL + FAST FOLLOW (2026-11-15 fix): the boss must keep up
            // with the player. Teleport back if too far; fly fast toward
            // the player if lagging behind (20 px/tick, faster than any
            // boss attack movement, so the minion catches up). The trail
            // cache is reset on teleport so the afterimages don't streak
            // across the screen from the old position.
            float distToOwner = Vector2.Distance(Projectile.Center, owner.Center);
            if (distToOwner > 800f)
            {
                Projectile.Center = owner.Center;
                Projectile.velocity = Vector2.Zero;
                Projectile.netUpdate = true;
                for (int i = 0; i < Projectile.oldPos.Length; i++)
                    Projectile.oldPos[i] = Projectile.position;
                for (int i = 0; i < 10; i++)
                    Dust.NewDust(Projectile.position, Projectile.width, Projectile.height, DustID.GoldFlame);
            }
            else if (distToOwner > 400f)
            {
                Vector2 toOwner = (owner.Center - Projectile.Center).SafeNormalize(Vector2.Zero);
                Projectile.velocity = Vector2.Lerp(Projectile.velocity, toOwner * 20f, 0.2f);
            }

            // AMBIENT DUST (vanilla, top of aiStyle 31 — same as the Eye of
            // Cthulhu): 1-in-5 per tick, type 5, from the eye's middle
            // band, drifting with the eye's horizontal velocity.
            if (Main.rand.Next(5) == 0)
            {
                Vector2 dustArea = new Vector2(Projectile.position.X, Projectile.position.Y + Projectile.height * 0.25f);
                int d = Dust.NewDust(dustArea, Projectile.width, (int)(Projectile.height * 0.5f), 5, Projectile.velocity.X, 2f, 0, default);
                Main.dust[d].velocity.X *= 0.5f;
                Main.dust[d].velocity.Y *= 0.1f;
            }

            // Animation frames (vanilla FindFrame, NPC 126 — see boss_frames.md;
            // the Twins share the Eye of Cthulhu's FindFrame block verbatim).
            // Runs every tick, even with no target. The frame cycle is
            // 0→1→2 at 7 ticks/frame; after the shell sheds (phase >= 2,
            // the spin-down stage) it shifts +3 to frames 3→4→5.
            UpdateFrame();

            // Re-scan for a target every 15 ticks (or if the current one died).
            retargetTimer++;
            if (retargetTimer >= 15 || target == null || !target.active || !target.CanBeChasedBy())
            {
                retargetTimer = 0;
                target = FindTarget(TargetRange);
            }

            if (target == null)
            {
                // No enemy: drift to the hover point near the player.
                SteerToward(owner.Center + PlayerHoverOffset, 8f, 0.15f);
                EaseRotationToward(owner.Center, RotationTurnRate);
                return;
            }

            bool expert = Main.expertMode || Main.masterMode;
            int phase = (int)Projectile.ai[0];

            switch (phase)
            {
                case 0: // Phase 1
                    phaseTimer++;
                    if (phaseTimer >= PhaseDuration)
                    {
                        Projectile.ai[0] = 1f; // transformation stage 1
                        Projectile.ai[1] = 0f;
                        phaseTimer = 0;
                        stateTimer = 0;
                        spinTimer = 0;
                        spinRate = 0f;
                        Projectile.netUpdate = true;
                        break;
                    }
                    Phase1AI(expert);
                    break;

                case 1: // Transformation, spin up
                    TransformAI(spinningUp: true);
                    break;

                case 2: // Transformation, spin down
                    TransformAI(spinningUp: false);
                    break;

                case 3: // Phase 2
                    phaseTimer++;
                    if (phaseTimer >= PhaseDuration)
                    {
                        // Back to phase 1 (minion loop; the real boss never returns).
                        Projectile.ai[0] = 0f;
                        Projectile.ai[1] = 0f;
                        phaseTimer = 0;
                        stateTimer = 0;
                        fireTimer = 0;
                        flameSoundTimer = 0;
                        dashCounter = 0;
                        Projectile.netUpdate = true;
                        break;
                    }
                    Phase2AI(expert);
                    break;

                default:
                    Projectile.ai[0] = 0f;
                    break;
            }
        }

        private void Phase1AI(bool expert)
        {
            int subState = (int)Projectile.ai[1];
            stateTimer++;

            switch (subState)
            {
                case 0: // Hover beside the target, shoot Cursed Flames
                    {
                        // Hover point: beside the target at the same height.
                        float side = Projectile.Center.X >= target.Center.X ? 1f : -1f;
                        Vector2 hoverPoint = target.Center + new Vector2(side * P1HoverSideOffset, 0f);
                        SteerToward(hoverPoint, P1HoverSpeed, P1HoverAccel);
                        EaseRotationToward(target.Center, RotationTurnRate);

                        // Cursed Flame: no positional gate (unlike Retinazer's laser).
                        fireTimer++;
                        if (fireTimer >= P1FlameCooldown)
                        {
                            fireTimer = 0;
                            FireCursedFlame(expert);
                        }

                        if (stateTimer >= P1HoverDuration)
                        {
                            Projectile.ai[1] = 1f; // dash
                            stateTimer = 0;
                            dashCounter = 0;
                            fireTimer = 0; // vanilla zeroes ai[3] at the transition
                            Projectile.netUpdate = true;
                        }
                        break;
                    }

                case 1: // Dash launch (1 tick): velocity set directly at the target.
                    {
                        float dashSpeed = expert ? P1DashSpeedExpert : P1DashSpeed;
                        Vector2 dir = (target.Center - Projectile.Center).SafeNormalize(Vector2.UnitX);
                        Projectile.velocity = dir * dashSpeed;
                        // Vanilla snaps rotation to face the target on the
                        // launch tick (rotation = num545); the recovery
                        // branch overwrites it mouth-first on the next tick.
                        Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.PiOver2;
                        Projectile.ai[1] = 2f;
                        stateTimer = 0;
                        Projectile.netUpdate = true;
                        break;
                    }

                case 2: // Dash recovery (phase-1 dashes are silent)
                    {
                        // Vanilla: ai[2] >= 8 decays (x0.9, near-zero
                        // components zeroed) and the top-of-AI easing
                        // (0.15) applies; before that, rotation locks to
                        // the velocity MOUTH-FIRST (atan2 - 1.57) every tick.
                        if (stateTimer >= P1DashDecayAfter)
                        {
                            Projectile.velocity *= P1DashDecay;
                            ZeroSmallVelocity();
                            EaseRotationToward(target.Center, RotationTurnRate);
                        }
                        else
                        {
                            Projectile.rotation = Projectile.velocity.ToRotation() - MathHelper.PiOver2;
                        }

                        if (stateTimer >= P1DashCycle)
                        {
                            // Vanilla snaps the rotation to face the target
                            // at dash end (rotation = num545).
                            Projectile.rotation = (target.Center - Projectile.Center).ToRotation() + MathHelper.PiOver2;
                            dashCounter++;
                            stateTimer = 0;
                            if (dashCounter >= P1DashCount)
                            {
                                Projectile.ai[1] = 0f; // back to hover
                                dashCounter = 0;
                            }
                            else
                            {
                                Projectile.ai[1] = 1f; // dash again
                            }
                            Projectile.netUpdate = true;
                        }
                        break;
                    }
            }
        }

        private void Phase2AI(bool expert)
        {
            int subState = (int)Projectile.ai[1];
            stateTimer++;

            switch (subState)
            {
                case 0: // Hover close beside the target, breathe fire
                    {
                        float side = Projectile.Center.X >= target.Center.X ? 1f : -1f;
                        Vector2 hoverPoint = target.Center + new Vector2(side * P2HoverSideOffset, 0f);
                        float speed = P2HoverSpeed;
                        if (expert)
                        {
                            // Expert: speed grows with distance to the HOVER
                            // POINT (vanilla num578), closing the gap faster.
                            float dist = Vector2.Distance(Projectile.Center, hoverPoint);
                            if (dist > 300f) speed += 0.5f;
                            if (dist > 400f) speed += 0.5f;
                            if (dist > 500f) speed += 0.55f;
                            if (dist > 600f) speed += 0.55f;
                            if (dist > 700f) speed += 0.6f;
                            if (dist > 800f) speed += 0.6f;
                        }
                        SteerToward(hoverPoint, speed, P2HoverAccel);
                        // Spazmatism's P2 has NO rotation override in
                        // vanilla (unlike Retinazer) — the top-of-AI easing
                        // toward +Pi/2 at 0.15 is correct here.
                        EaseRotationToward(target.Center, RotationTurnRate);

                        // Flamethrower: only while line of sight holds.
                        if (HasLineOfSight(target))
                        {
                            fireTimer++;
                            if (fireTimer > P2FlameThreshold)
                            {
                                fireTimer = 0;
                                FireEyeFire();
                            }

                            // Flamethrower loop sound (Item34), ~every 23 ticks.
                            flameSoundTimer++;
                            if (flameSoundTimer > P2FlameSoundInterval)
                            {
                                flameSoundTimer = 0;
                                SoundEngine.PlaySound(SoundID.Item34, Projectile.Center);
                            }
                        }

                        if (stateTimer >= P2HoverDuration)
                        {
                            Projectile.ai[1] = 1f; // dash
                            stateTimer = 0;
                            dashCounter = 0;
                            Projectile.netUpdate = true;
                        }
                        break;
                    }

                case 1: // Dash launch (1 tick) + roar
                    {
                        float dashSpeed = expert ? P2DashSpeedExpert : P2DashSpeed;
                        Vector2 dir = (target.Center - Projectile.Center).SafeNormalize(Vector2.UnitX);
                        Projectile.velocity = dir * dashSpeed;
                        // Vanilla snaps rotation to face the target on the
                        // launch tick (rotation = num545); the recovery
                        // branch overwrites it mouth-first on the next tick.
                        Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.PiOver2;
                        // Roar at each phase-2 dash start (vanilla).
                        SoundEngine.PlaySound(RoarSound, Projectile.Center);
                        Projectile.ai[1] = 2f;
                        stateTimer = 0;
                        p2DashTimer = 0f;
                        Projectile.netUpdate = true;
                        break;
                    }

                case 2: // Dash recovery
                    {
                        // Vanilla: the P2 dash timer runs 1.5x in Expert
                        // (ai[2] += 0.5 extra per tick). Decay (x0.93,
                        // near-zero components zeroed) starts at timer 50
                        // with the top-of-AI easing (0.15); before that,
                        // rotation locks to the velocity MOUTH-FIRST
                        // (atan2 - 1.57) every tick.
                        p2DashTimer += expert ? 1.5f : 1f;
                        if (p2DashTimer >= P2DashDecayAfter)
                        {
                            Projectile.velocity *= P2DashDecay;
                            ZeroSmallVelocity();
                            EaseRotationToward(target.Center, RotationTurnRate);
                        }
                        else
                        {
                            Projectile.rotation = Projectile.velocity.ToRotation() - MathHelper.PiOver2;
                        }

                        if (p2DashTimer >= P2DashCycle)
                        {
                            // Vanilla snaps the rotation to face the target
                            // at dash end (rotation = num545).
                            Projectile.rotation = (target.Center - Projectile.Center).ToRotation() + MathHelper.PiOver2;
                            dashCounter++;
                            p2DashTimer = 0f;
                            stateTimer = 0;
                            if (dashCounter >= P2DashCount)
                            {
                                Projectile.ai[1] = 0f; // back to flamethrower hover
                                dashCounter = 0;
                            }
                            else
                            {
                                Projectile.ai[1] = 1f; // dash again (roars again)
                            }
                            Projectile.netUpdate = true;
                        }
                        break;
                    }
            }
        }

        private void TransformAI(bool spinningUp)
        {
            spinTimer++;
            // Spin rate ramps 0 -> 0.5 at 0.005/tick (stage 1), then 0.5 -> 0 (stage 2).
            if (spinningUp)
                spinRate = Math.Min(0.5f, spinRate + 0.005f);
            else
                spinRate = Math.Max(0f, spinRate - 0.005f);
            Projectile.rotation += spinRate;
            Projectile.velocity *= 0.98f;
            ZeroSmallVelocity();

            // Vanilla spawns 1 dust (type 5) every tick during the transform.
            Dust.NewDust(Projectile.position, Projectile.width, Projectile.height, 5,
                Main.rand.NextFloat(-3f, 3f), Main.rand.NextFloat(-3f, 3f), 0, default);

            if (spinTimer >= TransformStageDuration)
            {
                spinTimer = 0;
                if (spinningUp)
                {
                    // Midpoint: gore burst + crunch + roar (vanilla).
                    // Gore types are 144, 7, 6 (x2 each) — note 144, not
                    // Retinazer's 143. Spawned from the hitbox top-left
                    // with +/-6 velocity (halved here for the 0.5-scale
                    // minion).
                    if (Projectile.owner == Main.myPlayer)
                    {
                        foreach (int goreType in new int[] { 144, 7, 6 })
                        {
                            for (int i = 0; i < 2; i++)
                            {
                                Gore.NewGore(Projectile.GetSource_FromThis(), Projectile.position,
                                    new Vector2(Main.rand.NextFloat(-3f, 3f), Main.rand.NextFloat(-3f, 3f)),
                                    goreType, 0.5f);
                            }
                        }
                    }
                    SoundEngine.PlaySound(SoundID.NPCHit1, Projectile.Center);
                    SoundEngine.PlaySound(RoarSound, Projectile.Center);
                    for (int i = 0; i < 20; i++)
                        Dust.NewDust(Projectile.position, Projectile.width, Projectile.height, 5, Main.rand.NextFloat(-3f, 3f), Main.rand.NextFloat(-3f, 3f), 0, default, 1.2f);
                    Projectile.ai[0] = 2f;
                }
                else
                {
                    Projectile.ai[0] = 3f; // phase 2
                    Projectile.ai[1] = 0f;
                    phaseTimer = 0;
                    stateTimer = 0;
                    fireTimer = 0;
                    flameSoundTimer = 0;
                }
                Projectile.netUpdate = true;
            }
        }

        private void FireCursedFlame(bool expert)
        {
            Vector2 dir = (target.Center - Projectile.Center).SafeNormalize(Vector2.UnitX);
            float speed = expert ? P1FlameSpeedExpert : P1FlameSpeed;
            Vector2 velocity = dir * speed;
            velocity.X += Main.rand.NextFloat(-P1FlameJitter, P1FlameJitter);
            velocity.Y += Main.rand.NextFloat(-P1FlameJitter, P1FlameJitter);
            // Vanilla spawns the flame at center + VELOCITY x 4 (past the
            // muzzle), using the final jittered velocity.
            Vector2 spawnPos = Projectile.Center + velocity * P1FlameSpawnAhead;
            FireFriendly(spawnPos, velocity, ProjectileID.CursedFlameHostile);
            // The flame's sound (SoundID.Item20) plays from the projectile
            // itself on its first AI tick — do not add it here.
        }

        private void FireEyeFire()
        {
            Vector2 dir = (target.Center - Projectile.Center).SafeNormalize(Vector2.UnitX);
            Vector2 velocity = dir * P2FlameSpeed;
            velocity.X += Main.rand.NextFloat(-P2FlameJitter, P2FlameJitter);
            velocity.Y += Main.rand.NextFloat(-P2FlameJitter, P2FlameJitter);
            // The stream inherits 50% of the eye's velocity — this is what
            // makes it sweep like a flamethrower instead of a laser.
            velocity += Projectile.velocity * 0.5f;
            // Vanilla spawns the stream at center - VELOCITY x 1 (behind
            // the eye, using the final velocity).
            Vector2 spawnPos = Projectile.Center - velocity;
            FireFriendly(spawnPos, velocity, ProjectileID.EyeFire);
            // EyeFire has no projectile-side sound; the Item34 loop above
            // is its audio.
        }

        // Spawns a FRIENDLY version of a vanilla (hostile-by-default) boss
        // projectile. Vanilla boss projectiles set hostile = true in
        // SetDefaults, so the flags must be flipped after spawn or the shot
        // would hurt the player instead of the enemy.
        private void FireFriendly(Vector2 position, Vector2 velocity, int type)
        {
            if (Projectile.owner != Main.myPlayer)
                return;
            int p = Projectile.NewProjectile(Projectile.GetSource_FromThis(), position, velocity, type, MinionDamage, 0f, Projectile.owner);
            if (p >= 0 && p < Main.maxProjectiles)
            {
                Main.projectile[p].friendly = true;
                Main.projectile[p].hostile = false;
                Main.projectile[p].usesLocalNPCImmunity = true;
                Main.projectile[p].localNPCHitCooldown = 20;
            }
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

        private bool HasLineOfSight(NPC npc)
        {
            return Collision.CanHit(Projectile.Center, 1, 1, npc.Center, 1, 1);
        }

        // Vanilla-style hover steering: accelerate toward the point, cap speed.
        private void SteerToward(Vector2 targetPoint, float maxSpeed, float accel)
        {
            Vector2 dir = targetPoint - Projectile.Center;
            if (dir.LengthSquared() < 4f)
            {
                Projectile.velocity *= 0.95f;
                return;
            }
            dir.Normalize();
            Projectile.velocity += dir * accel;
            if (Projectile.velocity.Length() > maxSpeed)
                Projectile.velocity = Vector2.Normalize(Projectile.velocity) * maxSpeed;
        }

        private void EaseRotationToward(Vector2 point, float maxTurn)
        {
            float targetRotation = (point - Projectile.Center).ToRotation() + MathHelper.PiOver2;
            float diff = MathHelper.WrapAngle(targetRotation - Projectile.rotation);
            Projectile.rotation += MathHelper.Clamp(diff, -maxTurn, maxTurn);
        }

        // Vanilla zeroes velocity components smaller than 0.1 during dash
        // decay and the transform so the eye comes to a clean stop.
        private void ZeroSmallVelocity()
        {
            if (Projectile.velocity.X > -0.1f && Projectile.velocity.X < 0.1f)
                Projectile.velocity.X = 0f;
            if (Projectile.velocity.Y > -0.1f && Projectile.velocity.Y < 0.1f)
                Projectile.velocity.Y = 0f;
        }

        // Vanilla FindFrame animation (NPC 126, 6 frames — see boss_frames.md;
        // byte-for-byte identical to the Eye of Cthulhu's shared block):
        // frames cycle 0→1→2 at 7 ticks/frame (21-tick loop). Once the shell
        // has shed (phase >= 2 — the spin-down stage), the cycle shifts +3
        // to frames 3→4→5 (the mechanical second-form eye, mouth/jaw form).
        // Spinning is rotation only. (The minion loops back to phase 1 after
        // phase 2; the frame shift follows the current phase, so it returns
        // to 0–2.)
        private void UpdateFrame()
        {
            Projectile.frameCounter++;
            int f;
            if (Projectile.frameCounter < 7) f = 0;
            else if (Projectile.frameCounter < 14) f = 1;
            else if (Projectile.frameCounter < 21) f = 2;
            else { Projectile.frameCounter = 0; f = 0; }
            if ((int)Projectile.ai[0] >= 2) f += 3;
            Projectile.frame = f;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D texture = TextureAssets.Projectile[Projectile.type].Value;
            // Frame count is HARDCODED (6 — Spazmatism's texture has 6 frames;
            // see boss_frames.md). Main.npcFrameCount returns 0/1 for NPCs
            // whose textures haven't loaded yet, which drew the whole sprite
            // sheet. Projectile.frame is set every tick by UpdateFrame().
            const int frameCount = 6;
            int frameHeight = texture.Height / frameCount;
            Rectangle frame = new Rectangle(0, Projectile.frame * frameHeight, texture.Width, frameHeight);
            Vector2 origin = frame.Size() / 2f;
            // Vanilla draws the sprite center 4 px below the NPC center
            // (Main.DrawNPC literal +4); scaled by the minion's 0.5.
            Vector2 drawPos = Projectile.Center + new Vector2(0f, 4f * Projectile.scale) - Main.screenPosition;

            // AFTERIMAGES (vanilla Main.DrawNPC, type 126): 5 ghosts at
            // oldPos[9,7,5,3,1] with alpha (10-n)/20, the current frame
            // and the current rotation. Zero entries are unset (spawn) and
            // are skipped so no ghost draws at the world origin.
            // (Spazmatism has no glow overlay in vanilla — only Retinazer
            // gets the EyeLaser glow — so none is drawn here.)
            for (int n = 9; n >= 1; n -= 2)
            {
                Vector2 oldPos = Projectile.oldPos[n];
                if (oldPos == Vector2.Zero)
                    continue;
                Vector2 ghostPos = oldPos + new Vector2(Projectile.width / 2f, Projectile.height / 2f + 4f * Projectile.scale) - Main.screenPosition;
                Color ghostColor = lightColor * ((10 - n) / 20f);
                Main.EntitySpriteDraw(texture, ghostPos, frame, ghostColor, Projectile.rotation, origin, Projectile.scale, SpriteEffects.None, 0);
            }

            Main.EntitySpriteDraw(texture, drawPos, frame, lightColor, Projectile.rotation, origin, Projectile.scale, SpriteEffects.None, 0);
            return false;
        }
    }
}
