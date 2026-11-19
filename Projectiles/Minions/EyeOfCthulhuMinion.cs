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
    /// Eye of Cthulhu boss minion (Brigitte's Charm, 2026-11-15): the
    /// vanilla Eye (NPC 4, aiStyle 4) rewritten as a friendly minion at
    /// 50% visual scale. It runs the real boss's attack cycle — phase-1
    /// hover with Servant spawns, triple dashes, the spinning
    /// transformation, phase-2 hover, faster triple dashes, the Expert
    /// rapid-dash barrage with target leading, and the below-target
    /// reposition — aimed at the owner's enemies instead of the player.
    /// Attack data from the decompiled Terraria 1.4.4.9 aiStyle 4 block
    /// (see ~/workspace/tmp/boss_attacks_batch1.md, Part 1).
    ///
    /// FIXED 2026-11-18 (vs decompiled vanilla Terraria 1.4.5 NPC.cs
    /// aiStyle 4, lines 24965-25840, cross-checked against the 1.4.4.9
    /// spec — the Eye's AI is stable across versions):
    /// - DASH ROTATION (the big one): vanilla sets rotation =
    ///   atan2(velocity) - 1.57 EVERY tick during the dash full-speed
    ///   window (mouth-first — the sprite's BOTTOM leads), in the P1
    ///   dash (first 39 ticks), P2 dash (first 39/49), and barrage
    ///   (first 19 ticks of each dash). The backup set +Pi/2 at launch
    ///   only — 180 degrees wrong AND frozen for the whole dash. Now
    ///   set per-tick in the window; during decay the rotation eases
    ///   toward the target at vanilla's num8 rates (P1 0.05, P2 0.08,
    ///   barrage 0.15, all x1.5 in Expert); at dash end it snaps to
    ///   face the target (vanilla sets rotation = num7).
    /// - HOVER ROTATION RATES: vanilla eases at 0.02 (P1 hover), 0.05
    ///   (P2 hover, reposition), x1.5 in Expert. The backup used a flat
    ///   0.1 everywhere — 5x too fast in P1. Now per-state.
    /// - P1 HOVER SPEED/ACCEL: vanilla 5 / 0.04 Classic, 7 / 0.15
    ///   Expert (backup: flat 6 / 0.07). Hover point is now 200 px
    ///   above the target's center (vanilla), not 180.
    /// - P2 HOVER TIERS: vanilla measures the 400/600/800 px tiers from
    ///   the distance to the HOVER POINT, not to the target. Fixed.
    /// - DASH DECAY (Expert): vanilla applies the base decay ALWAYS and
    ///   the Expert decay ADDITIONALLY (P1: 0.98 x 0.985 = 0.9653; P2:
    ///   0.97 x 0.98 = 0.9506). The backup replaced the base with the
    ///   Expert value (0.985 / 0.98) — the eye kept too much speed in
    ///   Expert. Fixed; decay now starts at stateTimer &gt;= decayStart
    ///   (vanilla: ai[2] &gt;= 40) instead of &gt;, and near-zero velocity
    ///   components are zeroed during decay (vanilla).
    /// - BARRAGE LEAD SIGN INVERTED: vanilla aims at target MINUS
    ///   velocity x lead (it aims BEHIND the target's motion — the
    ///   Eye's lead is deliberately backwards), with the Y lead
    ///   quartered (velocity.Y x lead / 4). The backup ADDED the full
    ///   velocity. Fixed.
    /// - BARRAGE CLOSE-RANGE QUIRK: at &lt;100 px (aim-point distance)
    ///   with a mostly-horizontal aim, vanilla SWAPS the velocity
    ///   components with TOWARD-PLAYER signs (vel.X = -/+|vel.Y|,
    ///   vel.Y = -/+|vel.X|, signs chosen so the dash still heads
    ///   toward the target). The backup rotated (-vel.Y, vel.X), which
    ///   can dash AWAY from the target. Fixed, no re-normalize.
    /// - BARRAGE DIAGONAL-IZE: at &gt;=100 px with a mostly-horizontal
    ///   aim, vanilla sets both components to the mean magnitude with
    ///   TOWARD-PLAYER signs (not the velocity's own signs) and does
    ///   NOT re-normalize (magnitude drops to m*sqrt(2)). The backup
    ///   used the velocity's signs and re-normalized to full speed.
    ///   Fixed.
    /// - BARRAGE JITTER: vanilla does NOT re-normalize after the +/-2
    ///   jitter (only the &lt;4%-life flag3 branch re-normalizes, and
    ///   the minion has no life so that branch is unreachable). The
    ///   backup re-normalized. Fixed.
    /// - BARRAGE WINDOW EXTENSION: vanilla extends the 20-tick window
    ///   REPEATEDLY while within 200 px (ai[2]-- every tick at the
    ///   boundary). The backup extended once via a flag. Flag removed.
    /// - TRANSFORM GORE: vanilla spawns gore 8, 7, 6 (x2 each) — the
    ///   backup used 143 (the wrong gore). Fixed; spawn position is now
    ///   the hitbox top-left (vanilla) instead of the center.
    /// - SERVANT SPAWNS: vanilla spawns 10 dust (type 5) at the spawn
    ///   point with the servant's velocity x 0.4, for BOTH the aimed
    ///   spawn and the transform spray — the backup spawned no dust.
    ///   The spray now spawns at center + velocity x 10 (vanilla), not
    ///   at the center. The servant counter RESETS when leaving the P1
    ///   hover (vanilla zeroes ai[3] at the hover-&gt;dash transition);
    ///   the backup let it persist across dashes. The 500 px servant
    ///   range is now measured to the HOVER POINT (vanilla num17), not
    ///   to the target's center. Rotation snaps to face the target on
    ///   each servant spawn (vanilla sets rotation = num7).
    /// - AMBIENT DUST: vanilla spawns 1 dust (type 5) with 1-in-5
    ///   chance EVERY tick (all states) in the middle band of the eye.
    ///   The backup had none. Added.
    /// - DRAW OFFSET: vanilla draws the sprite center 4 px below the
    ///   NPC center (Main.DrawNPC literal +4). Applied as +4 * scale.
    /// - Added Projectile.netImportant = true (weapon-minion pattern).
    /// - Doc fixes: the header claimed LegacySoundStyle (does not exist
    ///   in tML 1.4.4 — the path-based SoundStyle was already correct);
    ///   boosted barrage count is vanilla 5-8 (rand(-3,1) start), not
    ///   6-8.
    ///
    /// VERIFIED CORRECT (no change): frame cycle 0-1-2 at 7 t/f shifting
    /// +3 after the shell sheds (FindFrame 90898), 6 frames, 50x55
    /// halved hitbox, P1 hover/servant/dash timings (600/210 hover,
    /// 110/44 servant interval, 150/100 dash time, 3 dashes/set, 2 sets
    /// before transform), transform 2x100 ticks with spin ramp 0.005,
    /// P2 hover 200 ticks at speed 6 / accel 0.07, P2 dash speeds
    /// 6.8 / x1.15 / x1.3 (Expert), barrage 5 dashes at speed 20 with
    /// 20-tick window + 13 decay, boosted first dash speed 26 / lead
    /// 40, reposition 70 ticks at speed 9 / accel 0.3 to 600 px below
    /// the target, ForceRoar on P2 dash and barrage launches, Roar +
    /// NPCHit1 at the transform midpoint, silence on P1 dashes.
    /// NOT PORTED (life-based, unreachable for a minion): flag2/flag3
    /// low-life modes (Expert, life &lt; 12% / &lt; 4% — the flag3
    /// barrage variant with double error, extra jitter and window 10,
    /// and the barrage abort when above the target).
    ///
    /// Minion adaptations (the boss has no minion equivalent for these):
    /// - The phase transition is CYCLE-based (after 2 triple-dash sets)
    ///   instead of life-based — minions have no life fraction. The
    ///   transformation is one-way, like the real boss (no looping back
    ///   to phase 1).
    /// - Servants spawn as FRIENDLY Servant of Cthulhu NPCs. Their
    ///   vanilla AI targets players, so they cannot attack enemies; they
    ///   are a faithful visual port (launched at the target at the
    ///   vanilla speed) and are capped at 6 so they cannot fill the NPC
    ///   array. The Expert servant spray during the transformation is
    ///   included in all modes.
    /// - The rapid barrage and reposition are Expert-only in vanilla;
    ///   the minion performs them in all modes (the charm is an endgame
    ///   item — the minion is the boss at its best). Classic/Expert
    ///   NUMBERS (speeds, timings, decay) still follow the world
    ///   difficulty via Main.expertMode, like the other boss minions.
    /// - The boosted barrage fires a fixed 7 dashes (vanilla: 5-8,
    ///   random) so the count fits the state machine cleanly.
    /// - Daytime flee / despawn is NOT ported (flagged do-not-port).
    /// - All damage is flat 6000, typeless (DamageClass.Generic), per
    ///   the charm's design — the vanilla contact numbers in the spec
    ///   are informational only. Contact damage comes from the minion
    ///   body overlapping enemies (penetrate -1, local immunity frames).
    /// - Sounds: NPCHit1 on every phase-1 servant spawn, NPCHit1 + Roar
    ///   at the transformation midpoint (with the gore burst), ForceRoar
    ///   on every phase-2 dash launch (triple dash and barrage). No
    ///   sound on phase-1 dashes or the transformation spray (vanilla
    ///   is silent there). Roar/ForceRoar use path-based SoundStyles
    ///   because SoundID.Roar / SoundID.ForceRoar are const ints, not
    ///   SoundStyles (same as the Twins minions).
    /// </summary>
    public class EyeOfCthulhuMinion : ModProjectile
    {
        public override string Texture => "Terraria/Images/NPC_4";

        // --- Spec constants (vanilla aiStyle 4, Eye of Cthulhu) ---
        private const float TargetRange = 1000f;
        private const int MinionDamage = 6000;     // flat, typeless (charm design)

        // Hover offset near the player (no target): above-LEFT, so the
        // boss minions don't all stack on one point.
        private static readonly Vector2 IdleOffset = new Vector2(-140f, -180f);
        // Fighting hover X offset (anti-stacking). The Y offsets are
        // vanilla: P1 hovers 200 px above the target's center, P2 120 px.
        private const float FightHoverX = -140f;
        private const float Phase1HoverHeight = 200f;
        private const float Phase2HoverHeight = 120f;

        // Phase 1 hover + Servant spawn (Attack 1)
        private const int P1HoverTime = 600;       // Classic (Expert: 210)
        private const int P1HoverTimeExpert = 210;
        private const float P1HoverSpeed = 5f;     // Classic (Expert: 7)
        private const float P1HoverSpeedExpert = 7f;
        private const float P1HoverAccel = 0.04f;  // Classic (Expert: 0.15)
        private const float P1HoverAccelExpert = 0.15f;
        private const int ServantInterval = 110;   // Classic (Expert: 44)
        private const int ServantIntervalExpert = 44;
        private const float ServantRange = 500f;
        private const float ServantSpeed = 5f;     // Classic (Expert: 6)
        private const float ServantSpeedExpert = 6f;
        private const float ServantSpraySpeed = 5f; // transformation spray (Expert)
        private const int MaxServants = 6;         // safety cap; vanilla has none

        // Phase 1 triple dash (Attack 2)
        private const float DashSpeedP1 = 6f;      // Classic (Expert: 7)
        private const float DashSpeedP1Expert = 7f;
        private const int DashTimeP1 = 150;        // Classic (Expert: 100)
        private const int DashTimeP1Expert = 100;
        private const int DashDecayStartP1 = 40;
        // Decay: vanilla applies 0.98 ALWAYS, then 0.985 ADDITIONALLY in
        // Expert (combined 0.9653) — NOT a replacement.
        private const float DashDecayP1 = 0.98f;
        private const float DashDecayP1ExpertExtra = 0.985f;
        private const int DashesPerSet = 3;
        private const int DashSetsBeforeTransform = 2;

        // Transformation (Attack 3): 2 stages x 100 ticks
        private const int TransformStageTime = 100;
        private const int TransformSprayInterval = 20; // Expert servant spray

        // Phase 2 hover
        private const int P2HoverTime = 200;
        private const float P2HoverSpeed = 6f;
        private const float P2HoverAccel = 0.07f;
        // Expert distance tiers: +1 speed / +0.05 accel past 400 / 600 /
        // 800 px from the HOVER POINT, up to speed 9 / accel 0.22.

        // Phase 2 triple dash (Attack 4)
        private const float DashSpeedP2 = 6.8f;
        private const int DashTimeP2 = 130;        // Classic (Expert: 90)
        private const int DashTimeP2Expert = 90;
        private const int DashDecayStartP2 = 40;   // Classic (Expert: 50)
        private const int DashDecayStartP2Expert = 50;
        // Decay: vanilla applies 0.97 ALWAYS, then 0.98 ADDITIONALLY in
        // Expert (combined 0.9506) — NOT a replacement.
        private const float DashDecayP2 = 0.97f;
        private const float DashDecayP2ExpertExtra = 0.98f;

        // Rapid barrage (Attack 5, Expert)
        private const float BarrageSpeed = 20f;
        private const float BarrageBoostedSpeed = 26f; // 20 x 1.3, first dash after reposition
        private const int BarrageLead = 10;        // ticks of target leading
        private const int BarrageLeadBoosted = 40; // first dash after reposition
        private const int BarrageWindow = 20;      // full-speed ticks per dash
        private const int BarrageDashTime = 33;    // window + 13
        private const int BarrageDashes = 5;
        private const int BarrageBoostedDashes = 7; // vanilla: 5-8 (random); fixed 7
        private const float BarrageDecay = 0.95f;

        // Reposition (Attack 6, Expert)
        private const float RepositionSpeed = 9f;
        private const float RepositionAccel = 0.3f;
        private const float RepositionBelow = 600f; // 600 px below the target
        private const int RepositionTime = 70;

        // Rotation easing rates (vanilla num8, top of aiStyle 4), x1.5 in
        // Expert. P1 hover 0.02, P1 dash decay 0.05, P2 hover 0.05, P2
        // dash decay 0.08, barrage decay 0.15, reposition 0.05.
        private const float TurnP1Hover = 0.02f;
        private const float TurnP1DashDecay = 0.05f;
        private const float TurnP2Hover = 0.05f;
        private const float TurnP2DashDecay = 0.08f;
        private const float TurnBarrageDecay = 0.15f;
        private const float TurnReposition = 0.05f;

        // Roar (legacy sound type 15, style 0) and ForceRoar / "EoC Expert
        // Roar" (legacy sound type 36, style 0). SoundID.Roar and
        // SoundID.ForceRoar are const ints, not SoundStyles —
        // Use the path-based SoundStyle (LegacySoundStyle does not exist in tML 1.4.4).
        private static readonly SoundStyle RoarSound = new SoundStyle("Terraria/Sounds/Roar_0");
        private static readonly SoundStyle ForceRoarSound = new SoundStyle("Terraria/Sounds/Roar_2");

        // States (Projectile.ai[0])
        private const int StateP1Hover = 0;
        private const int StateP1Dash = 1;
        private const int StateTransform1 = 2; // spin up
        private const int StateTransform2 = 3; // spin down
        private const int StateP2Hover = 4;
        private const int StateP2Dash = 5;
        private const int StateBarrage = 6;
        private const int StateBarrageBoosted = 7;
        private const int StateReposition = 8;

        // --- State (instance fields; ai[0] is the synced state) ---
        private NPC target = null;
        private int retargetTimer = 0;
        private int stateTimer = 0;      // ticks in the current state
        private int dashCounter = 0;     // dashes completed in the current set
        private int servantCounter = 0;  // ticks in servant-spawn range (resets when the hover ends, like vanilla ai[3])
        private int dashSetsDone = 0;    // phase-1 dash sets completed (transform trigger)
        private int p2Rotation = 0;      // phase-2 attack rotation: 0 dash, 1 reposition, 2 dash, 3 barrage
        private float spinRate = 0f;     // transformation spin speed

        public override void SetDefaults()
        {
            // Vanilla Eye hitbox 100x110, halved for the minion (0.5 scale).
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
            // boss attack movement, so the minion catches up).
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

            // Animation frames (vanilla FindFrame, NPC 4 — see boss_frames.md).
            // Runs every tick, even with no target, so the eye animates while
            // idling. The frame cycle is 0→1→2 at 7 ticks/frame; after the
            // shell sheds (state >= Transform2) it shifts +3 to frames 3→4→5.
            UpdateFrame();

            // AMBIENT DUST (vanilla, top of aiStyle 4): 1-in-5 chance per
            // tick in every state — a type-5 mote from the eye's middle
            // band, drifting with the eye's horizontal velocity.
            if (Main.rand.Next(5) == 0)
            {
                Vector2 dustArea = new Vector2(Projectile.position.X, Projectile.position.Y + Projectile.height * 0.25f);
                int d = Dust.NewDust(dustArea, Projectile.width, (int)(Projectile.height * 0.5f), 5, Projectile.velocity.X, 2f, 0, default);
                Main.dust[d].velocity.X *= 0.5f;
                Main.dust[d].velocity.Y *= 0.1f;
            }

            // Re-scan for a target every 15 ticks (or if the current one died).
            retargetTimer++;
            if (retargetTimer >= 15 || target == null || !target.active || !target.CanBeChasedBy())
            {
                retargetTimer = 0;
                target = FindTarget(TargetRange);
            }

            if (target == null)
            {
                // No enemy: drift to the hover point near the player. The
                // state machine pauses (timers don't advance) and resumes
                // when a target appears.
                SteerToward(owner.Center + IdleOffset, 8f, 0.15f);
                EaseRotationToward(owner.Center, 0.1f);
                return;
            }

            bool expert = Main.expertMode || Main.masterMode;
            int state = (int)Projectile.ai[0];

            switch (state)
            {
                case StateP1Hover: P1Hover(expert); break;
                case StateP1Dash: P1Dash(expert); break;
                case StateTransform1: Transform(spinningUp: true); break;
                case StateTransform2: Transform(spinningUp: false); break;
                case StateP2Hover: P2Hover(expert); break;
                case StateP2Dash: P2Dash(expert); break;
                case StateBarrage: Barrage(boosted: false); break;
                case StateBarrageBoosted: Barrage(boosted: true); break;
                case StateReposition: Reposition(expert); break;
                default: SetState(StateP1Hover); break;
            }
        }

        // --- Phase 1 ---

        private void P1Hover(bool expert)
        {
            stateTimer++;

            // Hover 200 px above the target's center (vanilla), kept left
            // for anti-stacking. Speed/accel are difficulty-based
            // (5 / 0.04 Classic, 7 / 0.15 Expert).
            Vector2 hoverPoint = target.Center + new Vector2(FightHoverX, -Phase1HoverHeight);
            float speed = expert ? P1HoverSpeedExpert : P1HoverSpeed;
            float accel = expert ? P1HoverAccelExpert : P1HoverAccel;
            SteerToward(hoverPoint, speed, accel);
            EaseRotationToward(target.Center, TurnRate(TurnP1Hover, expert));

            // Servant spawn (Attack 1): the counter increments while the
            // target is within 500 px OF THE HOVER POINT (vanilla num17)
            // — in Classic the eye must also be above the target; in
            // Expert the "above" requirement is dropped. The counter
            // pauses (not reset) when out of range, but resets when the
            // hover ends (vanilla zeroes ai[3] at the transition).
            float dist = Vector2.Distance(Projectile.Center, hoverPoint);
            bool above = Projectile.position.Y + Projectile.height < target.position.Y;
            if (dist < ServantRange && (expert || above))
            {
                servantCounter++;
                int interval = expert ? ServantIntervalExpert : ServantInterval;
                if (servantCounter >= interval)
                {
                    servantCounter = 0;
                    if (SpawnServantAimed(expert))
                    {
                        // Vanilla snaps the rotation to face the target
                        // when a servant spawns (rotation = num7).
                        SnapRotationToTarget();
                        SoundEngine.PlaySound(SoundID.NPCHit1, Projectile.Center);
                    }
                }
            }

            int hoverTime = expert ? P1HoverTimeExpert : P1HoverTime;
            if (stateTimer >= hoverTime)
            {
                servantCounter = 0; // vanilla zeroes ai[3] at the transition
                SetState(StateP1Dash);
            }
        }

        private void P1Dash(bool expert)
        {
            if (stateTimer == 0)
            {
                // Launch: velocity set directly toward the target.
                // (Vanilla snaps rotation to face the target on the launch
                // tick, but the dash branch overwrites it with the
                // mouth-first angle on the very next tick, so the snap is
                // folded away here — the dash state starts with the
                // window rotation below.)
                float speed = expert ? DashSpeedP1Expert : DashSpeedP1;
                Vector2 dir = (target.Center - Projectile.Center).SafeNormalize(Vector2.UnitX);
                Projectile.velocity = dir * speed;
                // No sound: the vanilla phase-1 dash is silent.
            }
            stateTimer++;

            // Vanilla: ai[2] >= 40 decays, else rotation = atan2(vel) - 1.57
            // (mouth-first) every tick. Decay is 0.98 always, x0.985 more
            // in Expert; near-zero components are zeroed.
            if (stateTimer >= DashDecayStartP1)
            {
                Projectile.velocity *= DashDecayP1;
                if (expert)
                    Projectile.velocity *= DashDecayP1ExpertExtra;
                ZeroSmallVelocity();
                EaseRotationToward(target.Center, TurnRate(TurnP1DashDecay, expert));
            }
            else
            {
                Projectile.rotation = Projectile.velocity.ToRotation() - MathHelper.PiOver2;
            }

            int dashTime = expert ? DashTimeP1Expert : DashTimeP1;
            if (stateTimer >= dashTime)
            {
                // Vanilla snaps the rotation to face the target at dash end.
                SnapRotationToTarget();
                dashCounter++;
                stateTimer = 0;
                if (dashCounter >= DashesPerSet)
                {
                    dashCounter = 0;
                    dashSetsDone++;
                    if (dashSetsDone >= DashSetsBeforeTransform)
                    {
                        spinRate = 0f;
                        SetState(StateTransform1);
                    }
                    else
                    {
                        SetState(StateP1Hover);
                    }
                }
                else
                {
                    Projectile.netUpdate = true; // next dash in the set
                }
            }
        }

        // --- Transformation (Attack 3) ---

        private void Transform(bool spinningUp)
        {
            // Spin rate ramps 0 -> 0.5 at 0.005/tick (stage 1), then
            // 0.5 -> 0 (stage 2). Velocity decays; the spin deals no damage.
            if (spinningUp)
                spinRate = Math.Min(0.5f, spinRate + 0.005f);
            else
                spinRate = Math.Max(0f, spinRate - 0.005f);
            Projectile.rotation += spinRate;
            Projectile.velocity *= 0.98f;
            stateTimer++;

            // Expert servant spray: one servant every 20 ticks, random
            // direction, silent (only dust in vanilla). Included in all
            // modes for the minion.
            if (stateTimer % TransformSprayInterval == 0 && stateTimer > 0)
                SpawnServantRandom();

            if (stateTimer >= TransformStageTime)
            {
                stateTimer = 0;
                if (spinningUp)
                {
                    // Midpoint: gore burst + crunch + roar (vanilla).
                    // Gore types are 8, 7, 6 (x2 each), spawned from the
                    // hitbox top-left with +/-6 velocity (halved here for
                    // the 0.5-scale minion).
                    if (Projectile.owner == Main.myPlayer)
                    {
                        foreach (int goreType in new int[] { 8, 7, 6 })
                        {
                            for (int i = 0; i < 2; i++)
                            {
                                Gore.NewGore(Projectile.GetSource_FromThis(), Projectile.position,
                                    new Vector2(Main.rand.NextFloat(-3f, 3f), Main.rand.NextFloat(-3f, 3f)),
                                    goreType, 0.5f);
                            }
                        }
                    }
                    for (int i = 0; i < 20; i++)
                        Dust.NewDust(Projectile.position, Projectile.width, Projectile.height, 5,
                            Main.rand.NextFloat(-3f, 3f), Main.rand.NextFloat(-3f, 3f), 0, default, 1.2f);
                    SoundEngine.PlaySound(SoundID.NPCHit1, Projectile.Center);
                    SoundEngine.PlaySound(RoarSound, Projectile.Center);
                    SetState(StateTransform2);
                }
                else
                {
                    // Phase 2 begins (no roar here in vanilla — the roar
                    // plays at the midpoint, not at phase-2 start).
                    p2Rotation = 0;
                    SetState(StateP2Hover);
                }
            }
        }

        // --- Phase 2 ---

        private void P2Hover(bool expert)
        {
            stateTimer++;

            // Hover 120 px above the target (kept left for anti-stacking).
            // Expert: +1 speed / +0.05 accel per distance tier past 400 /
            // 600 / 800 px FROM THE HOVER POINT (vanilla num49), up to
            // speed 9 / accel 0.22.
            Vector2 hoverPoint = target.Center + new Vector2(FightHoverX, -Phase2HoverHeight);
            float speed = P2HoverSpeed;
            float accel = P2HoverAccel;
            if (expert)
            {
                float dist = Vector2.Distance(Projectile.Center, hoverPoint);
                if (dist > 400f) { speed += 1f; accel += 0.05f; }
                if (dist > 600f) { speed += 1f; accel += 0.05f; }
                if (dist > 800f) { speed += 1f; accel += 0.05f; }
            }
            SteerToward(hoverPoint, speed, accel);
            EaseRotationToward(target.Center, TurnRate(TurnP2Hover, expert));

            if (stateTimer >= P2HoverTime)
            {
                // Rotate through the phase-2 attacks: triple dash,
                // reposition + boosted barrage, triple dash, barrage.
                int next;
                switch (p2Rotation)
                {
                    case 1: next = StateReposition; break;
                    case 3: next = StateBarrage; break;
                    default: next = StateP2Dash; break;
                }
                p2Rotation = (p2Rotation + 1) % 4;
                SetState(next);
            }
        }

        private void P2Dash(bool expert)
        {
            if (stateTimer == 0)
            {
                // Launch: Expert speeds escalate across the set (x1.15 on
                // the 2nd dash, x1.3 on the 3rd); Classic is flat 6.8.
                // (Vanilla snaps rotation to face the target on the launch
                // tick; the dash branch overwrites it mouth-first on the
                // next tick, so the snap is folded away here.)
                float speed = DashSpeedP2;
                if (expert)
                {
                    if (dashCounter == 1)
                        speed *= 1.15f;
                    else if (dashCounter >= 2)
                        speed *= 1.3f;
                }
                Vector2 dir = (target.Center - Projectile.Center).SafeNormalize(Vector2.UnitX);
                Projectile.velocity = dir * speed;
                SoundEngine.PlaySound(ForceRoarSound, Projectile.Center);
            }
            stateTimer++;

            // Vanilla: ai[2] >= 40 (50 in Expert) decays, else rotation =
            // atan2(vel) - 1.57 (mouth-first) every tick. Decay is 0.97
            // always, x0.98 more in Expert; near-zero components zeroed.
            int decayStart = expert ? DashDecayStartP2Expert : DashDecayStartP2;
            if (stateTimer >= decayStart)
            {
                Projectile.velocity *= DashDecayP2;
                if (expert)
                    Projectile.velocity *= DashDecayP2ExpertExtra;
                ZeroSmallVelocity();
                EaseRotationToward(target.Center, TurnRate(TurnP2DashDecay, expert));
            }
            else
            {
                Projectile.rotation = Projectile.velocity.ToRotation() - MathHelper.PiOver2;
            }

            int dashTime = expert ? DashTimeP2Expert : DashTimeP2;
            if (stateTimer >= dashTime)
            {
                // Vanilla snaps the rotation to face the target at dash end.
                SnapRotationToTarget();
                dashCounter++;
                stateTimer = 0;
                if (dashCounter >= DashesPerSet)
                {
                    dashCounter = 0;
                    SetState(StateP2Hover);
                }
                else
                {
                    Projectile.netUpdate = true; // next dash in the set
                }
            }
        }

        private void Barrage(bool boosted)
        {
            int totalDashes = boosted ? BarrageBoostedDashes : BarrageDashes;

            if (stateTimer == 0)
            {
                // Launch with target leading, ported verbatim from the
                // ai[1] == 3 branch. The first dash after a reposition
                // leads 40 ticks at speed 26; all other barrage dashes
                // lead 10 ticks at speed 20.
                bool firstBoosted = boosted && dashCounter == 0;
                float speed = firstBoosted ? BarrageBoostedSpeed : BarrageSpeed;
                float lead = firstBoosted ? BarrageLeadBoosted : BarrageLead;

                // THE LEAD IS BACKWARDS (vanilla): the aim point is the
                // target MINUS velocity x lead — the Eye aims behind the
                // target's motion. The Y lead is quartered.
                Vector2 aim = target.Center - new Vector2(target.velocity.X * lead, target.velocity.Y * lead / 4f);
                Vector2 vel = aim - Projectile.Center;

                // +/-10% random error per axis (applied to the aim vector
                // before normalizing, like vanilla).
                vel.X *= 1f + Main.rand.NextFloat(-0.1f, 0.1f);
                vel.Y *= 1f + Main.rand.NextFloat(-0.1f, 0.1f);

                // num61: the aim distance AFTER the lead and error, BEFORE
                // normalizing — this is what the <100 px check uses.
                float aimDist = vel.Length();
                vel = vel.SafeNormalize(Vector2.UnitX) * speed;

                // +/-2 velocity jitter. Vanilla does NOT re-normalize
                // after this (only the unreachable <4%-life flag3 branch
                // re-normalizes).
                vel.X += Main.rand.NextFloat(-2f, 2f);
                vel.Y += Main.rand.NextFloat(-2f, 2f);

                // Close-range quirk (vanilla): at <100 px with a
                // mostly-horizontal aim, SWAP the components with
                // TOWARD-PLAYER signs — the dash still heads toward the
                // target, just diagonally. (Not a rotation: the old
                // (-vel.Y, vel.X) could dash AWAY from the target.)
                // At >=100 px with a mostly-horizontal aim, diagonal-ize:
                // both components become the mean magnitude with
                // TOWARD-PLAYER signs, and the result is NOT
                // re-normalized (the magnitude drops to m*sqrt(2)).
                if (Math.Abs(vel.X) > Math.Abs(vel.Y))
                {
                    float signX = Projectile.Center.X > target.Center.X ? -1f : 1f;
                    float signY = Projectile.Center.Y > target.Center.Y ? -1f : 1f;
                    if (aimDist < 100f)
                    {
                        float magX = Math.Abs(vel.X);
                        float magY = Math.Abs(vel.Y);
                        vel = new Vector2(signX * magY, signY * magX);
                    }
                    else
                    {
                        float m = (Math.Abs(vel.X) + Math.Abs(vel.Y)) * 0.5f;
                        vel = new Vector2(signX * m, signY * m);
                    }
                }

                Projectile.velocity = vel;
                SoundEngine.PlaySound(ForceRoarSound, Projectile.Center);
            }
            stateTimer++;

            // Full-speed window is 20 ticks, then x0.95/tick decay. If the
            // eye is within 200 px of the target (top-left positions, like
            // vanilla) at the end of the window, the window extends by 1
            // tick — REPEATEDLY, every tick, while it stays in range.
            // (Vanilla has no once-per-dash flag.)
            if (stateTimer == BarrageWindow &&
                Vector2.Distance(Projectile.position, target.position) < 200f)
            {
                stateTimer--;
            }

            // Vanilla: ai[2] >= 20 decays, else rotation = atan2(vel) -
            // 1.57 (mouth-first) every tick. During decay the rotation
            // eases toward the target at the barrage rate (0.15).
            if (stateTimer >= BarrageWindow)
            {
                Projectile.velocity *= BarrageDecay;
                ZeroSmallVelocity();
                bool expert = Main.expertMode || Main.masterMode;
                EaseRotationToward(target.Center, TurnRate(TurnBarrageDecay, expert));
            }
            else
            {
                Projectile.rotation = Projectile.velocity.ToRotation() - MathHelper.PiOver2;
            }

            if (stateTimer >= BarrageDashTime)
            {
                dashCounter++;
                stateTimer = 0;
                if (dashCounter >= totalDashes)
                {
                    dashCounter = 0;
                    SetState(StateP2Hover);
                }
                else
                {
                    Projectile.netUpdate = true; // next dash in the barrage
                }
            }
        }

        private void Reposition(bool expert)
        {
            // Fly to a point 600 px below the target, then barrage upward
            // (Attack 6 sets up the boosted barrage).
            stateTimer++;
            Vector2 point = target.Center + new Vector2(0f, RepositionBelow);
            SteerToward(point, RepositionSpeed, RepositionAccel);
            EaseRotationToward(target.Center, TurnRate(TurnReposition, expert));

            if (stateTimer >= RepositionTime)
                SetState(StateBarrageBoosted);
        }

        // --- Servant spawns (Attacks 1 and 3) ---

        private bool SpawnServantAimed(bool expert)
        {
            Vector2 dir = (target.Center - Projectile.Center).SafeNormalize(Vector2.UnitX);
            Vector2 spawnPos = Projectile.Center + dir * 10f;
            float speed = expert ? ServantSpeedExpert : ServantSpeed;
            return SpawnServant(spawnPos, dir * speed);
        }

        private void SpawnServantRandom()
        {
            float angle = Main.rand.NextFloat(MathHelper.TwoPi);
            Vector2 velocity = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * ServantSpraySpeed;
            // Vanilla spawns the spray servant at center + velocity x 10.
            SpawnServant(Projectile.Center + velocity * 10f, velocity);
        }

        // Servants spawn as FRIENDLY Servant of Cthulhu NPCs. Their
        // vanilla AI targets players, so they cannot attack enemies — the
        // launch toward the target is the faithful part of the port.
        // Capped so the minion cannot fill the NPC array. Vanilla also
        // spawns 10 dust (type 5) at the spawn point with the servant's
        // velocity x 0.4, for both the aimed spawn and the spray.
        private bool SpawnServant(Vector2 position, Vector2 velocity)
        {
            for (int i = 0; i < 10; i++)
                Dust.NewDust(position, 20, 20, 5, velocity.X * 0.4f, velocity.Y * 0.4f, 0, default);
            if (Projectile.owner != Main.myPlayer)
                return false;
            if (CountFriendlyNPCs(NPCID.ServantofCthulhu) >= MaxServants)
                return false;
            int idx = NPC.NewNPC(Projectile.GetSource_FromThis(), (int)position.X, (int)position.Y, NPCID.ServantofCthulhu);
            if (idx < 0 || idx >= Main.maxNPCs)
                return false;
            NPC servant = Main.npc[idx];
            servant.friendly = true;
            servant.velocity = velocity;
            servant.netUpdate = true;
            return true;
        }

        private int CountFriendlyNPCs(int type)
        {
            int count = 0;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (npc.active && npc.type == type && npc.friendly)
                    count++;
            }
            return count;
        }

        // --- Shared helpers ---

        private void SetState(int state)
        {
            Projectile.ai[0] = state;
            stateTimer = 0;
            dashCounter = 0;
            Projectile.netUpdate = true;
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

        // Vanilla rotation easing (num8): the base rate is x1.5 in Expert.
        private static float TurnRate(float baseRate, bool expert)
        {
            return expert ? baseRate * 1.5f : baseRate;
        }

        private void EaseRotationToward(Vector2 point, float maxTurn)
        {
            float targetRotation = (point - Projectile.Center).ToRotation() + MathHelper.PiOver2;
            float diff = MathHelper.WrapAngle(targetRotation - Projectile.rotation);
            Projectile.rotation += MathHelper.Clamp(diff, -maxTurn, maxTurn);
        }

        // Vanilla snaps rotation to face the target (rotation = num7) at
        // dash end and on servant spawn.
        private void SnapRotationToTarget()
        {
            if (target != null)
                Projectile.rotation = (target.Center - Projectile.Center).ToRotation() + MathHelper.PiOver2;
        }

        // Vanilla zeroes velocity components smaller than 0.1 during dash
        // decay so the eye comes to a clean stop instead of drifting.
        private void ZeroSmallVelocity()
        {
            if (Projectile.velocity.X > -0.1f && Projectile.velocity.X < 0.1f)
                Projectile.velocity.X = 0f;
            if (Projectile.velocity.Y > -0.1f && Projectile.velocity.Y < 0.1f)
                Projectile.velocity.Y = 0f;
        }

        // Vanilla FindFrame animation (NPC 4, 6 frames — see boss_frames.md):
        // frames cycle 0→1→2 at 7 ticks/frame (21-tick loop). Once the shell
        // has shed (state >= Transform2 — the midpoint gore burst), the
        // cycle shifts +3 to frames 3→4→5. Spinning is rotation only.
        private void UpdateFrame()
        {
            Projectile.frameCounter++;
            int f;
            if (Projectile.frameCounter < 7) f = 0;
            else if (Projectile.frameCounter < 14) f = 1;
            else if (Projectile.frameCounter < 21) f = 2;
            else { Projectile.frameCounter = 0; f = 0; }
            if ((int)Projectile.ai[0] >= StateTransform2) f += 3;
            Projectile.frame = f;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D texture = TextureAssets.Projectile[Projectile.type].Value;
            // Frame count is HARDCODED (6 — the Eye's texture has 6 frames;
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
            Main.EntitySpriteDraw(texture, drawPos, frame, lightColor, Projectile.rotation, origin, Projectile.scale, SpriteEffects.None, 0);
            return false;
        }
    }
}
