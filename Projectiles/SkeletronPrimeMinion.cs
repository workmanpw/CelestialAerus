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
    /// Skeletron Prime boss minion (Brigitte's Charm, 2026-11-15; FIXED
    /// 2026-11-18): the mechanical skull, rewritten as a friendly minion.
    /// The head hovers and spin-charges like the real boss (contact damage
    /// via its own hitbox), and the arms' projectile attacks run on the
    /// arms' own idle/attack-mode cycles — fired from the arms' simulated
    /// positions. Attack data from the decompiled Terraria AI (aiStyle 32
    /// head, aiStyles 33-36 arms; NPC IDs 127 head, 128 Cannon, 129 Saw,
    /// 130 Vice, 131 Laser).
    ///
    /// FIXES vs the 2026-11-15 build (verified against decompiled vanilla):
    /// - HEAD FRAME COUNT 6 -> 3. The mod targets tML 1.4.4, where NPC 127
    ///   has 3 frames (frames 3-5 are the 1.4.5 Mechdusa-only variants).
    ///   The old count sliced the texture into 51 px half-frames, drawing
    ///   only the top half of the skull.
    /// - THE FOUR LIMBS ARE NOW DRAWN. The old build drew only the head —
    ///   no Cannon, Saw, Vice, or Laser at all. The limbs are simulated
    ///   (position/velocity/rotation/frame per limb, vanilla aiStyle
    ///   33-36 steering, halved offsets for the 0.5-scale minion) and
    ///   drawn from TextureAssets.Npc[128..131] with their vanilla frame
    ///   counts (Cannon 1, Saw 2 @ 2 t/f, Vice 2 @ 8 t/f, Laser 1),
    ///   vanilla rotations (idle: atan2(anchor-pos)+1.57; Laser always
    ///   atan2(target)-1.57; Cannon attack mode atan2(target)-1.57;
    ///   Saw/Vice homing atan2(target)-1.57) and vanilla flips (Cannon
    ///   and Vice flipped, Saw and Laser unflipped — spriteDirection =
    ///   -(int)ai[0]). The limbs are VISUAL ONLY: they deal no damage
    ///   (the head hitbox + folded arm attacks cover damage).
    /// - BONE ARMS ADDED (Main.DrawNPC, aiStyle 33-36): each limb draws
    ///   its two-segment BoneArm2 chain to the head, vanilla algorithm
    ///   (start at limb + (width/2 - 5*ai[0], 20), k=0 aims at the head
    ///   center offset (-200*ai[0], +130) stepping 92 px then advancing
    ///   a half-step, k=1 aims at (-50*ai[0], +80) stepping 60 px;
    ///   rotation atan2-1.57; joint dust type 6) with all offsets/steps
    ///   halved for the 0.5-scale minion.
    /// - GLOW OVERLAYS ADDED (Main.DrawNPC): BoneEyes on the head and
    ///   BoneLaser on the Laser limb, additive (200,200,200,0), same
    ///   frame/rotation as the sprite.
    /// - MECH AFTERIMAGES ADDED (Main.DrawNPC, type 127): 5 ghosts at
    ///   oldPos[9,7,5,3,1], alpha (10-n)/20, current frame/rotation.
    ///   (Limb afterimages are skipped — the limbs are virtual, with no
    ///   oldPos history of their own.)
    /// - HOVER now uses vanilla's dead-band steering (aiStyle 32): the
    ///   head drifts freely while its top is 200-500 px above the
    ///   target's TOP and its center is within +/-100 px of the target
    ///   center, with a restoring force only outside the band. The old
    ///   build point-steered to 350 px above the target CENTER, so the
    ///   head never rested.
    /// - ARM MODE TIMERS now count only while the head hovers in idle
    ///   mode (vanilla: ai[3]++ is inside the head-hovering branch);
    ///   attack-mode timers count always. The old build counted both
    ///   continuously, so attack modes came ~40% too soon.
    /// - SPIN FIRE-RATE BONUS (laser 4x, bomb 3x) now applies in IDLE
    ///   mode only (vanilla: the +2/+3 localAI[0] bonus is inside the
    ///   idle branch). The old build applied it in attack mode too.
    /// - IDLE BOMBS now fire AWAY from the anchor point (vanilla: the
    ///   aim vector to the anchor, negated), not straight up. Fired
    ///   from the Cannon limb; Death Lasers fired from the Laser limb.
    /// - PROJECTILE SPAWN-AHEAD now uses the final velocity (center +
    ///   velocity x 8 for lasers, x 4 for bombs), not the unit
    ///   direction — the old spawn point was inside the head.
    /// - DRAW OFFSET: vanilla draws the sprite center 4 px below the NPC
    ///   center (Main.DrawNPC literal +4); applied scaled by 0.5.
    /// - netImportant = true (the minion's state must sync).
    ///
    /// BOMB SUBSTITUTION: the vanilla BombSkeletronPrime (102) hardcodes
    /// its explosion damage to 40 flat (Projectile.cs sets damage = 40 on
    /// detonation, ignoring the spawn damage), which would make the cannon
    /// attack useless next to the charm's flat 6000. The minion instead
    /// fires PrimeBombFriendly (below), a custom bomb that replicates the
    /// vanilla bomb — gravity arc, fuse hiss, detonation on landing /
    /// timeout, 128x128 explosion — but deals the charm's 6000.
    /// Bomb fixes: terminal velocity 16 -> 10 (vanilla caps at 10);
    /// frame cycling added (2 frames at 4 ticks/frame — the old bomb
    /// always drew frame 0); platform kill added (vanilla kills the bomb
    /// over a platform or tile 380); the rotation spin and ambient smoke
    /// are removed (vanilla type 102 has neither).
    ///
    /// Kept adaptations (not bugs):
    /// - Daytime enrage (9999 damage) and despawn behavior are dropped.
    /// - The Saw/Vice melee lunges are dropped — the head's spin charge
    ///   covers the melee role. (The saw/vice still home on the target
    ///   while the head spins, as in vanilla.)
    /// - The bomb detonates immediately on tile contact (vanilla rests
    ///   it 3 ticks first — a 3-tick difference, kept as the old build's
    ///   documented choice).
    /// - Bomb gravity 0.2 is kept: it sits in a poorly-decompiled region
    ///   of AI_016_Bombs and could not be verified from the decompile,
    ///   but it is the standard Terraria bomb gravity.
    /// - All damage is flat 6000, typeless (DamageClass.Generic), per the
    ///   charm's design — the vanilla damage values are informational.
    /// </summary>
    public class SkeletronPrimeMinion : ModProjectile
    {
        // --- Spec constants (vanilla aiStyle 32 head + aiStyles 35/36 arms) ---
        private const float TargetRange = 1000f;

        // Head hover (ai[1]==0): vanilla dead-band steering — the head
        // drifts freely while its TOP is 200-500 px above the target's TOP
        // and its center is within +/-100 px of the target center; the
        // restoring force only applies outside the band. (NOT a point
        // steer to 350 px above the target center, as the old build did.)
        private const float HoverBandNear = 200f;  // px above target TOP
        private const float HoverBandFar = 500f;   // px above target TOP
        private const float HoverBandX = 100f;     // px either side of target center
        private const float HoverAccelX = 0.1f;
        private const float HoverAccelXExpert = 0.07f;
        private const float HoverMaxX = 8f;
        private const float HoverMaxXExpert = 9.5f;
        private const float HoverAccelY = 0.1f;
        private const float HoverAccelYExpert = 0.03f; // Expert Y accel is LOWER
        private const float HoverMaxY = 2f;
        private const float HoverMaxYExpert = 4f;
        private const int HoverDuration = 600;     // 10 s, then spin

        // Head spin charge (ai[1]==1): homing chase, velocity set directly
        // at the target every tick.
        private const float SpinSpeed = 2f;
        private const float SpinSpeedExpert = 6f;
        private const float SpinRotationRate = 0.3f; // rad/tick
        private const int SpinDuration = 400;      // ~6.7 s, then hover

        // Prime Laser arm (aiStyle 36): Death Laser.
        private const int LaserIdleThreshold = 200;   // ~3.35 s between shots
        private const int LaserAttackThreshold = 80;  // ~1.35 s between shots
        private const float LaserSpeedIdle = 8f;
        private const float LaserSpeedAttack = 10f;
        private const float LaserJitter = 2f;         // +/- per axis
        private const float LaserSpawnAhead = 8f;     // x velocity, not x direction
        private const int LaserIdleDuration = 800;    // ~13 s idle, then attack mode
        private const int LaserAttackDuration = 200;  // ~3.3 s attack mode
        private const int LaserSpinFireRate = 4;      // fire timer runs 4x while head spins — IDLE mode only

        // Prime Cannon arm (aiStyle 35): bomb lob.
        private const int BombIdleThreshold = 140;    // ~2.35 s between bombs
        private const int BombAttackThreshold = 40;   // ~0.7 s between bombs
        private const float BombSpeedIdle = 12f;
        private const float BombSpeedAttack = 10f;
        private const float BombJitter = 0.4f;        // +/- per axis
        private const float BombSpawnAhead = 4f;      // x velocity, not x direction
        private const int BombIdleDuration = 1100;    // ~18 s idle, then attack mode
        private const int BombAttackDuration = 300;   // 5 s attack mode
        private const int BombSpinFireRate = 3;       // fire timer runs 3x while head spins — IDLE mode only

        private const int MinionDamage = 6000;     // flat, typeless (charm design)

        // --- Limb constants (vanilla aiStyles 33-36, halved for 0.5 scale) ---
        // Limb indices (vanilla spawn order).
        private const int LimbCannon = 0; // NPC 128, ai[0] = -1
        private const int LimbSaw = 1;    // NPC 129, ai[0] = +1
        private const int LimbVice = 2;   // NPC 130, ai[0] = -1
        private const int LimbLaser = 3;  // NPC 131, ai[0] = +1
        private const int LimbCount = 4;
        private const float LimbHalf = 13f; // vanilla arm hitbox 52x52, halved -> half-size 13

        // The anchor each limb orbits (vanilla: head center X - 200*ai[0],
        // head top + 230; halved): used for idle rotations, the idle bomb
        // direction, and the tether check.
        private const float AnchorOffsetX = 100f;
        private const float AnchorOffsetY = 115f;

        // Tether (vanilla ai[2]==99): the limb flies back to the head when
        // it strays too far from its anchor (800 px full scale, halved);
        // the tether releases once it is back within 200 px (hysteresis,
        // vanilla: enter >800, exit <400).
        private const float TetherEnterDistance = 400f;
        private const float TetherExitDistance = 200f;

        // Bone-arm segments (Main.DrawNPC, aiStyle 33-36): vanilla steps
        // 92 px then 60 px; halved for the 0.5-scale minion.
        private const float BoneStepFirst = 46f;
        private const float BoneStepSecond = 30f;

        // Roar (legacy sound type 15, style 0). SoundID.Roar is a const int,
        // not a SoundStyle — use the path-based SoundStyle instead.
        private static readonly SoundStyle RoarSound = new SoundStyle("Terraria/Sounds/Roar_0");

        // Hover offset near the player (no target): each boss minion gets a
        // distinct offset so the entourage doesn't stack on one point.
        private static readonly Vector2 PlayerHoverOffset = new Vector2(0f, -380f);

        // Vanilla limb data, indexed by limb: NPC texture ID, frame count
        // (1.4.4), frame timing (ticks/frame; 0 = static), ai[0] sign,
        // and whether the sprite is flipped (spriteDirection = -(int)ai[0];
        // spriteDirection == 1 draws flipped).
        private static readonly int[] LimbNpcId = { 128, 129, 130, 131 };
        private static readonly int[] LimbFrameCount = { 1, 2, 2, 1 };
        private static readonly int[] LimbFrameTime = { 0, 2, 8, 0 };
        private static readonly float[] LimbAi0 = { -1f, 1f, -1f, 1f };
        private static readonly SpriteEffects[] LimbFlip = { SpriteEffects.FlipHorizontally, SpriteEffects.None, SpriteEffects.FlipHorizontally, SpriteEffects.None };

        // --- State ---
        // Projectile.ai[1] = head mode: 0 = hover, 1 = spin charge
        // (matching the vanilla ai[1] modes).
        private int modeTimer = 0;
        private bool spinRoared = false;
        private int laserMode = 0;       // 0 = idle, 1 = attack
        private int laserModeTimer = 0;
        private int laserFireTimer = 0;
        private int bombMode = 0;        // 0 = idle, 1 = attack
        private int bombModeTimer = 0;
        private int bombFireTimer = 0;
        private int retargetTimer = 0;
        private NPC target = null;

        // Simulated limb state (top-left position, velocity, rotation,
        // frame). The limbs are visual only — no damage, no hitbox.
        private Vector2[] limbPos = new Vector2[LimbCount];
        private Vector2[] limbVel = new Vector2[LimbCount];
        private float[] limbRot = new float[LimbCount];
        private int[] limbFrame = new int[LimbCount];
        private int[] limbFrameCounter = new int[LimbCount];
        private bool[] limbTethered = new bool[LimbCount];
        private bool limbsInitialized = false;

        public override string Texture => "Terraria/Images/NPC_127";

        public override void SetStaticDefaults()
        {
            // Vanilla draws 5 afterimages for Skeletron Prime (Main.DrawNPC,
            // type 127) from oldPos[9,7,5,3,1] — the trail cache must be
            // long enough to hold them.
            ProjectileID.Sets.TrailCacheLength[Type] = 10;
        }

        public override void SetDefaults()
        {
            // Vanilla Prime head hitbox 80x102, halved for the minion (0.5 scale).
            Projectile.width = 40;
            Projectile.height = 51;
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

            Projectile.light = 0.3f;
        }

        public override void AI()
        {
            Player owner = Main.player[Projectile.owner];
            if (owner == null || !owner.active)
            {
                Projectile.velocity *= 0.95f;
                return;
            }

            if (!limbsInitialized)
                InitializeLimbs();

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
                // Clear the afterimage trail and pull the limbs back to the
                // head, or both streak across the screen after the jump.
                for (int i = 0; i < Projectile.oldPos.Length; i++)
                    Projectile.oldPos[i] = Projectile.position;
                ResetLimbsToHead();
                for (int i = 0; i < 10; i++)
                    Dust.NewDust(Projectile.position, Projectile.width, Projectile.height, DustID.GoldFlame);
            }
            else if (distToOwner > 400f)
            {
                Vector2 toOwner = (owner.Center - Projectile.Center).SafeNormalize(Vector2.Zero);
                Projectile.velocity = Vector2.Lerp(Projectile.velocity, toOwner * 20f, 0.2f);
            }

            // Animation frames (vanilla FindFrame, NPC 127). Runs every
            // tick, even with no target. Hover: frames 0↔1 at 12
            // ticks/frame; spin charge: static frame 2.
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
                // No enemy: drift to the hover point near the player, limbs
                // settling to their idle positions around the head.
                SteerToward(owner.Center + PlayerHoverOffset, 8f, 0.15f);
                Projectile.rotation = Projectile.velocity.X / 15f;
                UpdateLimbs(followingOwner: true);
                return;
            }

            bool expert = Main.expertMode || Main.masterMode;
            bool spinning = (int)Projectile.ai[1] == 1;
            modeTimer++;

            if (!spinning)
            {
                // --- Hover: vanilla dead-band steering (aiStyle 32). ---
                HoverAbove(target, expert);

                if (modeTimer >= HoverDuration)
                {
                    Projectile.ai[1] = 1f; // spin charge
                    modeTimer = 0;
                    spinRoared = false;
                    Projectile.netUpdate = true;
                }
            }
            else
            {
                // --- Spin charge: homing chase, velocity set directly at
                // the target every tick. ---
                float spinSpeed = expert ? SpinSpeedExpert : SpinSpeed;
                if (expert)
                {
                    // Expert: speed scales with distance (x1.05 past 150 px,
                    // then x1.1 at each 50 px threshold up to >600 px).
                    float dist = Vector2.Distance(Projectile.Center, target.Center);
                    if (dist > 150f)
                        spinSpeed *= 1.05f;
                    for (float t = 200f; t <= 600f; t += 50f)
                    {
                        if (dist > t)
                            spinSpeed *= 1.1f;
                    }
                }
                Vector2 dir = (target.Center - Projectile.Center).SafeNormalize(Vector2.UnitX);
                Projectile.velocity = dir * spinSpeed;
                // Projectile.direction is not maintained for ModProjectiles —
                // set it from the target's side so the spin turns correctly.
                Projectile.direction = target.Center.X >= Projectile.Center.X ? 1 : -1;
                Projectile.rotation += Projectile.direction * SpinRotationRate;

                // Roar 2 ticks into the spin (vanilla: ai[2] is incremented
                // before the == 2 check, so the roar lands on the 2nd tick).
                if (modeTimer == 2 && !spinRoared)
                {
                    spinRoared = true;
                    SoundEngine.PlaySound(RoarSound, Projectile.Center);
                }

                if (modeTimer >= SpinDuration)
                {
                    Projectile.ai[1] = 0f; // back to hover
                    modeTimer = 0;
                    Projectile.netUpdate = true;
                }
            }

            // Limbs follow the head's state (idle positions while hovering,
            // homing/tucked while spinning), then the arm attacks fire from
            // the limbs' positions.
            UpdateLimbs(followingOwner: false);
            UpdateLaserArm(spinning);
            UpdateBombArm(spinning);
        }

        // Vanilla hover steering (aiStyle 32), verbatim: a dead band, not a
        // point steer. While the head's TOP is 200-500 px above the target's
        // TOP and its center is within +/-100 px of the target center, no
        // force applies at all — the head drifts. Outside the band, the
        // head accelerates back toward it (with 0.98 damping when moving
        // the wrong way). Note the Y band is measured from the target's TOP
        // (position.Y), not its center.
        private void HoverAbove(NPC target, bool expert)
        {
            float accelY = expert ? HoverAccelYExpert : HoverAccelY;
            float maxY = expert ? HoverMaxYExpert : HoverMaxY;
            float accelX = expert ? HoverAccelXExpert : HoverAccelX;
            float maxX = expert ? HoverMaxXExpert : HoverMaxX;
            Projectile.rotation = Projectile.velocity.X / 15f;

            if (Projectile.position.Y > target.position.Y - HoverBandNear)
            {
                // Too low: push up.
                if (Projectile.velocity.Y > 0f)
                    Projectile.velocity.Y *= 0.98f;
                Projectile.velocity.Y -= accelY;
                if (Projectile.velocity.Y > maxY)
                    Projectile.velocity.Y = maxY;
            }
            else if (Projectile.position.Y < target.position.Y - HoverBandFar)
            {
                // Too high: push down.
                if (Projectile.velocity.Y < 0f)
                    Projectile.velocity.Y *= 0.98f;
                Projectile.velocity.Y += accelY;
                if (Projectile.velocity.Y < -maxY)
                    Projectile.velocity.Y = -maxY;
            }

            if (Projectile.Center.X > target.Center.X + HoverBandX)
            {
                // Too far right: push left.
                if (Projectile.velocity.X > 0f)
                    Projectile.velocity.X *= 0.98f;
                Projectile.velocity.X -= accelX;
                if (Projectile.velocity.X > maxX)
                    Projectile.velocity.X = maxX;
            }
            if (Projectile.Center.X < target.Center.X - HoverBandX)
            {
                // Too far left: push right.
                if (Projectile.velocity.X < 0f)
                    Projectile.velocity.X *= 0.98f;
                Projectile.velocity.X += accelX;
                if (Projectile.velocity.X < -maxX)
                    Projectile.velocity.X = -maxX;
            }
        }

        // --- Limb simulation (vanilla aiStyles 33-36, halved offsets) ---
        // In vanilla the four arms are separate NPCs tethered to the head.
        // Here they are simulated as position/velocity/rotation/frame state
        // on the head minion, steering with the vanilla per-limb numbers.
        // They are VISUAL ONLY — the head hitbox and the folded arm
        // attacks do the damage.

        private void InitializeLimbs()
        {
            // Spawn at the head's center; they drift out to their anchor
            // positions over the first few ticks.
            for (int i = 0; i < LimbCount; i++)
            {
                limbPos[i] = Projectile.Center - new Vector2(LimbHalf, LimbHalf);
                limbVel[i] = Vector2.Zero;
                limbRot[i] = 0f;
                limbFrame[i] = 0;
                limbFrameCounter[i] = 0;
                limbTethered[i] = false;
            }
            limbsInitialized = true;
        }

        private void ResetLimbsToHead()
        {
            for (int i = 0; i < LimbCount; i++)
            {
                limbPos[i] = Projectile.Center - new Vector2(LimbHalf, LimbHalf);
                limbVel[i] = Vector2.Zero;
                limbTethered[i] = false;
            }
        }

        // The anchor point a limb orbits (vanilla: head center X - 200*ai[0],
        // head top + 230; halved). Cannon/Vice (ai[0] = -1) anchor right of
        // the head, Saw/Laser (ai[0] = +1) anchor left.
        private Vector2 LimbAnchor(int limb)
        {
            return new Vector2(
                Projectile.Center.X - AnchorOffsetX * LimbAi0[limb],
                Projectile.position.Y + AnchorOffsetY);
        }

        private Vector2 LimbCenter(int limb)
        {
            return limbPos[limb] + new Vector2(LimbHalf, LimbHalf);
        }

        private void UpdateLimbs(bool followingOwner)
        {
            bool spinning = (int)Projectile.ai[1] == 1;
            for (int i = 0; i < LimbCount; i++)
            {
                Vector2 anchor = LimbAnchor(i);
                float anchorDist = Vector2.Distance(LimbCenter(i), anchor);

                // Tether (vanilla ai[2]==99, with hysteresis): stray too far
                // from the anchor and the limb flies back to the head.
                if (!limbTethered[i] && anchorDist > TetherEnterDistance)
                    limbTethered[i] = true;
                else if (limbTethered[i] && anchorDist < TetherExitDistance)
                    limbTethered[i] = false;

                if (limbTethered[i])
                {
                    // Fly back to the head: Y steers to the head's top,
                    // X steers to the head's center.
                    SteerAxisToward(limbPos[i].Y, ref limbVel[i].Y, Projectile.position.Y, 0.1f, 8f, 0.96f);
                    SteerAxisToward(LimbCenter(i).X, ref limbVel[i].X, Projectile.Center.X, 0.5f, 12f, 0.96f);
                }
                else if (followingOwner || !spinning)
                {
                    UpdateLimbHover(i, followingOwner);
                }
                else
                {
                    UpdateLimbSpin(i);
                }

                limbPos[i] += limbVel[i];
                UpdateLimbRotation(i, followingOwner);
                UpdateLimbFrame(i);
            }
        }

        // Limb idle positions while the head hovers (vanilla aiStyles
        // 33-36 idle branches; all offsets halved). Cannon/Laser also
        // reposition in attack mode (vanilla: cannon above the target,
        // laser 350 px left of the target — halved here).
        private void UpdateLimbHover(int limb, bool followingOwner)
        {
            Vector2 headCenter = Projectile.Center;
            float headTop = Projectile.position.Y;
            switch (limb)
            {
                case LimbCannon:
                    if (!followingOwner && bombMode == 1 && target != null)
                    {
                        // Attack mode: steer toward (head center X, target
                        // center Y - 80) at speed 6, velocity-matched
                        // (accel X 0.04 / Y 0.08, damping 0.9).
                        Vector2 point = new Vector2(headCenter.X, target.Center.Y - 40f);
                        SteerVelocityToward(limb, point, 6f, 0.04f, 0.08f, 0.9f);
                    }
                    else
                    {
                        // Idle: Y to head top - 150 (accel 0.04, max 3);
                        // X dead band [head center + 160, + 200]
                        // (accel 0.2, max 8).
                        SteerAxisToward(limbPos[limb].Y, ref limbVel[limb].Y, headTop - 75f, 0.04f, 3f, 0.96f);
                        SteerAxisBand(LimbCenter(limb).X, ref limbVel[limb].X, headCenter.X + 80f, headCenter.X + 100f, 0.2f, 8f, 0.96f);
                    }
                    break;
                case LimbLaser:
                    if (!followingOwner && laserMode == 1 && target != null)
                    {
                        // Attack mode: steer toward (target center X - 350,
                        // target center Y - 20) at speed 7, velocity-matched
                        // (accel X 0.1 / Y 0.03, damping 0.9).
                        Vector2 point = new Vector2(target.Center.X - 175f, target.Center.Y - 10f);
                        SteerVelocityToward(limb, point, 7f, 0.1f, 0.03f, 0.9f);
                    }
                    else
                    {
                        // Idle: Y to head top - 100 (accel 0.1, max 3);
                        // X to head center - 180 (accel 0.14, max 8).
                        SteerAxisToward(limbPos[limb].Y, ref limbVel[limb].Y, headTop - 50f, 0.1f, 3f, 0.96f);
                        SteerAxisToward(LimbCenter(limb).X, ref limbVel[limb].X, headCenter.X - 90f, 0.14f, 8f, 0.96f);
                    }
                    break;
                case LimbSaw:
                    // Idle: Y band [head top + 260, + 320] (accel 0.04,
                    // max 3); X band [head center - 250, head center]
                    // (accel 0.3, max 12).
                    SteerAxisBand(limbPos[limb].Y, ref limbVel[limb].Y, headTop + 130f, headTop + 160f, 0.04f, 3f, 0.96f);
                    SteerAxisBand(LimbCenter(limb).X, ref limbVel[limb].X, headCenter.X - 125f, headCenter.X, 0.3f, 12f, 0.96f);
                    break;
                case LimbVice:
                    // Idle: Y band [head top + 230, + 300] (accel 0.1,
                    // max 3); X band [head center, head center + 250]
                    // (accel 0.3, max 9, damping 0.94).
                    SteerAxisBand(limbPos[limb].Y, ref limbVel[limb].Y, headTop + 115f, headTop + 150f, 0.1f, 3f, 0.96f);
                    SteerAxisBand(LimbCenter(limb).X, ref limbVel[limb].X, headCenter.X, headCenter.X + 125f, 0.3f, 9f, 0.94f);
                    break;
            }
        }

        // Limb behavior while the head spins (vanilla aiStyles 33-36 spin
        // branches): Cannon/Laser tuck near the head; Saw/Vice home on the
        // target. (The Saw/Vice 22-speed lunge cycles are dropped per the
        // charm design — the head's spin charge covers the melee role.)
        private void UpdateLimbSpin(int limb)
        {
            Vector2 headCenter = Projectile.Center;
            float headTop = Projectile.position.Y;
            switch (limb)
            {
                case LimbCannon:
                    // Tuck: Y to head top - 100 (accel 0.07, max 6); X to
                    // head center + 120 (accel 0.1, max 8).
                    SteerAxisToward(limbPos[limb].Y, ref limbVel[limb].Y, headTop - 50f, 0.07f, 6f, 0.96f);
                    SteerAxisToward(LimbCenter(limb).X, ref limbVel[limb].X, headCenter.X + 60f, 0.1f, 8f, 0.96f);
                    break;
                case LimbLaser:
                    // Tuck: Y to head top - 100 (accel 0.07, max 6); X to
                    // head center - 120 (accel 0.1, max 8).
                    SteerAxisToward(limbPos[limb].Y, ref limbVel[limb].Y, headTop - 50f, 0.07f, 6f, 0.96f);
                    SteerAxisToward(LimbCenter(limb).X, ref limbVel[limb].X, headCenter.X - 60f, 0.1f, 8f, 0.96f);
                    break;
                case LimbSaw:
                    // Home on the target at speed 7 (accel 0.05/axis,
                    // damping 0.97).
                    if (target != null)
                        SteerVelocityToward(limb, target.Center, 7f, 0.05f, 0.05f, 0.97f);
                    break;
                case LimbVice:
                    // Home on the target at speed 12: vanilla snaps the
                    // velocity straight to the desired vector when nearly
                    // stopped (|vx|+|vy| < 2), otherwise damps x0.97 —
                    // the vice lunges, coasts, and re-lunges.
                    if (target != null)
                    {
                        Vector2 desired = (target.Center - LimbCenter(limb)).SafeNormalize(Vector2.Zero) * 12f;
                        if (Math.Abs(limbVel[limb].X) + Math.Abs(limbVel[limb].Y) < 2f)
                            limbVel[limb] = desired;
                        else
                            limbVel[limb] *= 0.97f;
                    }
                    break;
            }
        }

        // Limb rotations (vanilla aiStyles 33-36): the business end of the
        // Saw/Vice/Cannon textures is at the BOTTOM, so idle limbs aim
        // their top at the anchor (+1.57); attacking limbs aim at the
        // target (-1.57). The Laser ALWAYS aims at the target.
        private void UpdateLimbRotation(int limb, bool followingOwner)
        {
            Vector2 center = LimbCenter(limb);
            Vector2 anchor = LimbAnchor(limb);
            bool spinning = (int)Projectile.ai[1] == 1;

            if (limb == LimbLaser)
            {
                if (target != null && !followingOwner)
                    limbRot[limb] = (float)Math.Atan2(target.Center.Y - center.Y, target.Center.X - center.X) - 1.57f;
                else
                    limbRot[limb] = (float)Math.Atan2(anchor.Y - center.Y, anchor.X - center.X) + 1.57f;
            }
            else if (limb == LimbCannon && bombMode == 1 && target != null && !followingOwner)
            {
                // Attack mode: aim at the target.
                limbRot[limb] = (float)Math.Atan2(target.Center.Y - center.Y, target.Center.X - center.X) - 1.57f;
            }
            else if ((limb == LimbSaw || limb == LimbVice) && spinning && target != null && !followingOwner)
            {
                // Homing while the head spins: aim at the target.
                limbRot[limb] = (float)Math.Atan2(target.Center.Y - center.Y, target.Center.X - center.X) - 1.57f;
            }
            else
            {
                // Idle: aim the top of the texture at the anchor.
                limbRot[limb] = (float)Math.Atan2(anchor.Y - center.Y, anchor.X - center.X) + 1.57f;
            }
        }

        // Limb frame cycles (vanilla FindFrame): Saw cycles its 2 frames at
        // 2 ticks/frame, Vice at 8 ticks/frame; Cannon and Laser are static.
        private void UpdateLimbFrame(int limb)
        {
            int frameTime = LimbFrameTime[limb];
            if (frameTime <= 0)
            {
                limbFrame[limb] = 0;
                return;
            }
            limbFrameCounter[limb]++;
            if (limbFrameCounter[limb] >= frameTime)
            {
                limbFrameCounter[limb] = 0;
                limbFrame[limb]++;
                if (limbFrame[limb] >= LimbFrameCount[limb])
                    limbFrame[limb] = 0;
            }
        }

        // Vanilla's per-axis limb steering: accelerate toward the target
        // value, damping when moving the wrong way, capped at max speed.
        private static void SteerAxisToward(float pos, ref float vel, float targetValue, float accel, float max, float damp)
        {
            if (pos > targetValue)
            {
                if (vel > 0f)
                    vel *= damp;
                vel -= accel;
                if (vel > max)
                    vel = max;
            }
            else if (pos < targetValue)
            {
                if (vel < 0f)
                    vel *= damp;
                vel += accel;
                if (vel < -max)
                    vel = -max;
            }
        }

        // Same, but with a dead band: no force while pos is inside [low, high].
        private static void SteerAxisBand(float pos, ref float vel, float low, float high, float accel, float max, float damp)
        {
            if (pos > high)
            {
                if (vel > 0f)
                    vel *= damp;
                vel -= accel;
                if (vel > max)
                    vel = max;
            }
            else if (pos < low)
            {
                if (vel < 0f)
                    vel *= damp;
                vel += accel;
                if (vel < -max)
                    vel = -max;
            }
        }

        // Vanilla's velocity-matching steer (attack-mode repositioning and
        // Saw homing): accelerate the limb's velocity toward the velocity
        // that would carry it to the point at the given speed.
        private void SteerVelocityToward(int limb, Vector2 point, float speed, float accelX, float accelY, float damp)
        {
            Vector2 desired = (point - LimbCenter(limb)).SafeNormalize(Vector2.Zero) * speed;
            if (limbVel[limb].X > desired.X)
            {
                if (limbVel[limb].X > 0f)
                    limbVel[limb].X *= damp;
                limbVel[limb].X -= accelX;
            }
            if (limbVel[limb].X < desired.X)
            {
                if (limbVel[limb].X < 0f)
                    limbVel[limb].X *= damp;
                limbVel[limb].X += accelX;
            }
            if (limbVel[limb].Y > desired.Y)
            {
                if (limbVel[limb].Y > 0f)
                    limbVel[limb].Y *= damp;
                limbVel[limb].Y -= accelY;
            }
            if (limbVel[limb].Y < desired.Y)
            {
                if (limbVel[limb].Y < 0f)
                    limbVel[limb].Y *= damp;
                limbVel[limb].Y += accelY;
            }
        }

        // --- Arm attacks (aiStyles 35/36), fired from the limbs. ---
        // Vanilla timing rules, both fixed here:
        // (1) the IDLE-mode timer counts only while the head hovers
        // (ai[3]++ is inside the head-hovering branch); the attack-mode
        // timer counts always. (2) the spin fire-rate bonus (+2 bomb /
        // +3 laser localAI[0] per tick) is inside the IDLE branch only —
        // attack mode always fires at 1x.
        private void UpdateLaserArm(bool spinning)
        {
            if (laserMode == 0) // idle mode
            {
                if (!spinning)
                {
                    laserModeTimer++;
                    if (laserModeTimer >= LaserIdleDuration)
                    {
                        laserMode = 1;
                        laserModeTimer = 0;
                        laserFireTimer = 0;
                    }
                }
                laserFireTimer += spinning ? LaserSpinFireRate : 1;
                if (laserFireTimer > LaserIdleThreshold)
                {
                    laserFireTimer = 0;
                    FireDeathLaser(LaserSpeedIdle);
                }
            }
            else // attack mode
            {
                laserModeTimer++;
                if (laserModeTimer >= LaserAttackDuration)
                {
                    laserMode = 0;
                    laserModeTimer = 0;
                    laserFireTimer = 0;
                }
                laserFireTimer += 1;
                if (laserFireTimer > LaserAttackThreshold)
                {
                    laserFireTimer = 0;
                    FireDeathLaser(LaserSpeedAttack);
                }
            }
        }

        private void UpdateBombArm(bool spinning)
        {
            if (bombMode == 0) // idle mode: lob away from the anchor
            {
                if (!spinning)
                {
                    bombModeTimer++;
                    if (bombModeTimer >= BombIdleDuration)
                    {
                        bombMode = 1;
                        bombModeTimer = 0;
                        bombFireTimer = 0;
                    }
                }
                bombFireTimer += spinning ? BombSpinFireRate : 1;
                if (bombFireTimer > BombIdleThreshold)
                {
                    bombFireTimer = 0;
                    // Vanilla: the bomb is fired in the direction AWAY from
                    // the anchor point (the aim vector to the anchor,
                    // negated) at speed 12 — generally upward, arcing back
                    // down with gravity. NOT straight up, as the old build
                    // did: the direction depends on where the cannon sits
                    // relative to its anchor.
                    Vector2 cannonCenter = LimbCenter(LimbCannon);
                    Vector2 anchor = LimbAnchor(LimbCannon);
                    Vector2 velocity = (cannonCenter - anchor).SafeNormalize(Vector2.UnitY) * BombSpeedIdle;
                    velocity.X += Main.rand.NextFloat(-BombJitter, BombJitter);
                    velocity.Y += Main.rand.NextFloat(-BombJitter, BombJitter);
                    Vector2 spawnPos = cannonCenter + velocity * BombSpawnAhead;
                    FireBomb(spawnPos, velocity);
                }
            }
            else // attack mode: aimed directly at the target
            {
                bombModeTimer++;
                if (bombModeTimer >= BombAttackDuration)
                {
                    bombMode = 0;
                    bombModeTimer = 0;
                    bombFireTimer = 0;
                }
                bombFireTimer += 1;
                if (bombFireTimer > BombAttackThreshold)
                {
                    bombFireTimer = 0;
                    Vector2 cannonCenter = LimbCenter(LimbCannon);
                    Vector2 dir = (target.Center - cannonCenter).SafeNormalize(Vector2.UnitX);
                    Vector2 velocity = dir * BombSpeedAttack;
                    velocity.X += Main.rand.NextFloat(-BombJitter, BombJitter);
                    velocity.Y += Main.rand.NextFloat(-BombJitter, BombJitter);
                    Vector2 spawnPos = cannonCenter + velocity * BombSpawnAhead;
                    FireBomb(spawnPos, velocity);
                }
            }
        }

        private void FireDeathLaser(float speed)
        {
            // Fired from the Laser limb (vanilla: the laser arm's own
            // center), spawn-ahead = final velocity x 8.
            Vector2 laserCenter = LimbCenter(LimbLaser);
            Vector2 dir = (target.Center - laserCenter).SafeNormalize(Vector2.UnitX);
            Vector2 velocity = dir * speed;
            velocity.X += Main.rand.NextFloat(-LaserJitter, LaserJitter);
            velocity.Y += Main.rand.NextFloat(-LaserJitter, LaserJitter);
            Vector2 spawnPos = laserCenter + velocity * LaserSpawnAhead;
            FireFriendly(spawnPos, velocity, ProjectileID.DeathLaser);
            // SoundID.Item33 comes free from the projectile's own AI.
        }

        private void FireBomb(Vector2 position, Vector2 velocity)
        {
            if (Projectile.owner != Main.myPlayer)
                return;
            // Custom bomb (see PrimeBombFriendly below): the vanilla
            // BombSkeletronPrime hardcodes its explosion to 40 damage.
            Projectile.NewProjectile(Projectile.GetSource_FromThis(), position, velocity, ModContent.ProjectileType<PrimeBombFriendly>(), MinionDamage, 0f, Projectile.owner);
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

        // Vanilla FindFrame animation (NPC 127, 3 frames in 1.4.4 — the
        // Mechdusa frames 3-5 are 1.4.5-only and never apply to the minion):
        // while hovering (ai[1] == 0) frames cycle 0↔1 at 12 ticks/frame;
        // while spin-charging (ai[1] == 1) the frame sits static on 2. The
        // spin itself is rotation-driven, not frame-driven.
        private void UpdateFrame()
        {
            bool spinning = (int)Projectile.ai[1] == 1;
            if (!spinning)
            {
                Projectile.frameCounter++;
                if (Projectile.frameCounter >= 12)
                {
                    Projectile.frameCounter = 0;
                    int f = Projectile.frame + 1;
                    if (f >= 2) f = 0;
                    Projectile.frame = f;
                }
            }
            else
            {
                Projectile.frameCounter = 0;
                Projectile.frame = 2;
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D texture = TextureAssets.Projectile[Projectile.type].Value;
            // Frame count is HARDCODED (3 — Prime's texture has 3 frames in
            // 1.4.4; frames 3-5 are the 1.4.5 Mechdusa-only variants).
            // Main.npcFrameCount returns 0/1 for NPCs whose textures haven't
            // loaded yet, which drew the whole sprite sheet. Projectile.frame
            // is set every tick by UpdateFrame().
            const int frameCount = 3;
            int frameHeight = texture.Height / frameCount;
            Rectangle frame = new Rectangle(0, Projectile.frame * frameHeight, texture.Width, frameHeight);
            Vector2 origin = frame.Size() / 2f;
            // Vanilla draws the sprite center 4 px below the NPC center
            // (Main.DrawNPC literal +4); scaled by the minion's 0.5.
            Vector2 drawPos = Projectile.Center + new Vector2(0f, 4f * Projectile.scale) - Main.screenPosition;

            // AFTERIMAGES (vanilla Main.DrawNPC, type 127): 5 ghosts at
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

            // BONE EYES GLOW OVERLAY (vanilla Main.DrawNPC, type 127): the
            // BoneEyes texture over the base sprite — same frame, same
            // rotation, additive color (200,200,200,0). This is the skull's
            // glowing eyes; without it the minion looks flat next to the
            // real boss.
            Texture2D eyesGlow = TextureAssets.BoneEyes.Value;
            Main.EntitySpriteDraw(eyesGlow, drawPos, frame, new Color(200, 200, 200, 0), Projectile.rotation, origin, Projectile.scale, SpriteEffects.None, 0);

            // LIMBS (vanilla draws the head first, then the arms in spawn
            // order: Cannon, Saw, Vice, Laser). Each limb draws its bone
            // chain first, then its sprite over it — matching DrawNPC,
            // where the bone block precedes the main sprite draw.
            if (limbsInitialized)
            {
                for (int i = 0; i < LimbCount; i++)
                    DrawLimb(i);
            }
            return false;
        }

        // Draws one limb: its two-segment bone chain, then the limb sprite
        // (plus the BoneLaser glow for the Laser limb).
        private void DrawLimb(int limb)
        {
            DrawLimbBones(limb);

            Texture2D texture = TextureAssets.Npc[LimbNpcId[limb]].Value;
            int frameCount = LimbFrameCount[limb];
            int frameHeight = texture.Height / frameCount;
            Rectangle frame = new Rectangle(0, limbFrame[limb] * frameHeight, texture.Width, frameHeight);
            Vector2 origin = frame.Size() / 2f;
            Vector2 drawPos = LimbCenter(limb) + new Vector2(0f, 4f * Projectile.scale) - Main.screenPosition;
            Color limbLight = Lighting.GetColor((int)(LimbCenter(limb).X / 16f), (int)(LimbCenter(limb).Y / 16f));
            Main.EntitySpriteDraw(texture, drawPos, frame, limbLight, limbRot[limb], origin, Projectile.scale, LimbFlip[limb], 0);

            if (limb == LimbLaser)
            {
                // BONE LASER GLOW OVERLAY (vanilla Main.DrawNPC, type 131):
                // additive (200,200,200,0), same frame/rotation as the limb.
                Texture2D laserGlow = TextureAssets.BoneLaser.Value;
                Main.EntitySpriteDraw(laserGlow, drawPos, frame, new Color(200, 200, 200, 0), limbRot[limb], origin, Projectile.scale, LimbFlip[limb], 0);
            }
        }

        // The bone chain from a limb to the head (Main.DrawNPC, aiStyle
        // 33-36), vanilla algorithm with all offsets/steps halved for the
        // 0.5-scale minion: start at the limb's top-left + (width/2 -
        // 5*ai[0], 20); k=0 aims at the head center offset (-200*ai[0],
        // +130) and steps 92 px, then advances a half-step; k=1 aims at
        // (-50*ai[0], +80) and steps 60 px. Each segment is a full
        // BoneArm2 texture (source rect and origin taken from BoneArm's
        // dimensions, as vanilla does), rotated atan2-1.57, lit by the
        // tile at the segment position. A smoke puff (type 6) spawns at
        // the joint each frame — vanilla gates it on
        // FocusHelper.AllowMiscDustEffects, which does not exist in tML
        // 1.4.4, so it spawns unconditionally here.
        private void DrawLimbBones(int limb)
        {
            float ai0 = LimbAi0[limb];
            Vector2 headCenter = Projectile.Center;
            Vector2 segPos = limbPos[limb] + new Vector2(LimbHalf - 2.5f * ai0, 10f);
            Texture2D boneTexture = TextureAssets.BoneArm2.Value;
            Rectangle boneRect = new Rectangle(0, 0, TextureAssets.BoneArm.Width(), TextureAssets.BoneArm.Height());
            Vector2 boneOrigin = new Vector2(TextureAssets.BoneArm.Width() * 0.5f, TextureAssets.BoneArm.Height() * 0.5f);

            for (int k = 0; k < 2; k++)
            {
                float dx = headCenter.X - segPos.X;
                float dy = headCenter.Y - segPos.Y;
                float step;
                if (k == 0)
                {
                    dx -= AnchorOffsetX * ai0;
                    dy += 65f;
                    step = BoneStepFirst;
                }
                else
                {
                    dx -= 25f * ai0;
                    dy += 40f;
                    step = BoneStepSecond;
                }
                float len = (float)Math.Sqrt(dx * dx + dy * dy);
                if (len <= 0f)
                    len = 1f;
                float stepScale = step / len;
                segPos.X += dx * stepScale;
                segPos.Y += dy * stepScale;

                float rotation = (float)Math.Atan2(dy, dx) - 1.57f;
                Color segLight = Lighting.GetColor((int)(segPos.X / 16f), (int)(segPos.Y / 16f));
                Main.EntitySpriteDraw(boneTexture, segPos - Main.screenPosition, boneRect, segLight, rotation, boneOrigin, Projectile.scale, SpriteEffects.None, 0);

                if (k == 0)
                {
                    // Advance a half-step along the same direction before
                    // the second segment.
                    segPos.X += dx * stepScale / 2f;
                    segPos.Y += dy * stepScale / 2f;
                }
                else
                {
                    // Smoke puff at the joint (vanilla: dust type 6, scale
                    // 2.5, noGravity; halved area/scale for the minion).
                    Vector2 dustPos = new Vector2(segPos.X + dx * stepScale - 8f, segPos.Y + dy * stepScale - 3f);
                    int dust = Dust.NewDust(dustPos, 15, 5, DustID.Smoke, dx * 0.02f, dy * 0.02f, 0, default, 1.25f);
                    if (dust >= 0 && dust < Main.maxDust)
                        Main.dust[dust].noGravity = true;
                }
            }
        }
    }

    /// <summary>
    /// Friendly Prime bomb (Brigitte's Charm, 2026-11-15; FIXED 2026-11-18):
    /// a custom replacement for the vanilla BombSkeletronPrime (102). The
    /// vanilla bomb hardcodes its explosion damage to 40 flat on detonation
    /// (Projectile.cs: Resize(128, 128); damage = 40), ignoring whatever
    /// damage it was spawned with — useless next to the charm's flat 6000.
    /// This bomb replicates the vanilla behavior — gravity arc, fuse hiss
    /// (SoundID.Item10) on the first tick, detonation on landing or
    /// timeout, 128x128 explosion — but the explosion deals the damage it
    /// was spawned with (6000 from the Prime minion).
    /// Like the vanilla bomb it deals NO contact damage before detonating
    /// (CanHitNPC returns false until the detonation tick).
    ///
    /// Fixes vs the 2026-11-15 build (vanilla AI_016_Bombs, type 102):
    /// - Terminal velocity 16 -> 10 (vanilla caps velocity.Y at 10).
    /// - Frame cycling added: 2 frames at 4 ticks/frame, drawn via
    ///   Projectile.frame. The old bomb never advanced its frame and its
    ///   PreDraw always drew frame 0.
    /// - Platform kill added: vanilla kills the bomb when its center is
    ///   over an active platform tile or tile 380.
    /// - Rotation spin removed: vanilla type 102 has no rotation code.
    /// - Ambient fuse smoke removed: vanilla type 102 emits no dust in
    ///   AI_016 (the dust blocks are for other bomb types).
    /// Kept: the one-shot Item10 fuse hiss (confirmed vanilla — the
    /// soundDelay == 0 block); immediate detonation on tile contact
    /// (vanilla rests the bomb 3 ticks first); gravity 0.2 (sits in a
    /// poorly-decompiled region of AI_016_Bombs and could not be verified,
    /// but it is the standard Terraria bomb gravity).
    /// </summary>
    public class PrimeBombFriendly : ModProjectile
    {
        private const float Gravity = 0.2f;
        private const float MaxFallSpeed = 10f; // vanilla caps type 102 at 10
        private const int ExplosionSize = 128; // vanilla explosion hitbox

        private bool initialized = false;
        private bool detonated = false;

        public override string Texture => "Terraria/Images/Projectile_102";

        public override void SetDefaults()
        {
            Projectile.width = 22;
            Projectile.height = 22;
            Projectile.friendly = true;
            Projectile.hostile = false;
            Projectile.DamageType = DamageClass.Generic;
            Projectile.knockBack = 0f;
            Projectile.penetrate = -1;

            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 20;

            // The bomb arcs with gravity and detonates on landing.
            Projectile.tileCollide = true;
            Projectile.ignoreWater = true;

            Projectile.timeLeft = 300; // fuse: detonates on timeout if it never lands
        }

        // No contact damage before detonation (vanilla: the bomb itself is
        // harmless; only the explosion hurts).
        public override bool? CanHitNPC(NPC target)
        {
            return detonated ? null : false;
        }

        public override void AI()
        {
            if (!initialized)
            {
                initialized = true;
                // Fuse hiss (vanilla: SoundID.Item10 on the first AI tick,
                // via the soundDelay == 0 block).
                SoundEngine.PlaySound(SoundID.Item10, Projectile.Center);
            }

            if (detonated)
            {
                // Frozen in place for the detonation tick(s); the resized
                // hitbox deals the damage, then timeLeft runs out.
                Projectile.velocity = Vector2.Zero;
                return;
            }

            // Vanilla kills the bomb when its center is over an active
            // platform tile or tile 380.
            int tileX = (int)(Projectile.Center.X / 16f);
            int tileY = (int)(Projectile.Center.Y / 16f);
            if (WorldGen.InWorld(tileX, tileY))
            {
                Tile tile = Main.tile[tileX, tileY];
                if (tile != null && tile.HasTile && (TileID.Sets.Platforms[tile.TileType] || tile.TileType == 380))
                {
                    Detonate();
                    return;
                }
            }

            // Gravity arc (aiStyle 16 behavior), terminal velocity 10.
            Projectile.velocity.Y += Gravity;
            if (Projectile.velocity.Y > MaxFallSpeed)
                Projectile.velocity.Y = MaxFallSpeed;

            // Frame cycle (vanilla: 2 frames at 4 ticks/frame).
            Projectile.frameCounter++;
            if (Projectile.frameCounter > 3)
            {
                Projectile.frame++;
                Projectile.frameCounter = 0;
            }
            if (Projectile.frame > 1)
                Projectile.frame = 0;

            // Timeout: detonate before the fuse runs out.
            if (Projectile.timeLeft <= 3)
                Detonate();
        }

        public override bool OnTileCollide(Vector2 oldVelocity)
        {
            // Detonates on landing (vanilla: timeLeft forced to 3 when
            // velocity.Y hits 0). Detonate immediately instead.
            if (!detonated)
                Detonate();
            return false;
        }

        private void Detonate()
        {
            if (detonated)
                return;
            detonated = true;

            // Vanilla explosion: resize to 128x128 centered on the bomb.
            // Damage stays at the spawn damage (6000) — unlike vanilla,
            // which overwrites it to 40 here.
            Projectile.Resize(ExplosionSize, ExplosionSize);
            Projectile.velocity = Vector2.Zero;
            Projectile.tileCollide = false;

            // Explosion sound + dust burst.
            SoundEngine.PlaySound(SoundID.Item14, Projectile.Center);
            for (int i = 0; i < 30; i++)
            {
                int dustType = Main.rand.NextBool(2) ? DustID.Torch : DustID.Smoke;
                Dust.NewDust(Projectile.position, Projectile.width, Projectile.height, dustType, Main.rand.NextFloat(-4f, 4f), Main.rand.NextFloat(-4f, 4f), 0, Color.White, 1.5f);
            }

            // Two ticks left: one full tick for the resized hitbox to deal
            // its damage, then the bomb is gone.
            Projectile.timeLeft = 2;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D texture = TextureAssets.Projectile[Projectile.type].Value;
            int frameCount = Main.projFrames[ProjectileID.BombSkeletronPrime];
            if (frameCount < 1)
                frameCount = 1;
            int frameHeight = texture.Height / frameCount;
            Rectangle frame = new Rectangle(0, Projectile.frame * frameHeight, texture.Width, frameHeight);
            Vector2 origin = frame.Size() / 2f;
            Main.EntitySpriteDraw(texture, Projectile.Center - Main.screenPosition, frame, lightColor, Projectile.rotation, origin, Projectile.scale, SpriteEffects.None, 0);
            return false;
        }
    }
}
