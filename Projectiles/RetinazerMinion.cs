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
    /// Retinazer boss minion (Brigitte's Charm, 2026-11-15): the laser eye
    /// of the Twins, rewritten as a friendly minion. It fires friendly
    /// versions of its real attacks (EyeLaser in phase 1, DeathLaser in
    /// phase 2) at the owner's enemies, dashes like the real boss, and
    /// plays the boss's sounds at the same moments.
    /// Attack data from the decompiled Terraria 1.4.4.9 AI_030
    /// (see ~/workspace/tmp/boss_attacks_batch1.md, Part 2 section 1).
    ///
    /// FIXED 2026-11-18 (vs decompiled vanilla Terraria 1.4.5 NPC.cs
    /// aiStyle 30, lines 31706-32342, and Main.DrawNPC):
    /// - LASER SPAWN-AHEAD BUG: vanilla spawns both lasers at center +
    ///   VELOCITY x 15 (135-157 px ahead of the eye, past the muzzle).
    ///   The backup spawned at center + direction x 15 — 15 px, INSIDE
    ///   the eye, so the beam visually emerged from the eye's middle.
    ///   Fixed (both EyeLaser and DeathLaser), using the final jittered
    ///   velocity like vanilla.
    /// - P2 ROTATION: vanilla sets rotation = atan2(aim) - 1.57 DIRECTLY
    ///   every tick in BOTH P2 hover and P2 strafe (the laser cannon
    ///   aims at the target — the -1.57 points the emitter at the
    ///   target). The backup eased toward +Pi/2, so the eye faced
    ///   backwards while firing. Fixed in both P2 branches.
    /// - DASH ROTATION: vanilla sets rotation = atan2(velocity) - 1.57
    ///   during the first 24 ticks of the dash recovery (mouth-first),
    ///   decays x0.96 from tick 25 with near-zero components zeroed,
    ///   and snaps to face the target at dash end. The backup set +Pi/2
    ///   (180 degrees wrong) for 25 ticks, never zeroed, never snapped,
    ///   and never eased during the decay (vanilla's top-of-AI easing
    ///   at 0.1 applies during the decay). Fixed.
    /// - TRANSFORM GORE MISSING: vanilla spawns gore 143, 7, 6 (x2 each)
    ///   at the transform midpoint — the backup's comment said "gore
    ///   burst" but the code only did dust. Added (at the hitbox
    ///   top-left, velocity +/-3 halved, scale 0.5). Also added the
    ///   1 dust/tick type-5 mote vanilla spawns during the whole
    ///   transform, and the velocity zeroing.
    /// - EYELASER GLOW OVERLAY MISSING: vanilla draws the EyeLaser
    ///   texture over Retinazer (same frame/rotation, additive color
    ///   (255,255,255,0)) — the glowing red eye. The backup drew only
    ///   the base sprite. Added.
    /// - AFTERIMAGES MISSING: vanilla draws 5 ghosts for type 125
    ///   (oldPos[9,7,5,3,1], alpha (10-n)/20, current frame/rotation).
    ///   Added (TrailCacheLength = 10; oldPos cleared on recall
    ///   teleport so the ghosts don't streak across the screen).
    /// - AMBIENT DUST: vanilla's 1-in-5 mote comes from the eye's middle
    ///   band (position.X, position.Y + height*0.25, width x height*0.5)
    ///   with velocity (vel.X, 2), dust velocity scaled x0.5 / x0.1 —
    ///   the same as the Eye of Cthulhu. The backup used the full body,
    ///   zero velocity, white color. Fixed.
    /// - P1 LASER CONDITIONS: vanilla measures the 400 px range to the
    ///   HOVER POINT (num508), not to the target, and the fire counter
    ///   only advances while the conditions hold (it pauses out of
    ///   position). The backup measured to the target's center and let
    ///   the counter run always. Fixed. The counter also resets at the
    ///   hover-&gt;dash transition (vanilla zeroes ai[3] there).
    /// - P2 STRAFE EXPERT FIRE RATE: vanilla advances the strafe fire
    ///   timer by 1.5 extra per tick in Expert (mode-based, not
    ///   life-based — the life-tier accelerations stay dropped per the
    ///   documented adaptation). The backup used the base rate in all
    ///   modes. Fixed (fireTimer is now a float).
    /// - DRAW OFFSET: vanilla draws the sprite center 4 px below the
    ///   NPC center (Main.DrawNPC literal +4). Applied as +4 * scale.
    /// - Added Projectile.netImportant = true (weapon-minion pattern).
    ///
    /// VERIFIED CORRECT (no change): frame cycle 0-1-2 at 7 t/f shifting
    /// +3 in phase 2 (FindFrame 90898, shared with the Eye), 6 frames,
    /// 50x55 halved hitbox, P1 hover 7/0.1 (8.25/0.115 Expert) at
    /// +/-300 X / -300 Y, P1 laser speed 9/10.5 with +/-3.2 jitter and
    /// 60-tick cooldown, P1 dash 12/15 speed, 4 dashes, 70-tick cycle,
    /// 25-tick window, x0.96 decay, P2 hover 8/0.15 (9.5/0.175 Expert)
    /// 300 ticks with 8.5/10 Death Laser at threshold 180, P2 strafe
    /// 8/0.2 (9.5/0.25 Expert) 180 ticks at +/-340 X with speed-9 Death
    /// Laser at threshold 60, transform 2x100 ticks with spin ramp
    /// 0.005, NPCHit1 + Roar at the midpoint, P1 hover rotation easing
    /// 0.1 (vanilla num496, fixed — not difficulty-scaled).
    ///
    /// Minion adaptations (the boss has no minion equivalent for these):
    /// - Phase transitions are on a TIMER (20 s per phase), not life
    ///   fraction — minions have no meaningful life total. The 200-tick
    ///   spin transformation (with the crunch + roar at the midpoint) is
    ///   kept when entering phase 2. Returning to phase 1 is a direct
    ///   switch (the real boss never goes back).
    /// - The Expert fire-rate/life-threshold accelerations are dropped
    ///   (no life fraction); base rates are used — EXCEPT the P2 strafe
    ///   Expert +1.5/tick, which is mode-based and is now ported (see
    ///   FIXED above). Expert speeds are used in Expert/Master worlds,
    ///   Classic speeds otherwise.
    /// - Despawn-at-daylight and twin-coordination code are dropped.
    /// - All damage is flat 6000, typeless (DamageClass.Generic), per the
    ///   charm's design — the vanilla damage values in the spec are
    ///   informational only.
    /// - Sound correction: the spec's Part 2 resolved the transformation
    ///   crunch (legacy PlaySound(3, ...)) as "NPCKilled", but the
    ///   decompiled SoundID shows type 3 is the NPCHit bank (NPCHit1 =
    ///   LegacySoundStyle(3, 1)) and NPCKilled is the int const 4 (the
    ///   NPCDeath bank). Part 1 (Eye of Cthulhu, same call) resolved it
    ///   to NPCHit1 — that is what plays here.
    /// </summary>
    public class RetinazerMinion : ModProjectile
    {
        // --- Spec constants (vanilla AI_030, Retinazer) ---
        private const float TargetRange = 1000f;
        private const int PhaseDuration = 1200;    // 20 s per phase (minion adaptation)
        private const int TransformStageDuration = 100; // 100 ticks spin-up + 100 spin-down

        // Phase 1 hover
        private const float P1HoverSpeed = 7f;
        private const float P1HoverSpeedExpert = 8.25f;
        private const float P1HoverAccel = 0.1f;
        private const float P1HoverAccelExpert = 0.115f;
        private const int P1HoverDuration = 600;   // 10 s, then dash
        private const float P1HoverSideOffset = 300f;
        private const float P1HoverHeight = 300f;

        // Phase 1 Eye Laser
        private const float P1LaserSpeed = 9f;
        private const float P1LaserSpeedExpert = 10.5f;
        private const int P1LaserCooldown = 60;    // 1 s
        private const float P1LaserRange = 400f;   // must be within 400 px of the HOVER POINT + above target
        private const float P1LaserJitter = 3.2f;  // +/- per axis

        // Phase 1 dash
        private const float P1DashSpeed = 12f;
        private const float P1DashSpeedExpert = 15f;
        private const int P1DashCount = 4;
        private const int P1DashCycle = 70;        // ticks per dash cycle
        private const int P1DashDecayAfter = 25;   // velocity decays from tick 25
        private const float P1DashDecay = 0.96f;

        // Phase 2 hover (Death Laser, slow/heavy)
        private const float P2HoverSpeed = 8f;
        private const float P2HoverSpeedExpert = 9.5f;
        private const float P2HoverAccel = 0.15f;
        private const float P2HoverAccelExpert = 0.175f;
        private const int P2HoverDuration = 300;   // 5 s, then strafe
        private const float P2HoverHeight = 300f;
        private const float P2LaserSpeedHover = 8.5f;
        private const float P2LaserSpeedHoverExpert = 10f;
        private const int P2LaserThresholdHover = 180;

        // Phase 2 strafe (Death Laser, rapid/light)
        private const float P2StrafeSpeed = 8f;
        private const float P2StrafeSpeedExpert = 9.5f;
        private const float P2StrafeAccel = 0.2f;
        private const float P2StrafeAccelExpert = 0.25f;
        private const int P2StrafeDuration = 180;  // 3 s, then hover
        private const float P2StrafeSideOffset = 340f;
        private const float P2LaserSpeedStrafe = 9f;
        private const int P2LaserThresholdStrafe = 60;
        // In Expert the strafe fire timer advances 1 + 1.5 per tick
        // (vanilla localAI[1] += 1.5 in Expert — mode-based, ported).
        private const float P2StrafeFireRateExpert = 2.5f;

        private const float LaserSpawnAhead = 15f; // spawn = center + VELOCITY x 15
        private const float RotationTurnRate = 0.1f; // rad/tick (vanilla num496)

        private const int MinionDamage = 6000;     // flat, typeless (charm design)

        // Roar (legacy sound type 15, style 0). SoundID.Roar is a const int,
        // not a SoundStyle — use the path-based SoundStyle instead.
        private static readonly SoundStyle RoarSound = new SoundStyle("Terraria/Sounds/Roar_0");

        // Hover offset near the player (no target): each boss minion gets a
        // distinct offset so the entourage doesn't stack on one point.
        private static readonly Vector2 PlayerHoverOffset = new Vector2(220f, -260f);

        // --- State ---
        // Projectile.ai[0] = phase: 0 = phase 1, 1 = transform stage 1,
        // 2 = transform stage 2, 3 = phase 2.
        // Projectile.ai[1] = sub-state within the phase.
        private int phaseTimer = 0;
        private int stateTimer = 0;
        private float fireTimer = 0f;
        private int dashCounter = 0;
        private int spinTimer = 0;
        private float spinRate = 0f;
        private int retargetTimer = 0;
        private NPC target = null;

        public override string Texture => "Terraria/Images/NPC_125";

        public override void SetStaticDefaults()
        {
            // Vanilla draws 5 afterimages for Retinazer (Main.DrawNPC,
            // type 125) from oldPos[9,7,5,3,1] — the trail cache must be
            // long enough to hold them.
            ProjectileID.Sets.TrailCacheLength[Type] = 10;
        }

        public override void SetDefaults()
        {
            // Vanilla Retinazer hitbox 100x110, halved for the minion (0.5 scale).
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

            // AMBIENT DUST (vanilla, top of aiStyle 30 — same as the Eye of
            // Cthulhu): 1-in-5 per tick, type 5, from the eye's middle
            // band, drifting with the eye's horizontal velocity.
            if (Main.rand.Next(5) == 0)
            {
                Vector2 dustArea = new Vector2(Projectile.position.X, Projectile.position.Y + Projectile.height * 0.25f);
                int d = Dust.NewDust(dustArea, Projectile.width, (int)(Projectile.height * 0.5f), 5, Projectile.velocity.X, 2f, 0, default);
                Main.dust[d].velocity.X *= 0.5f;
                Main.dust[d].velocity.Y *= 0.1f;
            }

            // Animation frames (vanilla FindFrame, NPC 125 — see boss_frames.md;
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
                        fireTimer = 0f;
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
                case 0: // Hover, shoot Eye Lasers
                    {
                        // Hover point: 300 px above and 300 px to the side of
                        // the target (stay on the side we're already on).
                        float side = Projectile.Center.X >= target.Center.X ? 1f : -1f;
                        Vector2 hoverPoint = target.Center + new Vector2(side * P1HoverSideOffset, -P1HoverHeight);
                        float speed = expert ? P1HoverSpeedExpert : P1HoverSpeed;
                        float accel = expert ? P1HoverAccelExpert : P1HoverAccel;
                        SteerToward(hoverPoint, speed, accel);
                        EaseRotationToward(target.Center, RotationTurnRate);

                        // Eye Laser: only while above the target and within
                        // 400 px OF THE HOVER POINT (vanilla num508). The
                        // fire counter only advances while the conditions
                        // hold (vanilla ai[3] pauses out of position).
                        bool above = Projectile.position.Y + Projectile.height < target.position.Y;
                        float dist = Vector2.Distance(Projectile.Center, hoverPoint);
                        if (above && dist < P1LaserRange)
                        {
                            fireTimer++;
                            if (fireTimer >= P1LaserCooldown)
                            {
                                fireTimer = 0f;
                                FireEyeLaser(expert);
                            }
                        }

                        if (stateTimer >= P1HoverDuration)
                        {
                            Projectile.ai[1] = 1f; // dash
                            stateTimer = 0;
                            dashCounter = 0;
                            fireTimer = 0f; // vanilla zeroes ai[3] at the transition
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
                        // launch tick (rotation = num495); the recovery
                        // branch overwrites it mouth-first on the next tick.
                        Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.PiOver2;
                        Projectile.ai[1] = 2f;
                        stateTimer = 0;
                        Projectile.netUpdate = true;
                        break;
                    }

                case 2: // Dash recovery
                    {
                        // Vanilla: ai[2] >= 25 decays (x0.96, near-zero
                        // components zeroed) and the top-of-AI easing
                        // (0.1) applies; before that, rotation locks to the
                        // velocity MOUTH-FIRST (atan2 - 1.57) every tick.
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
                            // at dash end (rotation = num495).
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
                case 0: // Hover above the target, slow heavy Death Lasers
                    {
                        Vector2 hoverPoint = target.Center + new Vector2(0f, -P2HoverHeight);
                        float speed = expert ? P2HoverSpeedExpert : P2HoverSpeed;
                        float accel = expert ? P2HoverAccelExpert : P2HoverAccel;
                        SteerToward(hoverPoint, speed, accel);
                        // Vanilla sets rotation = atan2(aim) - 1.57 DIRECTLY
                        // every tick in P2 (the laser cannon aims at the
                        // target) — no easing.
                        Projectile.rotation = (target.Center - Projectile.Center).ToRotation() - MathHelper.PiOver2;

                        fireTimer++;
                        if (fireTimer > P2LaserThresholdHover && HasLineOfSight(target))
                        {
                            fireTimer = 0f;
                            float laserSpeed = expert ? P2LaserSpeedHoverExpert : P2LaserSpeedHover;
                            FireDeathLaser(laserSpeed, 0f);
                        }

                        if (stateTimer >= P2HoverDuration)
                        {
                            Projectile.ai[1] = 1f; // strafe
                            stateTimer = 0;
                            Projectile.netUpdate = true;
                        }
                        break;
                    }

                case 1: // Strafe beside the target, rapid light Death Lasers
                    {
                        float side = Projectile.Center.X >= target.Center.X ? 1f : -1f;
                        Vector2 strafePoint = target.Center + new Vector2(side * P2StrafeSideOffset, 0f);
                        float speed = expert ? P2StrafeSpeedExpert : P2StrafeSpeed;
                        float accel = expert ? P2StrafeAccelExpert : P2StrafeAccel;
                        SteerToward(strafePoint, speed, accel);
                        // Vanilla sets rotation = atan2(aim) - 1.57 DIRECTLY
                        // every tick in P2 (the laser cannon aims at the
                        // target) — no easing.
                        Projectile.rotation = (target.Center - Projectile.Center).ToRotation() - MathHelper.PiOver2;

                        // In Expert the strafe fire timer advances 2.5/tick
                        // (vanilla localAI[1] += 1.5 in Expert).
                        fireTimer += expert ? P2StrafeFireRateExpert : 1f;
                        if (fireTimer > P2LaserThresholdStrafe && HasLineOfSight(target))
                        {
                            fireTimer = 0f;
                            FireDeathLaser(P2LaserSpeedStrafe, 0f);
                        }

                        if (stateTimer >= P2StrafeDuration)
                        {
                            Projectile.ai[1] = 0f; // back to hover
                            stateTimer = 0;
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
                    // Gore types are 143, 7, 6 (x2 each), spawned from the
                    // hitbox top-left with +/-6 velocity (halved here for
                    // the 0.5-scale minion).
                    if (Projectile.owner == Main.myPlayer)
                    {
                        foreach (int goreType in new int[] { 143, 7, 6 })
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
                    fireTimer = 0f;
                }
                Projectile.netUpdate = true;
            }
        }

        private void FireEyeLaser(bool expert)
        {
            Vector2 dir = (target.Center - Projectile.Center).SafeNormalize(Vector2.UnitX);
            float speed = expert ? P1LaserSpeedExpert : P1LaserSpeed;
            Vector2 velocity = dir * speed;
            velocity.X += Main.rand.NextFloat(-P1LaserJitter, P1LaserJitter);
            velocity.Y += Main.rand.NextFloat(-P1LaserJitter, P1LaserJitter);
            // Vanilla spawns the laser at center + VELOCITY x 15 (past the
            // muzzle), using the final jittered velocity.
            Vector2 spawnPos = Projectile.Center + velocity * LaserSpawnAhead;
            FireFriendly(spawnPos, velocity, ProjectileID.EyeLaser);
            // The laser's sound (SoundID.Item33) plays from the projectile
            // itself on its first AI tick — do not add it here.
        }

        private void FireDeathLaser(float speed, float jitter)
        {
            Vector2 dir = (target.Center - Projectile.Center).SafeNormalize(Vector2.UnitX);
            Vector2 velocity = dir * speed;
            if (jitter > 0f)
            {
                velocity.X += Main.rand.NextFloat(-jitter, jitter);
                velocity.Y += Main.rand.NextFloat(-jitter, jitter);
            }
            // Vanilla spawns the laser at center + VELOCITY x 15.
            Vector2 spawnPos = Projectile.Center + velocity * LaserSpawnAhead;
            FireFriendly(spawnPos, velocity, ProjectileID.DeathLaser);
            // SoundID.Item33 comes free from the projectile's own AI.
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

        // Vanilla FindFrame animation (NPC 125, 6 frames — see boss_frames.md;
        // byte-for-byte identical to the Eye of Cthulhu's shared block):
        // frames cycle 0→1→2 at 7 ticks/frame (21-tick loop). Once the shell
        // has shed (phase >= 2 — the spin-down stage), the cycle shifts +3
        // to frames 3→4→5 (the mechanical second-form eye). Spinning is
        // rotation only. (The minion loops back to phase 1 after phase 2;
        // the frame shift follows the current phase, so it returns to 0–2.)
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
            // Frame count is HARDCODED (6 — Retinazer's texture has 6 frames;
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

            // AFTERIMAGES (vanilla Main.DrawNPC, type 125): 5 ghosts at
            // oldPos[9,7,5,3,1] with alpha (10-n)/20, the current frame
            // and the current rotation. Zero entries are unset (spawn) and
            // are skipped so no ghost draws at the world origin.
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

            // EYELASER GLOW OVERLAY (vanilla Main.DrawNPC, type 125): the
            // EyeLaser texture over the base sprite — same frame, same
            // rotation, additive color (255,255,255,0). This is the eye's
            // glow; without it the minion looks flat next to the real boss.
            Texture2D glow = TextureAssets.EyeLaser.Value;
            Main.EntitySpriteDraw(glow, drawPos, frame, new Color(255, 255, 255, 0), Projectile.rotation, origin, Projectile.scale, SpriteEffects.None, 0);
            return false;
        }
    }
}
