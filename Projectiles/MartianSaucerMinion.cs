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
    /// Martian Saucer (NPC 392) as a Brigitte's Charm boss minion
    /// (2026-11-15). ONE minion (the saucer body) running the Core's
    /// phase-0 600-tick attack cycle, retargeted at enemies: the
    /// Deathray sweep, the Turret laser barrage, the Cannon homing
    /// missiles, and the Scrap trickle. Half scale, flat 6000 typeless
    /// damage, local immunity frames.
    ///
    /// Attack data from the decompiled Terraria NPC AI (aiStyle 75/76):
    /// see ~/workspace/tmp/boss_attacks_event.md, Part B, Martian Saucer
    /// section.
    ///
    /// ARCHITECTURE: vanilla is 5 NPCs (chassis 392 + 2 Turrets 393 +
    /// 2 Cannons 394 + Core 395), with the Core owning the only real AI
    /// and a 600-tick master clock (ai[3]). The minion is a single
    /// entity running that same clock; the Turret/Cannon/Scrap attacks
    /// fire from the minion's own AI at the vanilla mount offsets
    /// (halved for 0.5 scale). No separate turret/cannon/core minions.
    ///
    /// PHASES: phase 0 only (Classic has no phase 2 — when all parts
    /// die the Core dies instantly, and a minion has no parts to
    /// destroy). No part destruction, no transition, no despawn states.
    ///
    /// SUBSTITUTIONS (vanilla content that cannot be reused):
    /// - SaucerDeathray (447): aiStyle 79 kills itself unless ai[0]
    ///   points at an active NPC 392/395, and aims at a player target.
    ///   Replaced by SaucerDeathrayFriendly (below): a vertical beam
    ///   anchored to the minion, extending from minion-center +23 px
    ///   down to the first solid tile at or below the target's feet
    ///   row (vanilla aiStyle 79: the beam passes a flying target down
    ///   to the ground), sweeping with the minion, 240 ticks, vanilla
    ///   447 texture + vanilla dust.
    /// - SaucerMissile (448): aiStyle 80 homes via Player.FindClosest
    ///   and proximity-detonates only vs players. Replaced by
    ///   SaucerMissileFriendly (below): 20-tick straight flight at
    ///   speed 8, then +4 speed and 30 ticks of 20%-per-tick steering
    ///   toward the nearest enemy, 42-px proximity detonation vs
    ///   enemies, 112x112 explosion on Kill with SoundID.Item14,
    ///   vanilla trail/ring dust and the 3-frame cycle.
    /// - SaucerScrap (450): vanilla fires only from DESTROYED part
    ///   mounts. A minion has no parts, so all four mounts fire on the
    ///   vanilla windows/chances as a baseline trickle (the spec's
    ///   recommended adaptation).
    ///
    /// FIXES (2026-11-18, vs decompiled aiStyle 75/76/79/80 + FindFrame
    /// cases 392-395 + DrawNPC): (1) the null-owner branch dereferenced
    /// player.Center when player could be null (NullReferenceException)
    /// — it now just damps velocity; (2) the reposition sides were
    /// recomputed EVERY tick (the destination could flip mid-flight
    /// and ping-pong) — vanilla LOCKS the side per window in ai[2]
    /// (reset at the transitions into states 0 and 2, NOT at the
    /// 599-&gt;0 wrap); the state-0 arrival jump is to 19 in BOTH windows
    /// (the backup jumped to 599 in the second window); and the state-2
    /// destination was on the WRONG side (vanilla stays on the NEAR
    /// side, +300 x Sign(core-target); the backup mirrored state 0's
    /// far-side logic); (3) state 2 adjusted the VELOCITY for ground
    /// clearance — vanilla adjusts the DESTINATION upward and checks
    /// arrival against the ADJUSTED vector; (4) the turret hover damped
    /// Y x0.85 AND then lerped it (double-damping the rise) — vanilla
    /// damps X always and lerps Y toward -4 (or -groundDist if closer)
    /// when the ground is within 170 px, else damps Y; the same -dist
    /// clamp was missing from the deathray sweep; (5) the cannon muzzle
    /// ignored the mount offset (the backup's own comment admitted it)
    /// — vanilla is cannon center + (outward x 36, +8), halved; (6) the
    /// scrap denominators were SWAPPED (turret window: turret 60 /
    /// cannon 90; cannon window: turret 90 / cannon 60) and the
    /// cannon-slot velocity X range was 0-3 instead of 0-2; (7) the
    /// turret aim was set ONCE at window open from the saucer's center
    /// — vanilla tracks the target from EACH turret's own mount center
    /// every tick OUTSIDE the window and freezes inside it (now
    /// per-turret aims); (8) the parts were NOT DRAWN AT ALL — PreDraw
    /// now draws the chassis (+GlowMask[48]), the core (11 frames,
    /// +GlowMask[49]), both turrets (18 aim frames each, no glow) and
    /// both cannons (8 frames each, +GlowMask[50], right cannon
    /// flipped); (9) the deathray hung 46 px below the center (a 26 px
    /// gap under the drawn saucer) — vanilla is +46 at FULL scale, +23
    /// halved; its bottom stopped at the target's feet where vanilla
    /// scans to the first solid tile at or below the feet row; the dust
    /// is now vanilla's (2x 228 scale 2.8 + 1/5 spark 31, not 1/10);
    /// (10) the missile drew 90 degrees sideways (vanilla rotation is
    /// velocity angle + PI/2 — the backup's own NOTE flagged this
    /// uncertainty and the decompile settles it), never animated (3
    /// frames, 3 t/f), and had no trail dust (vanilla: 1-2 dust 228/tick
    /// behind it, an 8-dust arming burst, and a 4-dust burst every 5
    /// ticks); its OnKill merged vanilla's two 228 dust loops into one
    /// and omitted the gore; (11) follow/recall updated to the
    /// 2026-11-18 weapon-minion pattern (2000 px teleport, ramped
    /// follow speed).
    ///
    /// SOUNDS (all modern SoundID.* constants, no legacy overloads):
    /// Item12 on deathray spawn and on every turret firing tick (one
    /// sound per tick for the pair, not per turret — throttled from
    /// vanilla's 48/window to 24/window); Item39 on every missile
    /// launch and every scrap shot; Item13 plays on vanilla 450's
    /// first tick (free); Item14 plays in the custom missile's Kill.
    ///
    /// FRAME DATA (verified against decompiled FindFrame + the binary
    /// npcFrameCount table): chassis (392) 4 frames, 3 t/f, double
    /// speed during the attack window (ai[3] 20-259); core (395) 11
    /// frames, 6 t/f, double speed clamped to frames 6-8 during the
    /// window; turrets (393) 18 frames (9 aim poses x 2 firing states);
    /// cannons (394) 8 frames (clock-driven). All five parts are drawn
    /// in PreDraw (chassis +GlowMask[48], core +GlowMask[49], turrets
    /// no glow, cannons +GlowMask[50], right cannon flipped).
    /// </summary>
    public class MartianSaucerMinion : ModProjectile
    {
        public override string Texture => "Terraria/Images/NPC_392";
        // DisplayName comes from the localization file
        // (Mods.CelestialAerus.Projectiles.MartianSaucerMinion.DisplayName).

        // --- Constants (vanilla aiStyle 76 phase-0 values, Classic) ---
        private const int MinionDamage = 6000;
        private const float TargetRange = 1000f;

        // Master clock (Projectile.localAI[0]): 600 ticks, then wraps.
        // ai[2] = the locked side offset in the reposition windows
        // (+/-600 in state 0, +/-300 in state 2) and the sweep
        // direction (+/-1) in the deathray window; reset to 0 on the
        // transitions into states 0 and 2 (vanilla aiStyle 76). ai[1] =
        // current target NPC index (or -1) so the deathray can extend
        // to its feet.
        private const int CycleLength = 600;

        // Clock windows (vanilla Core phase-0 sub-states).
        private const int RepositionAEnd = 20;  // 0-19: reposition
        private const int DeathrayEnd = 260;    // 20-259: deathray sweep
        private const int RepositionBEnd = 280; // 260-279: reposition
        private const int TurretEnd = 420;      // 280-419: turret hover
        private const int RepositionCEnd = 440; // 420-439: reposition
        private const int CannonEnd = 580;      // 440-579: cannon hover
        // 580-599: reposition (same as 0-19).

        // Movement speeds.
        private const float RepositionSpeedA = 16f;
        private const float RepositionSpeedB = 20f;
        private const float SweepSpeed = 3.5f;

        // Turret lasers (SaucerLaser 449): every 6 ticks in [280, 420).
        private const int TurretInterval = 6;
        private const float TurretLaserSpeed = 16f;
        private const float TurretSpread = 0.1309f; // (pi/4)/3/2, ~7.5 deg

        // Cannon missiles (custom): every 20 ticks in [440, 580).
        private const int CannonInterval = 20;
        private const float MissileLaunchSpeed = 8f;
        private const float MissileSpread = 0.3927f; // (pi/4)/2, ~22.5 deg

        // Scrap (SaucerScrap 450): per-tick chances per slot. Vanilla
        // (aiStyle 75): turret window [280, 400): turret slots 60,
        // cannon slots 90; cannon window [440, 580): turret slots 90,
        // cannon slots 60.
        private const int ScrapTurretWindowTurretDenom = 60;
        private const int ScrapTurretWindowCannonDenom = 90;
        private const int ScrapCannonWindowTurretDenom = 90;
        private const int ScrapCannonWindowCannonDenom = 60;
        private const int ScrapTurretWindowEnd = 400; // scrap turret window is 280-399

        // Mount offsets from the minion's center (vanilla offsets halved
        // for 0.5 scale): turrets (+/-60, +29), cannons (+/-49, -13).
        private static readonly Vector2 TurretOffsetLeft = new Vector2(-30f, 14.5f);
        private static readonly Vector2 TurretOffsetRight = new Vector2(30f, 14.5f);
        private static readonly Vector2 CannonOffsetLeft = new Vector2(-24.5f, -6.5f);
        private static readonly Vector2 CannonOffsetRight = new Vector2(24.5f, -6.5f);

        // Distinct hover offset so the boss minions don't stack on one point.
        private static readonly Vector2 HoverOffset = new Vector2(-60f, -420f);

        // --- State (fields; only the owner makes spawn decisions) ---
        private float animCounter; // chassis frame counter (case 392)
        private float coreCounter; // core frame counter (case 395)
        // Per-turret aims (vanilla aiStyle 75: each turret tracks the
        // target from its OWN mount center every tick outside the firing
        // window; the aims freeze inside it).
        private Vector2 turretAimLeft = Vector2.UnitY;
        private Vector2 turretAimRight = Vector2.UnitY;

        public override void SetDefaults()
        {
            // Hitbox matches the 150x80 vanilla hitbox at 0.5 scale.
            Projectile.width = 75;
            Projectile.height = 40;
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
            Projectile.netImportant = true;

            // The charm's ModPlayer refreshes timeLeft while equipped.
            Projectile.timeLeft = 2;
        }

        public override void AI()
        {
            Player player = Main.player[Projectile.owner];
            if (player == null || !player.active || player.dead)
            {
                // Owner gone: just drift to a stop (never dereference a
                // null player — the backup read player.Center here).
                Projectile.velocity *= 0.95f;
                UpdateFrame();
                return;
            }

            // FOLLOW/RECALL (weapon-minion pattern, 2026-11-18):
            // teleport if too far from the owner; ramped follow speed
            // (16 at close range, 60 far away) when chasing home.
            float distToOwner = Vector2.Distance(Projectile.Center, player.Center);
            if (distToOwner > 2000f)
            {
                Projectile.position = player.Center;
                Projectile.velocity *= 0.1f;
                Projectile.netUpdate = true;
            }
            else if (distToOwner > 400f)
            {
                float followSpeed = MathHelper.Clamp(distToOwner * 0.05f, 16f, 60f);
                Projectile.velocity = Vector2.Lerp(Projectile.velocity, (player.Center - Projectile.Center).SafeNormalize(Vector2.Zero) * followSpeed, 0.2f);
            }

            NPC target = FindTarget(TargetRange);
            // The deathray reads the target index from ai[1].
            Projectile.ai[1] = target != null ? target.whoAmI : -1;

            if (target == null)
            {
                // No target: hover near the owner, hold the clock.
                HoverToward(player.Center + HoverOffset, 8f, 0.3f);
                UpdateFrame();
                return;
            }

            // Advance the master clock (deterministic, all clients agree).
            // Vanilla (aiStyle 76) derives the state from the clock
            // BEFORE and AFTER the increment and applies transitions
            // (ai[2] resets) when the state changes.
            int prevClock = (int)Projectile.localAI[0];
            Projectile.localAI[0]++;
            if (Projectile.localAI[0] >= CycleLength)
                Projectile.localAI[0] = 0f;
            int clock = (int)Projectile.localAI[0];

            // --- State transitions (vanilla aiStyle 76) ---
            // Entering state 0 or state 2 resets the side in ai[2] (it
            // is re-locked on the first tick of the new window); entering
            // state 1 sets the sweep direction (+1 if the target is right
            // of the saucer). The 599 -> 0 wrap is NOT a transition
            // (state 0 both sides), so ai[2] keeps its locked side for
            // the 0-19 window.
            int prevState = ClockState(prevClock);
            int state = ClockState(clock);
            if (state != prevState)
            {
                if (state == 0)
                    Projectile.ai[2] = 0f;
                else if (state == 1)
                    Projectile.ai[2] = target.Center.X >= Projectile.Center.X ? 1f : -1f;
                else if (state == 2)
                    Projectile.ai[2] = 0f;
                Projectile.netUpdate = true;
            }

            // --- Turret aim tracking (vanilla aiStyle 75, type 393) ---
            // Each turret tracks the target from its OWN mount center
            // every tick OUTSIDE the firing window [280, 420); inside
            // the window the aims freeze where they are. Turrets never
            // aim upward (Y is clamped >= 0).
            if (clock < RepositionBEnd || clock >= TurretEnd)
            {
                turretAimLeft = TrackTurretAim(Projectile.Center + TurretOffsetLeft, target);
                turretAimRight = TrackTurretAim(Projectile.Center + TurretOffsetRight, target);
            }

            // --- Dispatch on the clock (vanilla state machine) ---
            // 0: reposition A (0-19 and 580-599) — far side, 600 px,
            //    250 px up, 16 px/tick, arrival < 50 px jumps to 19.
            // 1: deathray sweep (20-259) — strafe at 3.5 px/tick.
            // 2: reposition B (260-279) — near side, 300 px, 170 px up,
            //    20 px/tick, arrival < 70 px jumps to 279.
            // 3: turret barrage (280-419) — hover, 6-tick lasers.
            // 4: reposition C (420-439) — above target, 250 px up.
            // 5: cannon barrage (440-579) — hover, 20-tick missiles.
            if (state == 0)
            {
                // The side is LOCKED on the first tick of the window
                // (ai[2] = -600 x Sign(core - target), i.e. the FAR
                // side); it is not recomputed per tick (the backup
                // recomputed it every tick, so the destination could
                // flip mid-flight and ping-pong). Arrival jumps to 19
                // in BOTH windows (the backup jumped to 599 in the
                // second window, wasting 20 ticks of the cycle).
                if (Projectile.ai[2] == 0f)
                {
                    Projectile.ai[2] = -600f * Math.Sign(Projectile.Center.X - target.Center.X);
                    Projectile.netUpdate = true;
                }
                FlyTo(target.Center + new Vector2(Projectile.ai[2], -250f), RepositionSpeedA, 50f, RepositionAEnd - 1);
            }
            else if (state == 1)
            {
                DeathraySweep(target, clock);
            }
            else if (state == 2)
            {
                // The side is LOCKED on the first tick of the window
                // (ai[2] = +300 x Sign(core - target), i.e. the NEAR
                // side — the side the saucer is already on; the backup
                // mirrored state 0's far-side logic, sending the saucer
                // across the target instead). The ground clearance
                // adjusts the DESTINATION (not the velocity) and the
                // arrival check uses the adjusted vector (vanilla).
                if (Projectile.ai[2] == 0f)
                {
                    Projectile.ai[2] = 300f * Math.Sign(Projectile.Center.X - target.Center.X);
                    Projectile.netUpdate = true;
                }
                Vector2 dest = target.Center + new Vector2(Projectile.ai[2], -170f);
                float groundDist = GroundDistance();
                if (groundDist < 170f)
                    dest.Y -= 170f - groundDist;
                Vector2 to = dest - Projectile.Center;
                if (to.Length() < 70f)
                {
                    Projectile.localAI[0] = RepositionBEnd - 1;
                }
                else
                {
                    to.Normalize();
                    Projectile.velocity = Vector2.Lerp(Projectile.velocity, to * RepositionSpeedB, 0.1f);
                }
            }
            else if (state == 3)
            {
                TurretHover(target, clock);
            }
            else if (state == 4)
            {
                FlyTo(target.Center + new Vector2(0f, -250f), RepositionSpeedA, 50f, RepositionCEnd - 1);
            }
            else // state 5
            {
                CannonHover(target, clock);
            }

            UpdateFrame();
        }

        // Vanilla aiStyle 76 state from the clock: 0 = reposition A,
        // 1 = deathray, 2 = reposition B, 3 = turret, 4 = reposition C,
        // 5 = cannon.
        private static int ClockState(int clock)
        {
            if (clock >= CannonEnd)
                return 0;
            if (clock >= RepositionCEnd)
                return 5;
            if (clock >= TurretEnd)
                return 4;
            if (clock >= RepositionBEnd)
                return 3;
            if (clock >= DeathrayEnd)
                return 2;
            if (clock >= RepositionAEnd)
                return 1;
            return 0;
        }

        private void FlyTo(Vector2 dest, float speed, float arriveDist, int jumpClock)
        {
            Vector2 to = dest - Projectile.Center;
            if (to.Length() < arriveDist)
            {
                Projectile.localAI[0] = jumpClock;
                return;
            }
            to.Normalize();
            Projectile.velocity = Vector2.Lerp(Projectile.velocity, to * speed, 0.1f);
        }

        private void DeathraySweep(NPC target, int clock)
        {
            // At window entry (clock 20): spawn the beam. (The sweep
            // direction in ai[2] is set by the state transition in AI(),
            // not here — vanilla sets it when the state CHANGES.)
            if (clock == RepositionAEnd)
            {
                if (Projectile.owner == Main.myPlayer)
                {
                    SoundEngine.PlaySound(SoundID.Item12, Projectile.Center);
                    SpawnFriendlyProjectile(Projectile.Center, Vector2.Zero,
                        ModContent.ProjectileType<SaucerDeathrayFriendly>(), ai0: Projectile.whoAmI);
                }
            }

            // Horizontal strafe at 3.5 px/tick; vertical ground clearance
            // 250 px (rise at -4, lerp 0.05, if too close — but never
            // rise faster than the ground distance, so the saucer can't
            // overshoot into the ground when it's nearly touching it).
            Projectile.velocity.X = SweepSpeed * Projectile.ai[2];
            float groundDist = GroundDistance();
            if (groundDist < 250f)
            {
                float riseTarget = -4f;
                if (4f > groundDist)
                    riseTarget = -groundDist;
                Projectile.velocity.Y = MathHelper.Lerp(Projectile.velocity.Y, riseTarget, 0.05f);
            }
            else
            {
                Projectile.velocity.Y *= 0.95f;
            }
        }

        private void TurretHover(NPC target, int clock)
        {
            // Vanilla aiStyle 76 state 3: X always damps x0.85; Y lerps
            // toward -4 (or -groundDist if closer) when the ground is
            // within 170 px, else damps x0.85. (The backup damped Y
            // x0.85 AND then lerped it — double-damping the rise.)
            Projectile.velocity.X *= 0.85f;
            float groundDist = GroundDistance();
            if (groundDist < 170f)
            {
                float riseTarget = -4f;
                if (4f > groundDist)
                    riseTarget = -groundDist;
                Projectile.velocity.Y = MathHelper.Lerp(Projectile.velocity.Y, riseTarget, 0.05f);
            }
            else
            {
                Projectile.velocity.Y *= 0.85f;
            }

            // The aims are tracked outside the window in AI() and frozen
            // here (vanilla quirk: the tracking code is skipped during
            // the window); each turret fires along its own aim.

            // Fire every 6 ticks from both turret mounts (24 shots per
            // turret, 48 total per window). One Item12 per firing tick.
            if ((clock - RepositionBEnd) % TurretInterval == 0)
            {
                if (Projectile.owner == Main.myPlayer)
                {
                    SoundEngine.PlaySound(SoundID.Item12, Projectile.Center);
                    FireTurretLaser(Projectile.Center + TurretOffsetLeft, turretAimLeft);
                    FireTurretLaser(Projectile.Center + TurretOffsetRight, turretAimRight);
                }
            }

            // Scrap trickle (turret window: 280-399).
            if (Projectile.owner == Main.myPlayer && clock < ScrapTurretWindowEnd)
                RollScrap(ScrapTurretWindowTurretDenom, ScrapTurretWindowCannonDenom);
        }

        // Tracks one turret's aim from its own mount center (vanilla
        // aiStyle 75, type 393): aim at the target, never upward (Y
        // clamped >= 0), normalized; a zero-length aim becomes straight
        // down (vanilla: the NaN check after Normalize()).
        private static Vector2 TrackTurretAim(Vector2 mountCenter, NPC target)
        {
            Vector2 aim = target.Center - mountCenter;
            if (aim.Y < 0f)
                aim.Y = 0f;
            aim.Normalize();
            if (float.IsNaN(aim.X) || float.IsNaN(aim.Y))
                aim = Vector2.UnitY;
            return aim;
        }

        private void FireTurretLaser(Vector2 muzzle, Vector2 aim)
        {
            Vector2 vel = aim.RotatedBy(Main.rand.NextFloat(-1f, 1f) * TurretSpread) * TurretLaserSpeed;
            SpawnFriendlyProjectile(muzzle + vel * 1f, vel, ProjectileID.SaucerLaser);
        }

        private void CannonHover(NPC target, int clock)
        {
            // Full stop.
            Projectile.velocity *= 0.85f;

            // Fire every 20 ticks from both cannon mounts (7 missiles per
            // cannon, 14 total per window). Item39 per launch.
            if ((clock - RepositionCEnd) % CannonInterval == 0)
            {
                if (Projectile.owner == Main.myPlayer)
                {
                    FireCannonMissile(CannonOffsetLeft, -1f);
                    FireCannonMissile(CannonOffsetRight, 1f);
                }
            }

            // Scrap trickle (cannon window: 440-579).
            if (Projectile.owner == Main.myPlayer)
                RollScrap(ScrapCannonWindowTurretDenom, ScrapCannonWindowCannonDenom);
        }

        private void FireCannonMissile(Vector2 mountOffset, float outwardX)
        {
            // Launched horizontally outward from the cannon, +/-22.5 deg
            // spread, speed 8. Vanilla muzzle is the cannon center +
            // (outward x 36, +8) — halved for 0.5 scale: the mount
            // offset + outward x 18 + (0, 4). (The backup ignored the
            // mount offset entirely, so missiles launched from the
            // saucer's center line instead of the cannon barrels.)
            Vector2 outward = new Vector2(outwardX, 0f);
            Vector2 muzzle = Projectile.Center + mountOffset + outward * 18f + new Vector2(0f, 4f);
            Vector2 vel = outward.RotatedBy(Main.rand.NextFloat(-1f, 1f) * MissileSpread) * MissileLaunchSpeed;
            SoundEngine.PlaySound(SoundID.Item39, Projectile.Center);
            SpawnFriendlyProjectile(muzzle, vel, ModContent.ProjectileType<SaucerMissileFriendly>());
        }

        private void RollScrap(int turretDenom, int cannonDenom)
        {
            // One independent roll per mount, per tick. Turret slots fire
            // downward-ish from below the saucer; cannon slots fire
            // upward-ish from above it. Each scrap plays Item39 at the
            // minion's center (plus the projectile's own Item13 on spawn).
            if (Main.rand.Next(turretDenom) == 0)
                FireScrap(turretSlot: true, left: true);
            if (Main.rand.Next(turretDenom) == 0)
                FireScrap(turretSlot: true, left: false);
            if (Main.rand.Next(cannonDenom) == 0)
                FireScrap(turretSlot: false, left: true);
            if (Main.rand.Next(cannonDenom) == 0)
                FireScrap(turretSlot: false, left: false);
        }

        private void FireScrap(bool turretSlot, bool left)
        {
            float sideX = left ? -1f : 1f;
            Vector2 spawn;
            Vector2 baseVel;
            if (turretSlot)
            {
                // Below the saucer, downward-ish.
                spawn = Projectile.Center + new Vector2(sideX * Main.rand.NextFloat(25f, 35f), Main.rand.NextFloat(15f, 22.5f));
                baseVel = new Vector2(sideX * Main.rand.NextFloat(0f, 3f), 1f);
            }
            else
            {
                // Above the saucer, upward-ish. Vanilla cannon slots
                // use X range 0-2 (turret slots use 0-3).
                spawn = Projectile.Center + new Vector2(sideX * Main.rand.NextFloat(15f, 30f), -Main.rand.NextFloat(5f, 15f));
                baseVel = new Vector2(sideX * Main.rand.NextFloat(0f, 2f), -1f);
            }
            Vector2 vel = baseVel.RotatedBy(Main.rand.NextFloat(-1f, 1f) * MissileSpread) * 3f;
            SoundEngine.PlaySound(SoundID.Item39, Projectile.Center);
            SpawnFriendlyProjectile(spawn, vel, ProjectileID.SaucerScrap);
        }

        private float GroundDistance(float maxTiles = 150f)
        {
            // Distance from the minion's bottom to the first solid
            // (non-platform) tile below it, scanning up to maxTiles.
            int tileX = (int)(Projectile.Center.X / 16f);
            int startY = (int)(Projectile.Bottom.Y / 16f);
            if (tileX < 0 || tileX >= Main.maxTilesX)
                return maxTiles * 16f;
            int endY = Math.Min(startY + (int)maxTiles, Main.maxTilesY - 1);
            for (int y = startY; y <= endY; y++)
            {
                Tile tile = Main.tile[tileX, y];
                if (tile != null && tile.HasTile && Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType])
                    return y * 16f - Projectile.Bottom.Y;
            }
            return maxTiles * 16f;
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

        private void UpdateFrame()
        {
            // Vanilla FindFrame case 392: frame = (counter % 12) / 3, 3
            // ticks per frame; double speed during the attack window
            // (ai[3] 20-259). No different frames — only the speed changes.
            animCounter++;
            if (Projectile.localAI[0] >= 20f && Projectile.localAI[0] < 260f)
                animCounter++;
            if (animCounter >= 12f)
                animCounter = 0f;
            Projectile.frame = ((int)animCounter % 12) / 3;

            // Vanilla FindFrame case 395 (the core): 11 frames, counter++
            // per tick (twice during the deathray window), wrapping at
            // 66; during the window the counter is clamped to [36, 54)
            // (frames 6-8). Frame = (counter % 66) / 6 (computed in
            // PreDraw). Runs on every AI path (including the null-owner
            // and no-target branches, which call UpdateFrame).
            coreCounter++;
            if (coreCounter >= 66f)
                coreCounter = 0f;
            if (Projectile.localAI[0] >= 20f && Projectile.localAI[0] < 260f)
            {
                coreCounter++;
                if (coreCounter >= 54f || coreCounter < 36f)
                    coreCounter = 36f;
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            // Vanilla is 5 NPCs; the minion draws all 5 parts (the backup
            // drew only the chassis — the core, turrets and cannons were
            // invisible). Draw order: chassis -> core -> turrets ->
            // cannons. All parts are vertical-strip frames at 0.5 scale;
            // the chassis sits 1 px below the minion center (vanilla:
            // the chassis center is the core center + (0, 2), halved).
            // Glow masks are drawn in vanilla's (200, 200, 200, 0); the
            // turrets get NO glow mask (vanilla DrawNPC).
            int clock = (int)Projectile.localAI[0];
            Vector2 center = Projectile.Center - Main.screenPosition;

            // --- Chassis (NPC_392, 4 frames) + GlowMask[48] ---
            Texture2D chassis = TextureAssets.Projectile[Projectile.type].Value;
            int chassisFrameHeight = chassis.Height / 4;
            Rectangle chassisFrame = new Rectangle(0, Projectile.frame * chassisFrameHeight, chassis.Width, chassisFrameHeight);
            Vector2 chassisOrigin = chassisFrame.Size() / 2f;
            Vector2 chassisPos = center + new Vector2(0f, 1f);
            Main.EntitySpriteDraw(chassis, chassisPos, chassisFrame, lightColor, Projectile.rotation, chassisOrigin, Projectile.scale, SpriteEffects.None, 0);
            Texture2D chassisGlow = TextureAssets.GlowMask[48].Value;
            Main.EntitySpriteDraw(chassisGlow, chassisPos, chassisFrame, new Color(200, 200, 200, 0), Projectile.rotation, chassisOrigin, Projectile.scale, SpriteEffects.None, 0);

            // --- Core (NPC_395, 11 frames) + GlowMask[49] ---
            // The core IS the minion center. Frame = (counter % 66) / 6.
            Texture2D core = TextureAssets.Npc[395].Value;
            int coreFrameHeight = core.Height / 11;
            int coreFrameIndex = ((int)coreCounter % 66) / 6;
            Rectangle coreFrame = new Rectangle(0, coreFrameIndex * coreFrameHeight, core.Width, coreFrameHeight);
            Vector2 coreOrigin = coreFrame.Size() / 2f;
            Main.EntitySpriteDraw(core, center, coreFrame, lightColor, Projectile.rotation, coreOrigin, Projectile.scale, SpriteEffects.None, 0);
            Texture2D coreGlow = TextureAssets.GlowMask[49].Value;
            Main.EntitySpriteDraw(coreGlow, center, coreFrame, new Color(200, 200, 200, 0), Projectile.rotation, coreOrigin, Projectile.scale, SpriteEffects.None, 0);

            // --- Turrets (NPC_393, 18 frames each, NO glow mask) ---
            // Frame from the turret's own aim; +9 while firing (clock in
            // [280, 420) and clock % 6 <= 2). Origin Y is 8 px above the
            // frame center (vanilla DrawNPC: halfSize + UnitY * -8 for
            // turrets only). Turrets inherit the core's spriteDirection
            // (-1) so they are drawn unflipped.
            Texture2D turret = TextureAssets.Npc[393].Value;
            int turretFrameHeight = turret.Height / 18;
            int firingOffset = clock >= RepositionBEnd && clock < TurretEnd && clock % TurretInterval <= 2 ? 9 : 0;
            int leftFrameIndex = TurretAimFrame(turretAimLeft) + firingOffset;
            int rightFrameIndex = TurretAimFrame(turretAimRight) + firingOffset;
            Rectangle leftTurretFrame = new Rectangle(0, leftFrameIndex * turretFrameHeight, turret.Width, turretFrameHeight);
            Rectangle rightTurretFrame = new Rectangle(0, rightFrameIndex * turretFrameHeight, turret.Width, turretFrameHeight);
            Vector2 turretOrigin = new Vector2(leftTurretFrame.Width / 2f, leftTurretFrame.Height / 2f - 8f);
            Main.EntitySpriteDraw(turret, center + TurretOffsetLeft, leftTurretFrame, lightColor, Projectile.rotation, turretOrigin, Projectile.scale, SpriteEffects.None, 0);
            Main.EntitySpriteDraw(turret, center + TurretOffsetRight, rightTurretFrame, lightColor, Projectile.rotation, turretOrigin, Projectile.scale, SpriteEffects.None, 0);

            // --- Cannons (NPC_394, 8 frames each) + GlowMask[50] ---
            // Left cannon unflipped, right cannon flipped (vanilla: the
            // cannons' spriteDirection is their mount side).
            Texture2D cannon = TextureAssets.Npc[394].Value;
            int cannonFrameHeight = cannon.Height / 8;
            int cannonFrameIndex = CannonFrame(clock);
            Rectangle cannonFrame = new Rectangle(0, cannonFrameIndex * cannonFrameHeight, cannon.Width, cannonFrameHeight);
            Vector2 cannonOrigin = cannonFrame.Size() / 2f;
            Main.EntitySpriteDraw(cannon, center + CannonOffsetLeft, cannonFrame, lightColor, Projectile.rotation, cannonOrigin, Projectile.scale, SpriteEffects.None, 0);
            Main.EntitySpriteDraw(cannon, center + CannonOffsetRight, cannonFrame, lightColor, Projectile.rotation, cannonOrigin, Projectile.scale, SpriteEffects.FlipHorizontally, 0);
            Texture2D cannonGlow = TextureAssets.GlowMask[50].Value;
            Main.EntitySpriteDraw(cannonGlow, center + CannonOffsetLeft, cannonFrame, new Color(200, 200, 200, 0), Projectile.rotation, cannonOrigin, Projectile.scale, SpriteEffects.None, 0);
            Main.EntitySpriteDraw(cannonGlow, center + CannonOffsetRight, cannonFrame, new Color(200, 200, 200, 0), Projectile.rotation, cannonOrigin, Projectile.scale, SpriteEffects.FlipHorizontally, 0);
            return false;
        }

        // Vanilla FindFrame case 393: the turret's aim direction picks a
        // frame 0-8 (down = 4, horizontal = 0/8, shallow = 1/7, diagonal
        // = 2/6, down-steep = 3/5). The firing frames are +9 (PreDraw).
        private static int TurretAimFrame(Vector2 aim)
        {
            if (aim.Y > Math.Abs(aim.X) * 2f)
                return 4;
            if (aim.Y > Math.Abs(aim.X) * 1.5f)
                return aim.X > 0f ? 5 : 3;
            if (Math.Abs(aim.X) > aim.Y * 2f)
                return aim.X > 0f ? 8 : 0;
            if (Math.Abs(aim.X) > aim.Y * 1.5f)
                return aim.X > 0f ? 7 : 1;
            return aim.X > 0f ? 6 : 2;
        }

        // Vanilla FindFrame case 394: frame 4 until clock 420, 4-7
        // during the reposition window (420-439), cycling 0-3 during
        // the cannon window (440-579), then 7 down to 4 (580-599).
        private static int CannonFrame(int clock)
        {
            int t = clock - 300;
            if (t >= 120)
            {
                t -= 120; // t = clock - 420
                if (t >= 160)
                    return 7 - (t - 160) / 5;
                if (t >= 20)
                    return t / 4 % 4;
                return 4 + t / 5;
            }
            return 4;
        }

        /// <summary>
        /// Spawns a projectile owned by this minion's owner, flipped to
        /// friendly with local immunity frames. Only the owner spawns.
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
    }

    /// <summary>
    /// Friendly substitute for the Martian Saucer's Deathray (447).
    /// Vanilla 447's aiStyle 79 kills itself unless ai[0] points at an
    /// active NPC 392/395 in the right state, and aims at a player
    /// target — unusable by a minion. This beam anchors to the Saucer
    /// minion (ai[0] = the minion's projectile index), hangs vertically
    /// from minion-center +23 px (vanilla aiStyle 79: +46 at full
    /// scale, halved) down to the first solid tile at or below the
    /// target's feet row (vanilla: the beam passes a flying target down
    /// to the ground), sweeps with the minion, and lives 240 ticks (the
    /// full 20-259 window). Vanilla 447 texture (4 frames, cycling
    /// every 5 ticks) + vanilla dust (2x 228 scale 2.8 at the base,
    /// 1/5 spark 31).
    /// </summary>
    public class SaucerDeathrayFriendly : ModProjectile
    {
        public override string Texture => "Terraria/Images/Projectile_447";

        private const int MinionDamage = 6000;
        private const int BeamWidth = 30;
        private const int BeamLifetime = 240;
        private const float BeamDrop = 23f;

        public override void SetDefaults()
        {
            Projectile.width = BeamWidth;
            Projectile.height = 100; // resized every tick to the beam length
            Projectile.friendly = true;
            Projectile.hostile = false;
            Projectile.minion = true;
            Projectile.minionSlots = 0; // helper beam, takes no minion slot
            Projectile.damage = MinionDamage;
            Projectile.DamageType = DamageClass.Generic;
            Projectile.knockBack = 0f;
            Projectile.penetrate = -1;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 20;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.timeLeft = BeamLifetime;
        }

        public override void AI()
        {
            // Anchor: ai[0] = the Saucer minion's projectile index.
            int anchorIndex = (int)Projectile.ai[0];
            if (anchorIndex < 0 || anchorIndex >= Main.maxProjectiles)
            {
                Projectile.Kill();
                return;
            }
            Projectile anchor = Main.projectile[anchorIndex];
            if (!anchor.active || anchor.type != ModContent.ProjectileType<MartianSaucerMinion>())
            {
                Projectile.Kill();
                return;
            }

            // Beam top: 23 px below the anchor's center (vanilla aiStyle
            // 79: Center.Y + 46 at full scale, halved for the 0.5-scale
            // minion). Beam bottom (vanilla aiStyle 79): scan from the
            // beam-top row down (max 150 tiles) for the first solid
            // tile AT OR BELOW the target's feet row — the beam passes
            // a flying target down to the ground — and end 2 px into
            // that tile. (The backup stopped at the target's feet, so
            // the beam never reached the ground under a flying target.)
            float beamTop = anchor.Center.Y + BeamDrop;
            int feetRow = (int)(beamTop / 16f);
            int targetIndex = (int)anchor.ai[1];
            if (targetIndex >= 0 && targetIndex < Main.maxNPCs)
            {
                NPC target = Main.npc[targetIndex];
                if (target != null && target.active)
                    feetRow = (int)(target.Bottom.Y / 16f);
            }
            int tileX = (int)(anchor.Center.X / 16f);
            int topRow = (int)(beamTop / 16f);
            int emptyRows = 0;
            if (tileX >= 0 && tileX < Main.maxTilesX)
            {
                if (topRow >= feetRow && IsSolidTile(tileX, topRow))
                {
                    emptyRows = 1;
                }
                else
                {
                    for (; emptyRows < 150 && topRow + emptyRows < Main.maxTilesY; emptyRows++)
                    {
                        int row = topRow + emptyRows;
                        if (row >= feetRow && IsSolidTile(tileX, row))
                        {
                            emptyRows--;
                            break;
                        }
                    }
                }
            }
            int height = (emptyRows + 1) * 16;
            // Trim: if the beam bottom lands inside a solid tile, pull
            // it up to 2 px into the tile (vanilla: height -= bottom %
            // 16 - 2).
            int bottomY = (int)beamTop + height;
            if (tileX >= 0 && tileX < Main.maxTilesX && bottomY / 16 < Main.maxTilesY && IsSolidTile(tileX, bottomY / 16))
                height -= bottomY % 16 - 2;

            int beamHeight = Math.Max(height, 30);
            Projectile.width = BeamWidth;
            Projectile.height = beamHeight;
            Projectile.position = new Vector2(anchor.Center.X - BeamWidth / 2f, beamTop);

            // Teal dust at the beam's base (vanilla aiStyle 79: 2x dust
            // 228/tick in the bottom 16 px, scale 2.8, noGravity,
            // velocity x0.5, X biased by the anchor's velocity) + a
            // spark (1/5 chance, dust 31, vanilla position/velocity).
            for (int i = 0; i < 2; i++)
            {
                int d = Dust.NewDust(new Vector2(Projectile.position.X, Projectile.position.Y + Projectile.height - 16f), BeamWidth, 16, 228, 0f, 0f, 0, default(Color));
                Main.dust[d].noGravity = true;
                Main.dust[d].velocity *= 0.5f;
                Main.dust[d].velocity.X -= i - anchor.velocity.X * 2f / 3f;
                Main.dust[d].scale = 2.8f;
            }
            if (Main.rand.Next(5) == 0)
            {
                Vector2 sparkPos = new Vector2(Projectile.position.X + BeamWidth / 2f - (BeamWidth / 2f * Math.Sign(anchor.velocity.X)) - 4f, Projectile.position.Y + Projectile.height - 16f);
                int d = Dust.NewDust(sparkPos, 4, 16, 31, 0f, 0f, 100, default(Color), 1.5f);
                Main.dust[d].velocity *= 0.5f;
                Main.dust[d].velocity.X -= anchor.velocity.X / 2f;
                Main.dust[d].velocity.Y = -Math.Abs(Main.dust[d].velocity.Y);
            }
        }

        private static bool IsSolidTile(int x, int y)
        {
            Tile tile = Main.tile[x, y];
            return tile != null && tile.HasTile && Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType];
        }

        public override bool PreDraw(ref Color lightColor)
        {
            // Vanilla 447 texture, 4 frames cycling every 5 ticks,
            // stretched vertically to the beam's current height.
            Texture2D texture = TextureAssets.Projectile[Projectile.type].Value;
            const int frameCount = 4;
            int frameHeight = texture.Height / frameCount;
            int frameIndex = ((BeamLifetime - Projectile.timeLeft) / 5) % frameCount;
            Rectangle frame = new Rectangle(0, frameIndex * frameHeight, texture.Width, frameHeight);
            Vector2 scale = new Vector2((float)BeamWidth / frame.Width, (float)Projectile.height / frameHeight);
            Vector2 origin = new Vector2(frame.Width / 2f, 0f);
            Vector2 drawPos = new Vector2(Projectile.Center.X - Main.screenPosition.X, Projectile.position.Y - Main.screenPosition.Y);
            Main.EntitySpriteDraw(texture, drawPos, frame, Color.White, 0f, origin, scale, SpriteEffects.None, 0);
            return false;
        }
    }

    /// <summary>
    /// Friendly substitute for the Martian Saucer's homing missile (448).
    /// Vanilla 448's aiStyle 80 homes via Player.FindClosest and
    /// proximity-detonates only vs players — a friendly port would home
    /// onto the player. This version replicates the behavior with NPC
    /// targeting: 20-tick straight flight at speed 8, then +4 speed,
    /// tile collision on, and 30 ticks of 20%-per-tick steering toward
    /// the nearest enemy locked at arming; 42-px proximity detonation
    /// vs enemies; self-destruct 180 ticks after arming; 112x112
    /// explosion on Kill with SoundID.Item14, vanilla trail/ring dust
    /// (1-2 dust 228/tick behind it, an 8-dust arming burst, a 4-dust
    /// burst every 5 ticks) and the 3-frame cycle (3 t/f).
    ///
    /// The explosion damage is applied directly (SimpleStrikeNPC over
    /// NPCs in the 112x112 box) rather than relying on the engine
    /// re-checking the expanded hitbox after Kill — the hitbox is still
    /// expanded to 112x112 in Kill (vanilla 448 does the same) for the
    /// dust + any engine-side handling.
    ///
    /// Rotation is velocity angle + PI/2 (vanilla aiStyle 80: the 448
    /// sprite points UP at rotation 0, not +X).
    /// </summary>
    public class SaucerMissileFriendly : ModProjectile
    {
        public override string Texture => "Terraria/Images/Projectile_448";

        private const int MinionDamage = 6000;
        private const int ArmTime = 20;           // straight flight before homing
        private const int HomingTime = 30;        // steering ticks after arming
        private const int LifetimeAfterArm = 180; // self-destruct ticks after arming
        private const float ProximityRange = 42f;
        private const int ExplosionSize = 112;

        public override void SetStaticDefaults()
        {
            // Vanilla 448 has 3 frames, cycling every 3 ticks (aiStyle
            // 80). The missile uses the default draw, so projFrames is
            // required (the backup never set it and never animated).
            Main.projFrames[Type] = 3;
        }

        public override void SetDefaults()
        {
            Projectile.width = 14;
            Projectile.height = 14;
            Projectile.friendly = true;
            Projectile.hostile = false;
            Projectile.minion = true;
            Projectile.minionSlots = 0; // helper missile, takes no minion slot
            Projectile.damage = MinionDamage;
            Projectile.DamageType = DamageClass.Generic;
            Projectile.knockBack = 0f;
            Projectile.penetrate = -1;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 20;
            Projectile.tileCollide = false; // until homing activates, then true
            Projectile.ignoreWater = true;
            Projectile.timeLeft = ArmTime + LifetimeAfterArm;
        }

        public override void AI()
        {
            // ai[0] = age in ticks; ai[1] = locked target NPC index (-1 = none).
            Projectile.ai[0]++;

            if (Projectile.ai[0] == ArmTime)
            {
                // Arm: +4 speed, tile collision on, lock onto the nearest
                // enemy, and the 8-dust arming burst (vanilla aiStyle 80
                // ring, (2, 8)). Runs before the rotation is set for
                // this tick, so the burst uses the previous tick's
                // rotation (vanilla order).
                Projectile.velocity = Projectile.velocity.SafeNormalize(Vector2.UnitX) * (Projectile.velocity.Length() + 4f);
                Projectile.tileCollide = true;
                Projectile.ai[1] = FindNearestEnemyIndex(1500f);
                RingBurst(8, new Vector2(2f, 8f));
            }

            // Homing: first 30 ticks after arming, steer 20% of the angle
            // difference per tick toward the locked target. (Not on the
            // arming tick itself — vanilla's arming and homing branches
            // are else-if.)
            if (Projectile.ai[0] > ArmTime && Projectile.ai[0] < ArmTime + HomingTime)
            {
                int targetIndex = (int)Projectile.ai[1];
                if (targetIndex >= 0 && targetIndex < Main.maxNPCs)
                {
                    NPC target = Main.npc[targetIndex];
                    if (target != null && target.active && target.CanBeChasedBy())
                    {
                        float desiredAngle = (target.Center - Projectile.Center).ToRotation();
                        float currentAngle = Projectile.velocity.ToRotation();
                        float diff = MathHelper.WrapAngle(desiredAngle - currentAngle);
                        Projectile.velocity = Projectile.velocity.RotatedBy(diff * 0.2f);
                    }
                }
            }

            // Every 5 ticks after arming, a 4-dust ring burst (vanilla
            // aiStyle 80, ring (2, 4)).
            if (Projectile.ai[0] > ArmTime && (Projectile.ai[0] - ArmTime) % 5f == 0f)
                RingBurst(4, new Vector2(2f, 4f));

            // Rotation (vanilla aiStyle 80: velocity angle + PI/2 — the
            // sprite points up at rotation 0, not +X) and the 3-frame
            // cycle (3 t/f). Set AFTER the homing so the trail dust
            // uses the fresh rotation.
            Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.PiOver2;
            if (++Projectile.frameCounter >= 3)
            {
                Projectile.frameCounter = 0;
                if (++Projectile.frame >= 3)
                    Projectile.frame = 0;
            }

            // Trail dust (vanilla: 1 + armed dust-228 behind the missile
            // every tick — 1 before arming, 2 after — scale 1, noGravity).
            int trailCount = Projectile.ai[0] >= ArmTime ? 2 : 1;
            for (int i = 0; i < trailCount; i++)
            {
                Vector2 trailPos = Projectile.Center + Vector2.UnitY.RotatedBy(Projectile.rotation) * 8f * (i + 1);
                int d = Dust.NewDust(Projectile.Center, 0, 0, 228, 0f, 0f, 0, default(Color));
                Main.dust[d].position = trailPos;
                Main.dust[d].scale = 1f;
                Main.dust[d].noGravity = true;
            }

            // Proximity detonation: any chasable enemy within 42 px.
            // (Vanilla checks proximity from tick 1 — the backup's early
            // return skipped the first 20 ticks.)
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (npc == null || !npc.active || !npc.CanBeChasedBy())
                    continue;
                if (Vector2.DistanceSquared(Projectile.Center, npc.Center) < ProximityRange * ProximityRange)
                {
                    Projectile.Kill();
                    return;
                }
            }

            // Self-destruct 180 ticks after arming.
            if (Projectile.ai[0] >= ArmTime + LifetimeAfterArm)
                Projectile.Kill();
        }

        // Vanilla aiStyle 80 ring burst: dust arranged on a ring behind
        // the missile, rotated by (rotation - PI/2). The arming burst is
        // 8 dust with ring (2, 8); the every-5-ticks burst is 4 dust
        // with ring (2, 4). Scale 1.5, noGravity, zero velocity.
        private void RingBurst(int count, Vector2 ring)
        {
            for (int i = 0; i < count; i++)
            {
                Vector2 offset = Vector2.UnitX * -8f + -Vector2.UnitY.RotatedBy(i * MathHelper.Pi / 4f) * ring;
                offset = offset.RotatedBy(Projectile.rotation - MathHelper.PiOver2);
                int d = Dust.NewDust(Projectile.Center, 0, 0, 228, 0f, 0f, 0, default(Color));
                Main.dust[d].scale = 1.5f;
                Main.dust[d].noGravity = true;
                Main.dust[d].position = Projectile.Center + offset;
                Main.dust[d].velocity = Vector2.Zero;
            }
        }

        private int FindNearestEnemyIndex(float range)
        {
            int best = -1;
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
                    best = i;
                }
            }
            return best;
        }

        public override void OnKill(int timeLeft)
        {
            // 112x112 explosion (vanilla 448 Kill, verbatim: Item14 +
            // hitbox expansion + 4x dust 31 + 40x TWO dust 228 (scale
            // 2.5 vel x3, and alpha-100 scale 1.5 vel x2) + 1 gore
            // (61-63)). Damage is applied directly to enemies in the
            // box (each missile explodes once, so bypassing immunity
            // frames here is safe).
            SoundEngine.PlaySound(SoundID.Item14, Projectile.Center);
            Projectile.position = Projectile.Center;
            Projectile.width = Projectile.height = ExplosionSize;
            Projectile.position.X -= Projectile.width / 2;
            Projectile.position.Y -= Projectile.height / 2;
            for (int i = 0; i < 4; i++)
                Dust.NewDust(Projectile.position, Projectile.width, Projectile.height, 31, 0f, 0f, 100, default(Color), 1.5f);
            for (int i = 0; i < 40; i++)
            {
                int d1 = Dust.NewDust(Projectile.position, Projectile.width, Projectile.height, 228, 0f, 0f, 0, default(Color), 2.5f);
                Main.dust[d1].noGravity = true;
                Main.dust[d1].velocity *= 3f;
                int d2 = Dust.NewDust(Projectile.position, Projectile.width, Projectile.height, 228, 0f, 0f, 100, default(Color), 1.5f);
                Main.dust[d2].velocity *= 2f;
                Main.dust[d2].noGravity = true;
            }
            int goreIndex = Gore.NewGore(Projectile.GetSource_FromThis(), Projectile.position + new Vector2(Projectile.width * Main.rand.Next(100) / 100f, Projectile.height * Main.rand.Next(100) / 100f) - Vector2.One * 10f, Vector2.Zero, Main.rand.Next(61, 64));
            Main.gore[goreIndex].velocity *= 0.3f;
            Main.gore[goreIndex].velocity.X += Main.rand.Next(-10, 11) * 0.05f;
            Main.gore[goreIndex].velocity.Y += Main.rand.Next(-10, 11) * 0.05f;

            Rectangle boom = new Rectangle((int)Projectile.Center.X - ExplosionSize / 2, (int)Projectile.Center.Y - ExplosionSize / 2, ExplosionSize, ExplosionSize);
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (npc == null || !npc.active || !npc.CanBeChasedBy())
                    continue;
                if (npc.Hitbox.Intersects(boom))
                    npc.SimpleStrikeNPC(MinionDamage, npc.Center.X >= Projectile.Center.X ? 1 : -1, false, 0f, DamageClass.Generic);
            }
        }
    }
}
