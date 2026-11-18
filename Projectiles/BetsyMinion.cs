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
    /// Betsy (NPC 551, DD2Betsy) as a Brigitte's Charm boss minion
    /// (2026-11-15). The Old One's Army dragon with her real 8-step attack
    /// cycle, retargeted at enemies: wind dash, fire breath, fireball
    /// barrage, flying circle, scream + wyvern summon. Half scale, flat
    /// 6000 typeless damage, local immunity frames.
    ///
    /// Attack data from the decompiled Terraria NPC AI (AI_110_Betsy):
    /// see ~/workspace/tmp/boss_attacks_event.md, Betsy section.
    /// Frame data from decompiled FindFrame case 551 (11 frames,
    /// state-keyed — NOT the invented wing-flap loop in boss_frames.md):
    /// intro/hub/dash are frame 0 ALWAYS, circle is 3, breath and
    /// barrage run threshold tables off the state timer, and the scream
    /// runs 1-2-3-4-3-4-3-2-1-0 at 8 t/f and HOLDS frame 0.
    ///
    /// FIXES (2026-11-18, vs decompiled AI_110 + FindFrame case 551 +
    /// AI_136 + DrawNPC): (1) the null-owner branch dereferenced
    /// player.Center when player could be null (NullReferenceException)
    /// — it now just damps velocity; (2) the cycle skip set step=2 /
    /// cycleStep=6, inserting an EXTRA breath — vanilla sets step=6 /
    /// cycleStep=7 (breath, then scream); (3) the dash, circle, and
    /// barrage-dive launches were missing vanilla's +PI rotation when
    /// facing left (the barrage dive set NO rotation at all); (4) the
    /// rotation steering was wrong in every state — replaced with
    /// vanilla's AI_110 tail block verbatim (hub/intro steer at the
    /// target at 0.04 with +PI when facing left; dash/circle locked;
    /// breath staging levels to 0 at 0.01, strafe tilts side*PI/12 at
    /// 0.05; barrage levels to 0 at 0.01; scream levels to 0 at 0.02)
    /// and the backup's rotation=0 snap at breath launch is gone;
    /// (5) the barrage dive was missing vanilla's homing slide
    /// (position += 7 px/tick toward the target, 6 px/tick toward a
    /// point 400 px above it) and the climb ran at tick &gt;= 120 instead
    /// of &gt; 120; (6) the animation was largely invented — rewritten
    /// to FindFrame case 551 verbatim (see the frame-data note above),
    /// including the breath launch off-by-one (the backup launched on
    /// the 41st tick; vanilla launches on the 40th); (7) the wings were
    /// NOT DRAWN AT ALL — PreDraw now draws the far wing (Extra[82]),
    /// body, GlowMask[226], and near wing (Extra[81]) with vanilla's
    /// origins/attach points, plus the 3 afterimages; (8) the staging
    /// side was locked during breath/barrage — vanilla re-derives it
    /// every tick, and the hub side LOCKS at hub entry; (9) the summon
    /// ellipse was halved (300/150) — vanilla's is 600/300 (world
    /// distances, not halved) — and the cap counted only FRIENDLY
    /// wyverns where vanilla counts ALL of them; (10) the breath mouth
    /// was unrotated and the beam was purely horizontal — vanilla's
    /// mouth is rotated by Betsy's rotation and the beam runs from her
    /// center toward the mouth (angled down ~30 deg); the dust/light
    /// are now vanilla AI_136 verbatim (PlotTileLine light, dust 6/31,
    /// gore) and the draw is vanilla's 15-segment flame jet (the
    /// stretched texture smeared the 7-frame sheet); the collision
    /// grows over 25 ticks, not 40; (11) the scream tile dust was a
    /// generic box — now vanilla's real surface-layer tile dust + gore;
    /// the scream phase timer starts at 1 (vanilla's ai[1]), so the
    /// sound/rise/summons/dust all land on the right ticks; (12) hub
    /// and scream-approach movement now use vanilla's SimpleFlyMovement
    /// (per-axis velocity approach, double accel across zero); (13)
    /// follow/recall updated to the 2026-11-18 weapon-minion pattern
    /// (2000 px teleport, ramped follow speed).
    ///
    /// CAVEAT (not fixed — a redesign): the state/cycleStep/stateTimer
    /// are private fields, not Projectile.ai[], so they desync in
    /// multiplayer (PickNextAttack uses Main.rand per-client). Works in
    /// single-player.
    ///
    /// Cycle (vanilla): dash, dash, breath, dash, barrage, circle-or-breath
    /// (1-in-3 the circle is skipped for a breath), breath, scream. Every
    /// attack is preceded by a 30-tick hover hub.
    ///
    /// SUBSTITUTED — FIRE BREATH: vanilla projectile 687 (DD2BetsyFlameBreath)
    /// anchors to an NPC index in ai[1] (it follows Main.npc[ai[1]]). The
    /// minion is a PROJECTILE, so it has no NPC index to pass — a direct
    /// port would anchor the beam to a random NPC or die on an out-of-range
    /// index. The breath is re-implemented below as BetsyFlameBreathFriendly,
    /// a custom ModProjectile that anchors to the minion projectile and
    /// reproduces the vanilla beam: 400 px long, 40 px wide, grows outward
    /// over the first 40 ticks, retracts from tick 38, dies at 78 ticks,
    /// with fire dust and warm light. The On Fire! debuff (vanilla:
    /// OnHitPlayer-only) IS applied to enemies via OnHitNPC (7-10 s).
    ///
    /// ADAPTED — SCREAM SUMMONS: the vanilla bonus spawns anchor to DD2 lane
    /// portals (NPC 549), which only exist during the event — dropped. The
    /// ellipse spawn (1 wyvern per wave at ticks 20/45/70) is kept, spawning
    /// FRIENDLY DD2WyvernT3 (560) as VISUAL ONLY allies (vanilla wyvern AI
    /// targets players/crystals, so as friendly NPCs they cannot attack),
    /// capped at 4 alive (vanilla cap). The tile-dust bursts are kept as
    /// visual dust.
    ///
    /// SOUNDS: DD2_BetsyWindAttack (dash), DD2_BetsyFlameBreath (breath
    /// start), DD2_BetsyFlyingCircleAttack (circle), DD2_BetsyScream
    /// (scream), DD2_BetsySummon (each wyvern). The fireball sounds
    /// (DD2_BetsyFireballShot / Impact) play from the projectiles, free.
    /// </summary>
    public class BetsyMinion : ModProjectile
    {
        public override string Texture => "Terraria/Images/NPC_551";
        // DisplayName comes from the localization file
        // (Mods.CelestialAerus.Projectiles.BetsyMinion.DisplayName).

        // --- Constants (vanilla AI_110_Betsy values) ---
        private const int MinionDamage = 6000;
        private const float TargetRange = 1000f;

        // States (vanilla ai[0]).
        private const int StateIntro = 0;
        private const int StateHub = 1;
        private const int StateDash = 2;
        private const int StateBreath = 3;
        private const int StateBarrage = 4;
        private const int StateCircle = 5;
        private const int StateScream = 6;

        private const int IntroDuration = 10;
        private const int HubDuration = 30;
        private const float HubSpeed = 7.5f;
        private const float HubAccel = 0.45f;

        private const int DashDuration = 30;
        private const float DashSpeed = 23f;

        private const int BreathDuration = 120;
        private const int BreathStaging = 40;    // staging ticks 0-39, breath at 40
        private const float BreathStageSpeed = 12f;
        private const float BreathStrafeSpeed = 10f;
        private const float BreathMaxStrafeSpeed = 20f;

        private const int BarrageDuration = 180;
        private const int BarrageStaging = 60;   // staging ticks 0-59, dive at 60
        private const float BarrageStageSpeed = 13f;
        private const float BarrageDiveSpeed = 12f;

        private const int CircleDuration = 60;
        private const float CircleSpeed = 32f;

        private const int ScreamDuration = 90;
        private const int ScreamApproachMax = 180;
        private const float ScreamApproachSpeed = 15f;
        private const float ScreamApproachAccel = 0.9f;
        private const int MaxWyverns = 4;

        // Scream wing-flap sequence (vanilla: 1-2-3-4-3-4-3-2-1-0 at 8 t/f).
        private static readonly int[] ScreamFrames = { 1, 2, 3, 4, 3, 4, 3, 2, 1, 0 };

        // Distinct hover offset so the boss minions don't stack on one point.
        private static readonly Vector2 HoverOffset = new Vector2(-300f, -320f);

        // --- State (fields; only the owner makes spawn decisions) ---
        private int state;
        private int stateTimer;
        private int cycleStep;
        private int side = 1; // +1 = target to the right, -1 = to the left
        private bool approaching = true; // scream state: still flying in
        // Hub side (vanilla ai[2] in the hub): locks on the first hub
        // tick with a target, re-derived on every hub entry. 0 = unset.
        private int hubSide;
        // Wing-flap timer (vanilla localAI[0]): advances at a
        // state-specific rate, wraps at 36. Wing frame = (int)wingTimer / 4.
        private float wingTimer;

        public override void SetStaticDefaults()
        {
            // Hardcoded frame count (11 — see boss_frames.md). PreDraw uses
            // the same constant; Main.npcFrameCount is never consulted.
            Main.projFrames[Type] = 11;
        }

        public override void SetDefaults()
        {
            // Hitbox matches the 190x90 vanilla hitbox at 0.5 scale.
            Projectile.width = 95;
            Projectile.height = 45;
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
            Projectile.netImportant = true;
        }

        public override void AI()
        {
            Player player = Main.player[Projectile.owner];
            if (player == null || !player.active || player.dead)
            {
                // Owner gone: drift to a stop and idle (player may be
                // null here — never dereference it).
                Projectile.velocity *= 0.95f;
                return;
            }

            // RECALL + FAST FOLLOW (2026-11-18 weapon-minion pattern):
            // speed ramps with distance, teleport only past 2000 px.
            // A recall resets the attack state to the hub and clears the
            // afterimage history (so no ghost streak crosses the screen).
            float distToOwner = Vector2.Distance(Projectile.Center, player.Center);
            if (distToOwner > 2000f)
            {
                Projectile.Center = player.Center + HoverOffset;
                Projectile.velocity = Vector2.Zero;
                Projectile.netUpdate = true;
                state = StateHub;
                stateTimer = 0;
                for (int i = 0; i < Projectile.oldPos.Length; i++)
                {
                    Projectile.oldPos[i] = Projectile.position;
                    Projectile.oldRot[i] = Projectile.rotation;
                }
                for (int i = 0; i < 10; i++)
                    Dust.NewDust(Projectile.position, Projectile.width, Projectile.height, DustID.GoldFlame);
            }
            else if (distToOwner > 400f)
            {
                // Speed ramps with distance: 20 at 400px, 40 at 800px,
                // 60 past 1200px.
                float followSpeed = MathHelper.Clamp(distToOwner * 0.05f, 16f, 60f);
                Vector2 toPlayer = (player.Center - Projectile.Center).SafeNormalize(Vector2.Zero);
                Projectile.velocity = Vector2.Lerp(Projectile.velocity, toPlayer * followSpeed, 0.2f);
            }

            NPC target = FindTarget(TargetRange);
            // Facing updates in states where it is free — during the dash
            // and circle the rotation/facing is locked at launch, and
            // during breath/barrage the side is re-derived inside the
            // state cases (vanilla ai[2]). The scream phase does NOT
            // update facing (vanilla only updates it during the approach).
            if (target != null && (state == StateIntro || state == StateHub || (state == StateScream && approaching)))
                side = target.Center.X >= Projectile.Center.X ? 1 : -1;
            Projectile.spriteDirection = side;

            switch (state)
            {
                case StateIntro:
                    Projectile.velocity *= 0.95f;
                    stateTimer++;
                    if (stateTimer >= IntroDuration)
                    {
                        state = StateHub;
                        stateTimer = 0;
                    }
                    break;

                case StateHub:
                    if (target != null)
                    {
                        // The hub side LOCKS on the first hub tick with a
                        // target (vanilla ai[2] is set once when it is 0);
                        // the hub point is target + (-hubSide x 300, -200).
                        // Movement is vanilla's SimpleFlyMovement (per-axis
                        // velocity approach, double accel across zero).
                        if (stateTimer == 0)
                            hubSide = Projectile.Center.X < target.Center.X ? 1 : -1;
                        Vector2 destination = target.Center + new Vector2(-hubSide * 300f, -200f);
                        Vector2 desiredVelocity = (destination - Projectile.Center).SafeNormalize(Vector2.Zero) * HubSpeed;
                        SimpleFlyMovement(desiredVelocity, HubAccel);
                        stateTimer++;
                        if (stateTimer >= HubDuration)
                            PickNextAttack();
                    }
                    else
                    {
                        FlyToward(player.Center + HoverOffset, HubSpeed, HubAccel);
                    }
                    break;

                case StateDash:
                    if (stateTimer == 0)
                    {
                        // Launch: straight-line dash at the target's position
                        // as of launch (no tracking, no re-aim). Rotation is
                        // locked at launch for the whole dash — and vanilla
                        // adds PI when facing left (the sprite is drawn
                        // flipped, so the rotation is measured in the
                        // flipped frame).
                        Vector2 dir = target != null
                            ? (target.Center - Projectile.Center).SafeNormalize(Vector2.UnitX)
                            : new Vector2(side, 0f);
                        side = dir.X > 0f ? 1 : -1;
                        Projectile.spriteDirection = side;
                        Projectile.velocity = dir * DashSpeed;
                        Projectile.rotation = dir.ToRotation();
                        if (side == -1)
                            Projectile.rotation += MathHelper.Pi;
                        SoundEngine.PlaySound(SoundID.DD2_BetsyWindAttack, Projectile.Center);
                    }
                    stateTimer++;
                    if (stateTimer >= DashDuration)
                    {
                        state = StateHub;
                        stateTimer = 0;
                    }
                    break;

                case StateBreath:
                    // Vanilla increments ai[1] at the START of the breath
                    // state (unlike the barrage, which increments at the
                    // end) — so stateTimer is incremented BEFORE the
                    // checks here. The three blocks below are separate
                    // `if`s in vanilla, not an else-if chain: the launch
                    // tick also runs the strafe accel.
                    stateTimer++;
                    if (stateTimer < BreathStaging)
                    {
                        // Staging: move directly to target + (-side x 600,
                        // -250) at 12 px/tick (position-based). The staging
                        // side is re-derived EVERY tick (vanilla ai[2]).
                        // Snap to the point if within one step, else step
                        // toward it; skip ahead if within 16 px AFTER
                        // moving (vanilla checks after the move).
                        if (target != null)
                        {
                            int stageSide = target.Center.X >= Projectile.Center.X ? 1 : -1;
                            Vector2 staging = target.Center + new Vector2(-stageSide * 600f, -250f);
                            Vector2 to = staging - Projectile.Center;
                            if (to.Length() < BreathStageSpeed)
                            {
                                Projectile.Center = staging;
                            }
                            else
                            {
                                Projectile.velocity = Vector2.Zero;
                                Projectile.Center += to.SafeNormalize(Vector2.Zero) * BreathStageSpeed;
                            }
                            if (Vector2.Distance(staging, Projectile.Center) < 16f)
                                stateTimer = BreathStaging - 1;
                        }
                    }
                    if (stateTimer == BreathStaging)
                    {
                        // Launch: horizontal strafe toward the target at
                        // 10 px/tick, spawn the breath, start the sound.
                        // The side is re-derived at launch (vanilla num35).
                        // No rotation snap — the rotation tail levels the
                        // dragon (vanilla sets no rotation at launch).
                        int launchSide = target != null
                            ? (target.Center.X >= Projectile.Center.X ? 1 : -1)
                            : side;
                        side = launchSide;
                        Projectile.spriteDirection = side;
                        Projectile.velocity = new Vector2(side * BreathStrafeSpeed, 0f);
                        if (Projectile.owner == Main.myPlayer)
                        {
                            SpawnFriendlyProjectile(Projectile.Center, Vector2.Zero,
                                ModContent.ProjectileType<BetsyFlameBreathFriendly>(),
                                MinionDamage, 0f, Projectile.whoAmI, side);
                        }
                        SoundEngine.PlaySound(SoundID.DD2_BetsyFlameBreath, Projectile.Center);
                    }
                    if (stateTimer >= BreathStaging)
                    {
                        // Strafe: accelerate +0.5/tick up to 20 while the
                        // target is more than 550 px away horizontally
                        // (vanilla uses Sign(velocity.X) for the direction).
                        if (target != null && Math.Abs(target.Center.X - Projectile.Center.X) > 550f
                            && Math.Abs(Projectile.velocity.X) < BreathMaxStrafeSpeed)
                        {
                            Projectile.velocity.X += Math.Sign(Projectile.velocity.X) * 0.5f;
                        }
                        Projectile.velocity.Y = 0f;
                    }
                    if (stateTimer >= BreathDuration)
                    {
                        state = StateHub;
                        stateTimer = 0;
                    }
                    break;

                case StateBarrage:
                    // The staging side is re-derived EVERY tick (vanilla
                    // ai[2]), and the facing follows it during staging.
                    if (target != null)
                    {
                        side = target.Center.X >= Projectile.Center.X ? 1 : -1;
                        Projectile.spriteDirection = side;
                    }
                    if (stateTimer < BarrageStaging)
                    {
                        // Staging: fly to target + (-side x 1500, -350) at
                        // speed 13 (velocity lerped at 1/30 per tick). Skip
                        // ahead if within 16 px.
                        if (target != null)
                        {
                            Vector2 staging = target.Center + new Vector2(-side * 1500f, -350f);
                            Vector2 to = staging - Projectile.Center;
                            Vector2 desired = to.SafeNormalize(Vector2.Zero) * BarrageStageSpeed;
                            Projectile.velocity = Vector2.Lerp(Projectile.velocity, desired, 1f / 30f);
                            if (Vector2.Distance(staging, Projectile.Center) < 16f)
                                stateTimer = BarrageStaging - 1;
                        }
                    }
                    else if (stateTimer == BarrageStaging)
                    {
                        // Dive: flattened direction-to-target x 12 (the Y
                        // component is flattened x0.25 at dive start).
                        // Rotation is set at the dive (+PI when facing
                        // left) and the facing follows the dive direction
                        // — the backup set no rotation at all here.
                        if (target != null)
                        {
                            Vector2 dir = (target.Center - Projectile.Center).SafeNormalize(Vector2.UnitX);
                            dir.Y *= 0.25f;
                            dir = dir.SafeNormalize(new Vector2(side, 0f));
                            side = dir.X > 0f ? 1 : -1;
                            Projectile.spriteDirection = side;
                            Projectile.rotation = dir.ToRotation();
                            if (side == -1)
                                Projectile.rotation += MathHelper.Pi;
                            Projectile.velocity = dir * BarrageDiveSpeed;
                        }
                    }
                    else
                    {
                        // Homing slide (vanilla): the position slides
                        // 7 px/tick toward the target horizontally and
                        // 6 px/tick toward a point 400 px above the target
                        // vertically — the dive tracks the target.
                        if (target != null)
                        {
                            Projectile.position.X += (target.Center - Projectile.Center).SafeNormalize(Vector2.Zero).X * 7f;
                            Vector2 above = target.Center + new Vector2(0f, -400f);
                            Projectile.position.Y += (above - Projectile.Center).SafeNormalize(Vector2.Zero).Y * 6f;
                        }
                        // Fireballs at ticks 69, 79, 89, 99, 109, 119 — one
                        // every 10 ticks. Each inherits Betsy's current
                        // velocity (they fly along the dive line).
                        if (stateTimer >= 69 && stateTimer <= 119 && (stateTimer - 69) % 10 == 0
                            && Projectile.owner == Main.myPlayer)
                        {
                            Vector2 mouth = Projectile.Center + new Vector2(70f * side, 10f).RotatedBy(Projectile.rotation);
                            SpawnFriendlyProjectile(mouth, Projectile.velocity, ProjectileID.DD2BetsyFireball);
                        }
                        // After tick 120: climb for the remaining 60 ticks
                        // (vanilla: ai[1] > 120, not >=).
                        if (stateTimer > 120)
                            Projectile.velocity.Y -= 0.1f;
                    }
                    stateTimer++;
                    if (stateTimer >= BarrageDuration)
                    {
                        state = StateHub;
                        stateTimer = 0;
                    }
                    break;

                case StateCircle:
                    if (stateTimer == 0)
                    {
                        // Entry velocity 32 px/tick toward the target.
                        // Rotation is locked at launch (+PI when facing
                        // left, as in the dash).
                        Vector2 dir = target != null
                            ? (target.Center - Projectile.Center).SafeNormalize(Vector2.UnitX)
                            : new Vector2(side, 0f);
                        side = dir.X > 0f ? 1 : -1;
                        Projectile.spriteDirection = side;
                        Projectile.velocity = dir * CircleSpeed;
                        Projectile.rotation = dir.ToRotation();
                        if (side == -1)
                            Projectile.rotation += MathHelper.Pi;
                    }
                    if (stateTimer == 1)
                        SoundEngine.PlaySound(SoundID.DD2_BetsyFlyingCircleAttack, Projectile.Center);
                    // One full revolution over the 60-tick state, while the
                    // position slides 10 px/tick toward the target and rises
                    // 0.1 px/tick — a tightening spiral, not a closed loop.
                    Projectile.velocity = Projectile.velocity.RotatedBy(-MathHelper.TwoPi / 60f * side);
                    if (target != null)
                        Projectile.Center = Projectile.Center + (target.Center - Projectile.Center).SafeNormalize(Vector2.Zero) * 10f;
                    Projectile.Center = new Vector2(Projectile.Center.X, Projectile.Center.Y - 0.1f);
                    Projectile.rotation -= MathHelper.TwoPi / 60f * side;
                    stateTimer++;
                    if (stateTimer >= CircleDuration)
                    {
                        Projectile.velocity /= 2f;
                        state = StateHub;
                        stateTimer = 0;
                    }
                    break;

                case StateScream:
                    if (approaching)
                    {
                        // Approach flight at 15 px/tick toward target +
                        // (0, -200) (vanilla SimpleFlyMovement, accel 0.9);
                        // ends within 350 px of the target or after
                        // 180 ticks.
                        if (target != null)
                        {
                            Vector2 destination = target.Center + new Vector2(0f, -200f);
                            Vector2 desiredVelocity = (destination - Projectile.Center).SafeNormalize(Vector2.Zero) * ScreamApproachSpeed;
                            SimpleFlyMovement(desiredVelocity, ScreamApproachAccel);
                        }
                        stateTimer++;
                        if ((target != null && Vector2.Distance(Projectile.Center, target.Center) <= 350f)
                            || stateTimer >= ScreamApproachMax)
                        {
                            approaching = false;
                            // Vanilla's scream ai[1] starts at 1 in the
                            // phase (the approach used ai[2] as its
                            // timer) — the phase checks (sound, rise,
                            // summons, dust, animation) are all keyed on
                            // ai[1], so the phase timer starts at 1.
                            stateTimer = 1;
                        }
                    }
                    else
                    {
                        // Decelerate (x0.95 for the first 20 ticks, x0.98
                        // after) and rise at tick 20.
                        Projectile.velocity *= stateTimer < 20 ? 0.95f : 0.98f;
                        if (stateTimer == 20)
                        {
                            if (Projectile.velocity.Y > 0f)
                                Projectile.velocity.Y /= 3f;
                            Projectile.velocity.Y -= 3f;
                        }
                        if (stateTimer == 1)
                            SoundEngine.PlaySound(SoundID.DD2_BetsyScream, Projectile.Center);
                        // Wyvern summons at ticks 20, 45, 70 (1 per wave;
                        // the lane-portal bonus spawns are event-only).
                        if ((stateTimer == 20 || stateTimer == 45 || stateTimer == 70)
                            && Projectile.owner == Main.myPlayer)
                        {
                            SummonWyvern(target);
                        }
                        // Tile-dust bursts at ticks 20, 25, 30 (visual only).
                        if (stateTimer == 20 || stateTimer == 25 || stateTimer == 30)
                            DustBurst();
                        stateTimer++;
                        if (stateTimer >= ScreamDuration)
                        {
                            state = StateHub;
                            stateTimer = 0;
                        }
                    }
                    break;
            }

            // --- Rotation steering (vanilla AI_110 tail, verbatim) ---
            // After the state machine, the rotation steers toward a
            // state-specific target at a state-specific rate. The +PI
            // adjustments compensate for the sprite being drawn flipped
            // when facing left. (The backup steered toward the target in
            // breath staging and scream, toward 0 at the wrong rate in
            // the strafe, and toward the velocity in the barrage dive —
            // all wrong targets/rates.)
            Vector2 steerTarget = target != null ? target.Center : player.Center;
            float rotTarget = (steerTarget - Projectile.Center).ToRotation();
            float rotRate = 0.04f;
            switch (state)
            {
                case StateDash:
                case StateCircle:
                    rotRate = 0f; // rotation locked at launch
                    break;
                case StateBreath:
                    rotRate = 0.01f;
                    rotTarget = 0f;
                    if (Projectile.spriteDirection == -1)
                        rotTarget -= MathHelper.Pi;
                    if (stateTimer >= BreathStaging)
                    {
                        rotTarget += Projectile.spriteDirection * MathHelper.Pi / 12f;
                        rotRate = 0.05f;
                    }
                    break;
                case StateBarrage:
                    rotRate = 0.01f;
                    rotTarget = MathHelper.Pi;
                    if (Projectile.spriteDirection == 1)
                        rotTarget += MathHelper.Pi;
                    break;
                case StateScream:
                    rotRate = 0.02f;
                    rotTarget = 0f;
                    if (Projectile.spriteDirection == -1)
                        rotTarget -= MathHelper.Pi;
                    break;
            }
            if (Projectile.spriteDirection == -1)
                rotTarget += MathHelper.Pi;
            if (rotRate != 0f)
                SteerRotationAngle(rotTarget, rotRate);

            // --- Animation (vanilla FindFrame case 551, verbatim) ---
            // At animation time the minion's stateTimer == vanilla's ai[1]
            // (both increment once per tick before FindFrame runs).
            switch (state)
            {
                case StateCircle:
                    Projectile.frame = 3;
                    break;
                case StateScream:
                    if (approaching)
                    {
                        Projectile.frame = 1;
                    }
                    else
                    {
                        // 1-2-3-4-3-4-3-2-1-0 at 8 ticks per step, HOLDING
                        // frame 0 after tick 72 — not a loop.
                        Projectile.frame = ScreamFrames[Math.Min(stateTimer / 8, 9)];
                    }
                    break;
                case StateBreath:
                    Projectile.frame = BreathBarrageFrame(stateTimer, BreathStaging, 98);
                    break;
                case StateBarrage:
                    Projectile.frame = BreathBarrageFrame(stateTimer, BarrageStaging, 123);
                    break;
                default: // intro, hub, dash: frame 0 ALWAYS (the backup's
                         // "full 0-10 wing-flap loop" for hub/intro was
                         // invented — vanilla holds frame 0).
                    Projectile.frame = 0;
                    break;
            }

            // Wing-flap timer (vanilla localAI[0]): advances at a
            // state-specific rate, wraps at 36. The wing frame is
            // (int)wingTimer / 4 (see PreDraw).
            float wingRate = 1f;
            switch (state)
            {
                case StateBreath:
                    wingRate = 1.5f;
                    break;
                case StateBarrage:
                    wingRate = stateTimer <= 120 ? 1.5f : 1f;
                    break;
                case StateCircle:
                    wingRate = 0.7f;
                    break;
                case StateScream:
                    wingRate = approaching ? 1f : 0.85f;
                    break;
            }
            wingTimer += wingRate;
            if (wingTimer >= 36f)
                wingTimer = 0f;
        }

        /// <summary>
        /// Vanilla FindFrame case 551 breath/barrage frame table. Both
        /// states share the same shape: frame 5, then 6 from tick 5, 7
        /// from (launch-6), 8 from (launch-3), alternating 9/10 from the
        /// launch tick (3 t/f), then 8 from the return tick, 7 five ticks
        /// later, 0 ten ticks later. Breath: launch 40, return 98.
        /// Barrage: launch 60, return 123.
        /// </summary>
        private static int BreathBarrageFrame(int t, int launch, int ret)
        {
            if (t < 5)
                return 5;
            if (t < launch - 6)
                return 6;
            if (t < launch - 3)
                return 7;
            if (t < launch)
                return 8;
            if (t < ret)
                return 9 + (t / 3) % 2;
            if (t < ret + 5)
                return 8;
            if (t < ret + 10)
                return 7;
            return 0;
        }

        /// <summary>
        /// Picks the next attack from the vanilla cycle: 0 dash, 1 dash,
        /// 2 breath, 3 dash, 4 barrage, 5 circle, 6 breath, 7 scream. At
        /// step 5 there is a 1-in-3 chance the circle is skipped for a
        /// breath instead (counter moves to 6).
        /// </summary>
        private void PickNextAttack()
        {
            int step = cycleStep;
            cycleStep = (cycleStep + 1) % 8;
            if (step == 5 && Main.rand.Next(3) == 0)
            {
                // The circle is skipped for a breath: vanilla does ai[3]++
                // (5 -> 6, breath) and then ai[3]++ again (6 -> 7, scream
                // next) — NOT step 2 / cycleStep 6, which would insert an
                // EXTRA breath (breath from step 5, then breath again
                // from step 6).
                step = 6;
                cycleStep = 7;
            }
            if (step == 0 || step == 1 || step == 3)
                state = StateDash;
            else if (step == 2 || step == 6)
                state = StateBreath;
            else if (step == 4)
                state = StateBarrage;
            else if (step == 5)
                state = StateCircle;
            else
                state = StateScream;
            approaching = true;
            stateTimer = 0;
        }

        private void SummonWyvern(NPC target)
        {
            // Cap: vanilla counts ALL active DD2WyvernT3 (CountNPCS(560)
            // > 4), not just friendly ones — a hostile wyvern from the
            // event counts against the cap too.
            if (CountNPCs(NPCID.DD2WyvernT3) > MaxWyverns)
                return;
            float angle = Main.rand.NextFloat() * MathHelper.TwoPi;
            float radiusFactor = Main.rand.NextFloat(0.6f, 1f);
            // Vanilla ellipse: rotation-vector x (2, 1) x 300 x (0.6-1.0)
            // — an X radius up to 600 px and a Y radius up to 300 px.
            // These are WORLD distances and are NOT halved for the minion.
            Vector2 offset = new Vector2((float)Math.Cos(angle) * 600f * radiusFactor,
                (float)Math.Sin(angle) * 300f * radiusFactor);
            Vector2 point = Projectile.Center + offset;
            // The spawn point must be more than 100 px from the target.
            if (target != null && Vector2.Distance(point, target.Center) <= 100f)
                return;
            int idx = NPC.NewNPC(Projectile.GetSource_FromThis(), (int)point.X, (int)point.Y, NPCID.DD2WyvernT3);
            if (idx >= 0 && idx < Main.maxNPCs)
            {
                NPC wyvern = Main.npc[idx];
                wyvern.friendly = true;
                wyvern.netUpdate = true;
                SoundEngine.PlaySound(SoundID.DD2_BetsySummon, point);
            }
        }

        private void DustBurst()
        {
            // Vanilla AI_110 scream dust: real tile dust from the surface
            // layer (solid non-frameImportant tiles below a non-solid
            // tile) in a 61 x ~47 tile area around Betsy, plus gore. No
            // tiles are destroyed.
            Point centerTile = Projectile.Center.ToTileCoordinates();
            int halfWidth = 30;
            int height = 35;
            int power = 4;
            for (int i = centerTile.X - halfWidth; i <= centerTile.X + halfWidth; i++)
            {
                bool aboveWasOpen = false;
                for (int j = centerTile.Y - height / 3; j < centerTile.Y + height; j++)
                {
                    Tile tile = Framing.GetTileSafely(i, j);
                    bool isSurface = tile.HasTile && Main.tileSolid[tile.TileType] && !Main.tileFrameImportant[tile.TileType];
                    if (aboveWasOpen && isSurface)
                    {
                        int amount = WorldGen.KillTile_GetTileDustAmount(true, tile, i, j);
                        for (int k = 0; k < amount; k++)
                        {
                            Dust dust = Main.dust[WorldGen.KillTile_MakeTileDust(i, j, tile)];
                            dust.velocity.Y -= 3f + power * 1.5f;
                            dust.velocity.Y *= Main.rand.NextFloat();
                            dust.scale += power * 0.03f;
                        }
                        for (int l = 0; l < amount - 1; l++)
                        {
                            Dust dust2 = Main.dust[WorldGen.KillTile_MakeTileDust(i, j, tile)];
                            dust2.velocity.Y -= 1f + power;
                            dust2.velocity.Y *= Main.rand.NextFloat();
                        }
                        if (amount > 0 && Main.rand.Next(3) != 0)
                        {
                            float distFactor = Math.Abs(centerTile.X - i) / (halfWidth / 2f);
                            Gore gore = Gore.NewGoreDirect(Projectile.GetSource_FromThis(), Projectile.position, Vector2.Zero, 61 + Main.rand.Next(3), 1f - power * 0.15f + distFactor * 0.5f);
                            gore.velocity.Y -= 0.1f + power * 0.5f + distFactor * power;
                            gore.velocity.Y *= Main.rand.NextFloat();
                            gore.position = new Vector2(i * 16 + 20, j * 16 + 20);
                        }
                    }
                    aboveWasOpen = !isSurface;
                }
            }
        }

        private void SteerRotationAngle(float targetAngle, float rate)
        {
            float diff = MathHelper.WrapAngle(targetAngle - Projectile.rotation);
            Projectile.rotation += MathHelper.Clamp(diff, -rate, rate);
        }

        /// <summary>
        /// Vanilla NPC.SimpleFlyMovement: per-axis approach to a desired
        /// velocity, with double acceleration when crossing zero.
        /// </summary>
        private void SimpleFlyMovement(Vector2 desiredVelocity, float moveSpeed)
        {
            if (Projectile.velocity.X < desiredVelocity.X)
            {
                Projectile.velocity.X += moveSpeed;
                if (Projectile.velocity.X < 0f && desiredVelocity.X > 0f)
                    Projectile.velocity.X += moveSpeed;
            }
            else if (Projectile.velocity.X > desiredVelocity.X)
            {
                Projectile.velocity.X -= moveSpeed;
                if (Projectile.velocity.X > 0f && desiredVelocity.X < 0f)
                    Projectile.velocity.X -= moveSpeed;
            }
            if (Projectile.velocity.Y < desiredVelocity.Y)
            {
                Projectile.velocity.Y += moveSpeed;
                if (Projectile.velocity.Y < 0f && desiredVelocity.Y > 0f)
                    Projectile.velocity.Y += moveSpeed;
            }
            else if (Projectile.velocity.Y > desiredVelocity.Y)
            {
                Projectile.velocity.Y -= moveSpeed;
                if (Projectile.velocity.Y > 0f && desiredVelocity.Y < 0f)
                    Projectile.velocity.Y -= moveSpeed;
            }
        }

        private void FlyToward(Vector2 target, float maxSpeed, float accel)
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

        private static int CountNPCs(int type)
        {
            int count = 0;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (npc != null && npc.active && npc.type == type)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Spawns a projectile owned by this minion's owner, flipped to
        /// friendly with local immunity frames — vanilla boss projectiles
        /// default to hostile, which would damage the player instead of
        /// enemies.
        /// </summary>
        private int SpawnFriendlyProjectile(Vector2 position, Vector2 velocity, int type, int damage = MinionDamage, float ai0 = 0f, float ai1 = 0f, float ai2 = 0f)
        {
            if (Projectile.owner != Main.myPlayer)
                return -1;
            int p = Projectile.NewProjectile(Projectile.GetSource_FromThis(), position, velocity, type, damage, 0f, Projectile.owner, ai0, ai1, ai2);
            if (p >= 0 && p < Main.maxProjectiles)
            {
                Main.projectile[p].friendly = true;
                Main.projectile[p].hostile = false;
                Main.projectile[p].DamageType = DamageClass.Generic;
                Main.projectile[p].usesLocalNPCImmunity = true;
                Main.projectile[p].localNPCHitCooldown = 20;
            }
            return p;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D texture = TextureAssets.Projectile[Projectile.type].Value;
            // Frame count is HARDCODED (11 — Betsy's texture has 11 frames;
            // see boss_frames.md). Main.npcFrameCount returns 0/1 for NPCs
            // whose textures haven't loaded yet, which drew the whole sprite
            // sheet. Projectile.frame is the state-keyed animation, set in AI().
            const int frameCount = 11;
            int frameHeight = texture.Height / frameCount;
            Rectangle frame = new Rectangle(0, Projectile.frame * frameHeight, texture.Width, frameHeight);
            // Vanilla DrawNPC (type 551): the body origin is (150.5, 78) in
            // texture pixels (mirrored when flipped), and the sprite is
            // drawn flipped OPPOSITE to the usual rule (val16 ^ 1: facing
            // right -> None, facing left -> Flip).
            Vector2 drawPos = Projectile.Center - Main.screenPosition;
            Vector2 bodyOrigin = new Vector2(150.5f, 78f);
            SpriteEffects effects = Projectile.spriteDirection == 1 ? SpriteEffects.None : SpriteEffects.FlipHorizontally;
            bool flipped = (effects & SpriteEffects.FlipHorizontally) != 0;
            if (flipped)
                bodyOrigin.X = frame.Width - bodyOrigin.X;

            // Wings (vanilla: Extra[82] far wing behind the body, Extra[81]
            // near wing above it). The wing frame is (int)wingTimer / 4
            // (0-8) in a 2x5 grid. The attach offsets are screen-space
            // offsets from the body center, so they are halved for the
            // 0.5-scale minion (the origins stay in texture pixels).
            int wingFrame = (int)wingTimer / 4;
            Texture2D farWing = TextureAssets.Extra[82].Value;
            Rectangle farWingFrame = farWing.Frame(2, 5, wingFrame / 5, wingFrame % 5);
            Vector2 farWingOrigin = new Vector2(16f, 176f);
            Vector2 farWingOffset = new Vector2(79.5f, -26f);
            if (flipped)
            {
                farWingOrigin.X = farWingFrame.Width - farWingOrigin.X;
                farWingOffset.X *= -1f;
            }
            Texture2D nearWing = TextureAssets.Extra[81].Value;
            Rectangle nearWingFrame = nearWing.Frame(2, 5, wingFrame / 5, wingFrame % 5);
            Vector2 nearWingOrigin = new Vector2(215f, 170f);
            Vector2 nearWingOffset = new Vector2(20.5f, -34f);
            if (flipped)
            {
                nearWingOrigin.X = nearWingFrame.Width - nearWingOrigin.X;
                nearWingOffset.X *= -1f;
            }
            farWingOffset *= Projectile.scale;
            nearWingOffset *= Projectile.scale;

            // Afterimages (vanilla: oldPos[9, 6, 3] at 35% fade, each
            // drawing far wing + body + near wing with the old rotation).
            // A (0, 0) oldPos means the history isn't filled yet (the
            // first 10 ticks) — skip those ghosts.
            for (int i = 9; i > 0; i -= 3)
            {
                if (Projectile.oldPos[i] == Vector2.Zero)
                    continue;
                Vector2 ghostPos = Projectile.oldPos[i] + Projectile.Size / 2f - Main.screenPosition;
                float ghostRot = Projectile.oldRot[i];
                Color ghostColor = lightColor * (1f - i / 10f) * 0.35f;
                ghostColor.A /= 2;
                Main.EntitySpriteDraw(farWing, ghostPos + farWingOffset.RotatedBy(ghostRot), farWingFrame, ghostColor, ghostRot, farWingOrigin, Projectile.scale, effects, 0);
                Main.EntitySpriteDraw(texture, ghostPos, frame, ghostColor, ghostRot, bodyOrigin, Projectile.scale, effects, 0);
                Main.EntitySpriteDraw(nearWing, ghostPos + nearWingOffset.RotatedBy(ghostRot), nearWingFrame, ghostColor, ghostRot, nearWingOrigin, Projectile.scale, effects, 0);
            }

            // Far wing, body, glow, near wing (vanilla draw order). The
            // glow is Lerp(light, White, 0.6) with alpha 66, x1.3
            // (vanilla's lerpValue2 is overwritten to 2, so the factor is
            // 0.7 + 0.3*2 = 1.3).
            Main.EntitySpriteDraw(farWing, drawPos + farWingOffset.RotatedBy(Projectile.rotation), farWingFrame, lightColor, Projectile.rotation, farWingOrigin, Projectile.scale, effects, 0);
            Main.EntitySpriteDraw(texture, drawPos, frame, lightColor, Projectile.rotation, bodyOrigin, Projectile.scale, effects, 0);
            Texture2D glow = TextureAssets.GlowMask[226].Value;
            Color glowColor = Color.Lerp(lightColor, Color.White, 0.6f);
            glowColor.A = 66;
            Main.EntitySpriteDraw(glow, drawPos, frame, glowColor * 1.3f, Projectile.rotation, bodyOrigin, Projectile.scale, effects, 0);
            Main.EntitySpriteDraw(nearWing, drawPos + nearWingOffset.RotatedBy(Projectile.rotation), nearWingFrame, lightColor, Projectile.rotation, nearWingOrigin, Projectile.scale, effects, 0);
            return false;
        }
    }

    /// <summary>
    /// Betsy's flame breath, re-implemented as a friendly minion projectile
    /// (2026-11-15). Vanilla 687 (DD2BetsyFlameBreath) anchors to an NPC
    /// index in ai[1] — the minion is a projectile, so a direct port is
    /// impossible. This version anchors to the BetsyMinion projectile
    /// (ai[1] = the minion's whoAmI) and reproduces the vanilla beam:
    /// 400 px long, 40 px wide, growing outward over the first 40 ticks,
    /// retracting from tick 38, dead at 78 ticks, with fire dust and warm
    /// light. ai[2] = beam direction (+1 right, -1 left), set at spawn.
    ///
    /// The beam is angled (vanilla AI_136: the mouth is at NPC center +
    /// ((110-8) x spriteDirection, 30) rotated by the NPC's rotation,
    /// and the beam runs from the NPC's center toward the mouth — about
    /// 30 degrees below horizontal while strafing). The mouth offset
    /// (51 x direction, 15) is the vanilla (102 x spriteDirection, 30)
    /// halved for the 0.5-scale minion. The draw is vanilla's 15-segment
    /// flame jet (see PreDraw), not a stretched texture.
    ///
    /// The On Fire! debuff (vanilla: OnHitPlayer-only) is applied to
    /// enemies via OnHitNPC for 7-10 seconds.
    /// </summary>
    public class BetsyFlameBreathFriendly : ModProjectile
    {
        public override string Texture => "Terraria/Images/Projectile_687";

        private const int BeamLength = 400;
        private const int BeamWidth = 40;
        private const int BeamLife = 78;

        public override void SetDefaults()
        {
            Projectile.width = 30;
            Projectile.height = 30;
            Projectile.friendly = true;
            Projectile.hostile = false;
            Projectile.damage = 6000;
            Projectile.DamageType = DamageClass.Generic;
            Projectile.knockBack = 0f;
            Projectile.penetrate = -1;

            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 20;

            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.timeLeft = BeamLife;
        }

        public override void AI()
        {
            int ownerIndex = (int)Projectile.ai[1];
            if (ownerIndex < 0 || ownerIndex >= Main.maxProjectiles)
            {
                Projectile.Kill();
                return;
            }
            Projectile owner = Main.projectile[ownerIndex];
            if (owner == null || !owner.active || owner.type != ModContent.ProjectileType<BetsyMinion>())
            {
                Projectile.Kill();
                return;
            }

            // Anchor to the minion's mouth every tick (vanilla AI_136
            // re-anchors to the owner NPC the same way). The mouth is at
            // owner center + ((110-8) x spriteDirection, 30) rotated by
            // the owner's rotation — halved to (51, 15) for the 0.5-scale
            // minion. The beam runs from the owner's center toward the
            // mouth (slightly downward, NOT purely horizontal), and the
            // projectile's rotation is the beam direction (Colliding and
            // PreDraw both use it).
            Vector2 mouth = owner.Center + new Vector2(51f * owner.spriteDirection, 15f).RotatedBy(owner.rotation);
            Projectile.Center = mouth;
            Projectile.rotation = (mouth - owner.Center).SafeNormalize(Vector2.UnitX).ToRotation();
            Vector2 beamDir = Projectile.rotation.ToRotationVector2();

            // Warm light along the beam (vanilla AI_136: 3 PlotTileLine
            // passes — center and +/-0.196 rad — width 16, color
            // (1.2, 1, 0.3)).
            DelegateMethods.v3_1 = new Vector3(1.2f, 1f, 0.3f);
            float num2 = Math.Min(Projectile.ai[0] / 40f, 1f);
            float num3 = Math.Max((Projectile.ai[0] - 38f) / 40f, 0f);
            Utils.PlotTileLine(Projectile.Center + beamDir * BeamLength * num3, Projectile.Center + beamDir * BeamLength * num2, 16f, DelegateMethods.CastLight);
            Utils.PlotTileLine(Projectile.Center + beamDir.RotatedBy(0.19634954f) * BeamLength * num3, Projectile.Center + beamDir.RotatedBy(0.19634954f) * BeamLength * num2, 16f, DelegateMethods.CastLight);
            Utils.PlotTileLine(Projectile.Center + beamDir.RotatedBy(-0.19634954f) * BeamLength * num3, Projectile.Center + beamDir.RotatedBy(-0.19634954f) * BeamLength * num2, 16f, DelegateMethods.CastLight);

            // Fire dust (vanilla: 3x dust 6/tick at the mouth while the
            // beam is growing and num2 > 0.1).
            if (num3 == 0f && num2 > 0.1f)
            {
                for (int i = 0; i < 3; i++)
                {
                    Dust dust = Dust.NewDustDirect(Projectile.position, Projectile.width, Projectile.height, DustID.Torch);
                    dust.fadeIn = 1.5f;
                    dust.velocity = beamDir.RotatedBy(Main.rand.NextFloatDirection() * (MathHelper.Pi / 12f)) * (0.5f + Main.rand.NextFloat() * 2.5f) * 15f;
                    dust.velocity += owner.velocity * 2f;
                    dust.noLight = true;
                    dust.noGravity = true;
                    dust.alpha = 200;
                }
            }

            // Ember gore (vanilla: 1/5 chance per tick from tick 15, at
            // 300 px out along the beam).
            if (Main.rand.Next(5) == 0 && Projectile.ai[0] >= 15f)
            {
                Gore gore = Gore.NewGoreDirect(Projectile.GetSource_FromThis(), Projectile.Center + beamDir * 300f - Utils.RandomVector2(Main.rand, -20f, 20f), Vector2.Zero, 61 + Main.rand.Next(3), 0.5f);
                gore.velocity *= 0.3f;
                gore.velocity += beamDir * 4f;
            }

            // Spark dust (vanilla: 1x dust 31/tick, repositioned along the
            // beam with a perpendicular spread).
            {
                Dust dust2 = Dust.NewDustDirect(Projectile.position, Projectile.width, Projectile.height, DustID.Smoke);
                dust2.fadeIn = 1.5f;
                dust2.scale = 0.4f;
                dust2.velocity = beamDir.RotatedBy(Main.rand.NextFloatDirection() * (MathHelper.Pi / 12f)) * (0.5f + Main.rand.NextFloat() * 2.5f) * 15f;
                dust2.velocity += owner.velocity * 2f;
                dust2.velocity *= 0.3f;
                dust2.noLight = true;
                dust2.noGravity = true;
                float along = Main.rand.NextFloat();
                dust2.position = Vector2.Lerp(Projectile.Center + beamDir * BeamLength * num3, Projectile.Center + beamDir * BeamLength * num2, along);
                dust2.position += beamDir.RotatedBy(MathHelper.PiOver2) * (20f + 100f * (along - 0.5f));
            }

            // Vanilla increments frameCounter then ai[0] at the END of the
            // AI (ai[0] is 0 on the first tick). The frameCounter drives
            // the draw's growth/retract (see PreDraw).
            Projectile.frameCounter++;
            Projectile.ai[0]++;
            if (Projectile.ai[0] >= BeamLife)
                Projectile.Kill();
        }

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            // Vanilla 687 collision: the beam's COLLISION length grows over
            // the first 25 ticks (num42 = ai[0] / 25), while the VISUAL
            // grows over 40 — the hitbox reaches full length 15 ticks
            // before the flame does. The near end retracts from tick 38
            // (num43), so the flame front sweeps forward.
            float num42 = Math.Min(Projectile.ai[0] / 25f, 1f);
            float num43 = Math.Max((Projectile.ai[0] - 38f) / 40f, 0f);
            Vector2 beamDir = Projectile.rotation.ToRotationVector2();
            Vector2 start = Projectile.Center + beamDir * BeamLength * num43;
            Vector2 end = Projectile.Center + beamDir * BeamLength * num42;
            float collisionPoint = 0f;
            return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(), start, end, BeamWidth, ref collisionPoint);
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            // Vanilla applies On Fire! on hit vs players only; the friendly
            // port applies it to enemies (7-10 seconds).
            target.AddBuff(BuffID.OnFire, 60 * Main.rand.Next(7, 11));
        }

        public override bool PreDraw(ref Color lightColor)
        {
            // Vanilla DrawProj for type 687: 15 flame segments along the
            // beam, each a 1x7 frame of the texture, positioned by
            // SmoothStep along a slightly jittered beam direction, with a
            // white -> red -> black -> transparent gradient and
            // per-segment spin. The growth/retract gating uses
            // frameCounter (which increments once per tick in AI): while
            // it is under 40 only the segments behind the growth front
            // show; from 40 on, only the segments ahead of the retracting
            // near end show. (The backup's stretched texture smeared the
            // 7-frame flame sheet into a blur.)
            Texture2D texture = TextureAssets.Projectile[ProjectileID.DD2BetsyFlameBreath].Value;
            Vector2 beamStart = Projectile.Center - Main.screenPosition;
            float growth = Projectile.frameCounter / 40f;
            Color segmentColor = Color.Transparent;
            Color whiteHot = new Color(255, 255, 255, 0);
            Color redHot = new Color(180, 30, 30, 200);
            Color dark = new Color(0, 0, 0, 30);
            ulong seed = 1uL;
            for (float i = 0f; i < 15f; i++)
            {
                float jitter = Utils.RandomFloat(ref seed) * 0.25f - 0.125f;
                Vector2 segDir = (Projectile.rotation + jitter).ToRotationVector2();
                Vector2 segEnd = beamStart + segDir * BeamLength;
                float pos = growth + i * (1f / 15f);
                int lap = (int)(pos / (1f / 15f));
                pos %= 1f;
                if ((!(pos > growth % 1f) || !(Projectile.frameCounter < 40)) && (!(pos < growth % 1f) || !(Projectile.frameCounter >= 40)))
                {
                    if (pos < 0.1f)
                        segmentColor = Color.Lerp(Color.Transparent, whiteHot, Utils.GetLerpValue(0f, 0.1f, pos, true));
                    else if (pos < 0.35f)
                        segmentColor = whiteHot;
                    else if (pos < 0.7f)
                        segmentColor = Color.Lerp(whiteHot, redHot, Utils.GetLerpValue(0.35f, 0.7f, pos, true));
                    else if (pos < 0.9f)
                        segmentColor = Color.Lerp(redHot, dark, Utils.GetLerpValue(0.7f, 0.9f, pos, true));
                    else
                        segmentColor = (!(pos < 1f)) ? Color.Transparent : Color.Lerp(dark, Color.Transparent, Utils.GetLerpValue(0.9f, 1f, pos, true));
                    float scale = 0.9f + pos * 0.8f;
                    scale *= scale;
                    scale *= 0.8f;
                    Vector2 position = Vector2.SmoothStep(beamStart, segEnd, pos);
                    Rectangle segFrame = texture.Frame(1, 7, 0, (int)(pos * 7f));
                    Main.EntitySpriteDraw(texture, position, segFrame, segmentColor, Projectile.rotation + MathHelper.TwoPi * (pos + Main.GlobalTimeWrappedHourly * 1.2f) * 0.2f + lap * (MathHelper.TwoPi / 5f), segFrame.Size() / 2f, scale, SpriteEffects.None, 0);
                }
            }
            return false;
        }
    }
}
