using System;
using System.Collections.Generic;
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
    /// Moon Lord boss minion (Brigitte's Charm, 2026-11-15): a half-scale
    /// Moon Lord that fights for the player with the Moon Lord's REAL
    /// attacks, taken from the decompiled AI_077-AI_081 (see
    /// ~/workspace/tmp/boss_attacks_batch1.md, Part 5).
    ///
    /// V1 SIMPLIFICATION (per the charm brief): the Moon Lord is ONE minion
    /// (anchored on the head, NPC 396) that uses the attacks of the head
    /// and hands COMBINED, plus the True Eye attacks — no separate hand /
    /// head / eye minions. The core itself has no attacks in vanilla; every
    /// attack below is borrowed from the head (396), the hands (397), or
    /// the True Eyes (400). All attack origins stay at the head (the
    /// minion's Center) — the hands and core are drawn but do not attack
    /// from their own positions.
    ///
    /// DRAWING (fixed 2026-11-18 — the v1 render was completely broken):
    /// NPC 396's frame count is 1, and its FindFrame sets frame.Y to
    /// frameCounter/4 as RAW PIXELS (0-7), not frame indices — a 0-7 px
    /// vertical jitter of the single frame. The v1 file set
    /// Main.projFrames = 8 and animated Projectile.frame 0-7 with NO
    /// PreDraw, so the default draw sliced the head texture into 8 strips
    /// and the minion rendered as a thin sliver. The minion now draws the
    /// full assembly in PreDraw at the vanilla pins (halved for 0.5
    /// scale): head at Center (source rect offset 0-7 px for the jitter),
    /// core (NPC 398, frame 0 — the 1-4 cycle only runs in the final
    /// phase) at Center + (0, +200) [vanilla AI_079 pins the head at
    /// core.Center + (0, -400)], hands (NPC 397) at Center + (-175, +150)
    /// and (+175, +150) [vanilla AI_078 pins the hands at core.Center +
    /// (+/-350, -100)]. The hands' eye frames ease like vanilla
    /// (frameCounter/7, counter easing toward 0 in combat = eye open,
    /// 21 at rest = eye closed). The left hand is drawn unflipped, the
    /// right hand flipped (vanilla AI_078: spriteDirection = side, and
    /// NPC drawing flips on spriteDirection 1); the head and core never
    /// flip. NOTE: the assembly is large (~400x300 px at half scale) —
    /// it reads as the Moon Lord, but it dwarfs the other minions.
    ///
    /// SUBSTITUTIONS (all verified against the decompiled Projectile AI):
    /// - Phantasmal Deathray (455) CANNOT be reused: aiStyle 84 anchors only
    ///   to NPC types 396/400 (it reads the owner's localAI eye state). The
    ///   minion reproduces the sweep with a stream of friendly DeathLasers
    ///   (100) fired along the same 120-degree arc at the same sweep rate
    ///   (2π/540 rad/tick), with the same 180-tick telegraph and Zombie_104
    ///   on fire. The True Eye's lesser ray uses the same substitute with a
    ///   lower bolt rate (thinner beam, 0.4x width). DeathLasers get
    ///   timeLeft 300 (the vanilla Destroyer value — the default is 2700).
    /// - PhantasmalEye (452) CANNOT be reused: its homing phase hardcodes
    ///   Main.player targets (aiStyle 82: Player.FindClosest +
    ///   Main.player[ai[1]]). A friendly 452 would home at the player and
    ///   self-destruct. The minion fires friendly HallowBossRainbowStreaks
    ///   (873) instead — the only vanilla projectile with built-in friendly
    ///   NPC homing (verified in AI_171: the friendly branch homes at
    ///   Main.npc[ai[0]]) — in the same ring/spiral patterns and timings.
    /// - MoonLeech (456) + Leech Blob (401) CANNOT be reused: the whole
    ///   subsystem hardcodes NPC 396 and Main.player targets (aiStyle 85),
    ///   and its purpose is healing the boss. Rethemed: the minion fires
    ///   VampireKnife (304) clots, the one vanilla projectile that drains
    ///   life and heals its owner on NPC hit (vampireHeal, verified in the
    ///   decompile), on the vanilla clot/blob timings (ticks 0/120/180/240).
    ///   The knives fly straight (304 does not home), so they are aimed
    ///   directly at the target at speed 20.
    /// - PhantasmalSphere (454) stick phase skipped: aiStyle 83 anchors the
    ///   sphere to Main.npc[ai[1]] with no validity guard, and the minion is
    ///   a projectile, not an NPC, so there is no valid anchor. Spheres are
    ///   spawned with ai[0] = 30 (exactly what the True Eye does), so they
    ///   fan out and drift immediately, then launch together on the vanilla
    ///   launch tick. The minion tracks its spheres by index to launch them.
    ///
    /// NOT ported (boss-only or meaningless for a minion): the core's
    /// coordination/teleport/death drama/despawn, eye destruction and True
    /// Eye spawning, eye-open invulnerability, the Moon Bite debuff (enemies
    /// don't lifesteal), contact damage (only the True Eye has any — the
    /// minion's own 6000 contact damage covers the ram), and all dead code.
    /// The Moon Boulder burst is included, gated on Main.getGoodWorld, like
    /// vanilla. Damage: every projectile is spawned friendly at a flat 6000
    /// typeless (Generic) damage with local immunity frames, per the charm
    /// spec. Ambient roars play only while fighting (a minion that roars
    /// constantly while idle would be unbearable).
    /// </summary>
    public class MoonLordMinion : ModProjectile
    {
        // --- Charm spec ---
        private const int MinionDamage = 6000;
        private const float TargetRange = 1000f;

        // --- States ---
        private const int StateIdle = 0;
        private const int StateBolts = 1;      // hands'/head's bolt triple
        private const int StateEyeRing = 2;    // hands' Phantasmal Eye ring
        private const int StateSphereFan = 3;  // hands' sphere fan + launch
        private const int StateLeech = 4;      // head's Moon Leech
        private const int StateDeathray = 5;   // head's Phantasmal Deathray
        private const int StateHexRam = 6;     // True Eye's sphere hexagon + ram
        private const int StateSpiralDash = 7; // True Eye's eye spiral dash
        private const int StateLesserRay = 8;  // True Eye's lesser deathray
        private const int StateChase = 9;      // True Eye chase (gap between attacks)

        // Attack cycle: the head's, hands' and True Eyes' attacks combined,
        // with a 53-tick chase (the True Eye idle duration) between attacks.
        private static readonly int[] Cycle = { 1, 2, 3, 1, 4, 5, 6, 7, 8 };

        // Durations in ticks (vanilla values from the spec).
        private const int BoltsDuration = 90;
        private const int EyeRingDuration = 70;
        private const int SphereFanDuration = 330;
        private const int LeechDuration = 435;
        private const int DeathrayDuration = 375;
        private const int HexRamDuration = 135;
        private const int SpiralDashDuration = 200;
        private const int LesserRayDuration = 375;
        private const int ChaseDuration = 53;

        // Assembly layout is computed at draw time from the real texture
        // sizes (see PreDraw) — NOT hardcoded offsets. The first version
        // used fixed pins (core +200, hands +/-175/+150) and the assembly
        // fell apart in-game: the core read as a tiny green blob 200 px
        // below the head and the hands floated as disconnected fists
        // (Peyton's playtest, 2026-11-18). The minion's Center is the
        // HEAD; the core now tucks under it, the hands flank the core,
        // and tentacle arms link them (Peyton asked for arms).

        // Idle hover offset, relative to the player — distinct per boss so
        // the charm's minions don't all stack on one point. (Other bosses
        // already use (380,-120), (0,-240), (140,-140), (-140,-180),
        // (220,-260), (0,-380), (-220,-260), and the Empress (-360,-80);
        // this one is far right, high up.)
        private static readonly Vector2 IdleOffset = new Vector2(420f, -300f);

        // --- State (stateTimer/cycleIndex/targetWho and the fields below
        // are owner-side only; clients don't run minion AI) ---
        private int stateTimer = 0;
        private bool stateChanged = false;
        private int cycleIndex = 0;
        private int targetWho = -1;
        private bool initialized = false;

        private float aimAngle = 0f;       // bolt eye aim (AngleLerp tracking)
        private List<int> linkedSpheres = new List<int>(); // spheres awaiting launch
        private Vector2 ramDir = Vector2.UnitY; // HexRam charge direction (locked at tick 105)
        private float sweepAngle = 0f;     // deathray sweep angle
        private float sweepDir = 1f;       // deathray sweep direction (+1 / -1)
        private float dashAim = 0f;        // spiral dash heading
        private float spiralTurn = 0f;     // spiral dash turn rate
        private float handFrameCounter = 21f; // hands' eye frame counter (0 = open, 21 = closed)

        // The minion is anchored on the HEAD (NPC 396) — the Moon Lord's
        // most recognizable part, and the origin of the head's attacks.
        // The core (398) and hands (397) are drawn in PreDraw at the
        // vanilla pins; they are not separate minions.
        public override string Texture => "Terraria/Images/NPC_396";

        public override void SetStaticDefaults()
        {
            // The head texture is a SINGLE frame (npcFrameCount 396 = 1).
            // Its FindFrame sets frame.Y to frameCounter/4 as raw PIXELS
            // (a 0-7 px jitter), not frame indices — see PreDraw.
            Main.projFrames[Type] = 1;
        }

        public override void SetDefaults()
        {
            // Vanilla head hitbox 38x56; the minion hitbox is a round 50x50
            // at 0.5 scale so the head reads and clicks clearly among the
            // other minions (the drawn assembly is much larger than the
            // hitbox — the core and hands are visual only).
            Projectile.width = 50;
            Projectile.height = 50;
            Projectile.scale = 0.5f;

            Projectile.friendly = true;
            Projectile.hostile = false;
            Projectile.minion = true;
            Projectile.minionSlots = 1;
            Projectile.penetrate = -1; // a minion must never die on contact
            Projectile.damage = MinionDamage;
            Projectile.DamageType = DamageClass.Generic; // typeless, per the charm spec
            Projectile.knockBack = 0f;

            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 20;

            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;

            // The charm's ModPlayer refreshes this while equipped.
            Projectile.timeLeft = 2;
            Projectile.netImportant = true;
        }

        public override void AI()
        {
            Player player = Main.player[Projectile.owner];
            if (!player.active || player.dead)
            {
                // The charm stops refreshing timeLeft; drift and fade out.
                Projectile.velocity *= 0.95f;
                return;
            }

            if (!initialized)
            {
                initialized = true;
                // Spawn roar (the core plays Zombie_92 at tick 30 of its
                // spawn-in; the minion plays it once on summon).
                SoundEngine.PlaySound(new SoundStyle("Terraria/Sounds/Zombie_92"), Projectile.Center);
            }

            // Recall if too far from the player (2026-11-18 pattern,
            // shared with the weapon minions).
            if (Vector2.Distance(Projectile.Center, player.Center) > 2000f)
            {
                Projectile.Center = player.Center + IdleOffset;
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

            bool hasTarget = FindTarget();
            NPC target = hasTarget ? Main.npc[targetWho] : null;

            // Head jitter counter (vanilla FindFrame 396: +1/tick, +1 extra
            // on a 1/10 chance, wrapping at 32; the jitter offset is
            // counter/4 = 0-7 raw pixels, applied in PreDraw).
            Projectile.frameCounter++;
            if (Main.rand.Next(10) == 0)
                Projectile.frameCounter++;
            if (Projectile.frameCounter >= 32)
                Projectile.frameCounter = 0;

            // Hands' eye frames (vanilla AI_078/FindFrame 397: the counter
            // eases +/-1/tick toward its target, frame = counter/7;
            // 0 = eye open, 21 = eye closed). The minion's hands open in
            // combat and close at rest.
            float handTarget = hasTarget ? 0f : 21f;
            if (handFrameCounter < handTarget)
                handFrameCounter++;
            else if (handFrameCounter > handTarget)
                handFrameCounter--;

            // Ambient roars, only while fighting: the core's random roar
            // (Zombie_93-99, 1/200 per tick) and the True Eye's ambient
            // (Zombie_100, 1/420 per tick).
            if (hasTarget)
            {
                if (Main.rand.Next(200) == 0)
                    SoundEngine.PlaySound(new SoundStyle($"Terraria/Sounds/Zombie_{Main.rand.Next(93, 100)}"), Projectile.Center);
                if (Main.rand.Next(420) == 0)
                    SoundEngine.PlaySound(new SoundStyle("Terraria/Sounds/Zombie_100"), Projectile.Center);
            }

            // Losing the target mid-attack returns to idle; a fresh attack
            // starts when a new target is found.
            int state = (int)Projectile.ai[0];
            if (!hasTarget && state != StateIdle)
            {
                SetState(StateIdle);
                state = StateIdle;
            }

            switch (state)
            {
                case StateIdle: UpdateIdle(player, target); break;
                case StateBolts: UpdateBolts(target); break;
                case StateEyeRing: UpdateEyeRing(target); break;
                case StateSphereFan: UpdateSphereFan(target); break;
                case StateLeech: UpdateLeech(target); break;
                case StateDeathray: UpdateDeathray(target, boltInterval: 2, boulders: true); break;
                case StateHexRam: UpdateHexRam(target); break;
                case StateSpiralDash: UpdateSpiralDash(target); break;
                case StateLesserRay: UpdateDeathray(target, boltInterval: 3, boulders: false); break;
                case StateChase: UpdateChase(target); break;
                default: SetState(StateIdle); break;
            }

            // The tick a state changes doesn't count toward the new state's
            // timer, so every attack's tick-0 actions fire on its first tick.
            if (stateChanged)
                stateChanged = false;
            else
                stateTimer++;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            // The full Moon Lord assembly (see the class doc): core first
            // (behind), then arms, then hands, then the head on top so
            // its tentacles overlap the core. Frame counts read from the
            // game's own npcFrameCount table in the binary and HARDCODED:
            // head 1, hands 4, core 5. Nothing rotates; only the right
            // hand is flipped (vanilla AI_078).
            //
            // Layout is computed from the real texture sizes every frame:
            // the core is scaled to 65% of the head's drawn width and
            // tucked under the head (overlapping its tentacles), and the
            // hands flank the core — so the pieces read as ONE creature
            // instead of a head with a distant blob and floating fists.
            Vector2 screenCenter = Projectile.Center - Main.screenPosition;

            // Head texture first (its size drives the whole layout).
            Texture2D headTex = TextureAssets.Projectile[Projectile.type].Value;
            float headDrawnW = headTex.Width * Projectile.scale;
            float headDrawnH = headTex.Height * Projectile.scale;

            // Core (NPC_398, 5 frames): frame 0 — the 1-4 cycle only runs
            // in the final phase (vanilla FindFrame 398, ai[0] > 0).
            const int coreFrameCount = 5;
            Texture2D coreTex = TextureAssets.Npc[NPCID.MoonLordCore].Value;
            int coreFrameHeight = coreTex.Height / coreFrameCount;
            Rectangle coreFrame = new Rectangle(0, 0, coreTex.Width, coreFrameHeight);
            // Scale the core to 65% of the head's drawn width so it reads
            // as a torso, never smaller than the minion scale and never
            // above 0.9 (beyond that it would dwarf the head).
            float coreScale = MathHelper.Clamp(headDrawnW * 0.65f / coreFrame.Width, Projectile.scale, 0.9f);
            float coreDrawnW = coreFrame.Width * coreScale;
            float coreDrawnH = coreFrameHeight * coreScale;
            // Tuck the core under the head: its top edge overlaps the
            // head's bottom tentacles by 12 px.
            Vector2 corePos = screenCenter + new Vector2(0f, headDrawnH / 2f + coreDrawnH / 2f - 12f);
            Main.EntitySpriteDraw(coreTex, corePos, coreFrame, lightColor, 0f, coreFrame.Size() / 2f, coreScale, SpriteEffects.None, 0);

            // Hands (NPC_397, 4 frames): frame = counter/7 (0 open, 3 closed).
            const int handFrameCount = 4;
            int handFrameIndex = (int)MathHelper.Clamp((int)(handFrameCounter / 7f), 0, handFrameCount - 1);
            Texture2D handTex = TextureAssets.Npc[NPCID.MoonLordHand].Value;
            int handFrameHeight = handTex.Height / handFrameCount;
            Rectangle handFrame = new Rectangle(0, handFrameIndex * handFrameHeight, handTex.Width, handFrameHeight);
            float handDrawnW = handFrame.Width * Projectile.scale;
            // Flank the core: inner edges overlap the core's sides by 8 px,
            // vertically centered on the core.
            float handX = coreDrawnW / 2f + handDrawnW / 2f - 8f;
            Vector2 leftHandPos = corePos + new Vector2(-handX, 0f);
            Vector2 rightHandPos = corePos + new Vector2(handX, 0f);

            // ARMS (Peyton, 2026-11-18 — vanilla has none, the hands just
            // float): tentacle limbs from the core's upper sides to each
            // hand, drawn as tapered segments along a curve bowed outward.
            // Tan at the shoulder fading to the tentacle teal at the hand,
            // matching the head's tentacles.
            DrawTentacleArm(corePos + new Vector2(-coreDrawnW * 0.3f, -coreDrawnH * 0.2f), leftHandPos + new Vector2(handDrawnW * 0.3f, 0f), lightColor);
            DrawTentacleArm(corePos + new Vector2(coreDrawnW * 0.3f, -coreDrawnH * 0.2f), rightHandPos + new Vector2(-handDrawnW * 0.3f, 0f), lightColor);

            // Left hand unflipped, right hand flipped (vanilla AI_078:
            // spriteDirection = side, NPC drawing flips on 1).
            Main.EntitySpriteDraw(handTex, leftHandPos, handFrame, lightColor, 0f, handFrame.Size() / 2f, Projectile.scale, SpriteEffects.None, 0);
            Main.EntitySpriteDraw(handTex, rightHandPos, handFrame, lightColor, 0f, handFrame.Size() / 2f, Projectile.scale, SpriteEffects.FlipHorizontally, 0);

            // Head (NPC_396, 1 frame): the source rect is offset 0-7 px
            // vertically (vanilla FindFrame 396 sets frame.Y to
            // frameCounter/4 as raw pixels — a jitter, not an animation).
            // The rect height shrinks by the jitter so it never samples
            // past the texture's bottom edge.
            int jitter = Projectile.frameCounter / 4;
            Rectangle headFrame = new Rectangle(0, jitter, headTex.Width, headTex.Height - jitter);
            Main.EntitySpriteDraw(headTex, screenCenter, headFrame, lightColor, 0f, new Vector2(headTex.Width / 2f, headTex.Height / 2f), Projectile.scale, SpriteEffects.None, 0);

            return false;
        }

        // One tentacle arm: tapered segments along a quadratic curve from
        // the shoulder to the hand, bowed outward. Drawn with the 1x1
        // MagicPixel stretched into rotated rects; the color fades from
        // the Moon Lord's bone-tan at the shoulder to his tentacle teal
        // at the hand. All positions are screen coordinates.
        private void DrawTentacleArm(Vector2 shoulder, Vector2 hand, Color lightColor)
        {
            // Control point: bowed outward (away from the body center)
            // and slightly down, so the arm curves instead of cutting
            // straight across the core.
            Vector2 mid = (shoulder + hand) / 2f;
            float bowDir = hand.X >= shoulder.X ? 1f : -1f;
            Vector2 control = mid + new Vector2(bowDir * 26f, 14f);

            const int segments = 6;
            Vector2 prev = shoulder;
            Color boneTan = new Color(198, 178, 148);
            Color tentacleTeal = new Color(120, 220, 200);
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                // Quadratic Bezier point.
                float u = 1f - t;
                Vector2 point = u * u * shoulder + 2f * u * t * control + t * t * hand;
                Vector2 delta = point - prev;
                float length = delta.Length();
                if (length < 0.5f)
                {
                    prev = point;
                    continue;
                }
                // Taper: 16 px at the shoulder down to 8 px at the hand.
                float width = MathHelper.Lerp(16f, 8f, t);
                Color segmentColor = new Color(Color.Lerp(boneTan, tentacleTeal, t).ToVector4() * lightColor.ToVector4());
                Main.EntitySpriteDraw(
                    TextureAssets.MagicPixel.Value,
                    prev + delta / 2f,
                    new Rectangle(0, 0, 1, 1),
                    segmentColor,
                    delta.ToRotation(),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(length, width),
                    SpriteEffects.None, 0);
                prev = point;
            }
        }

        private void UpdateIdle(Player player, NPC target)
        {
            if (target != null)
            {
                SetState(StateChase);
                return;
            }
            FlyTo(player.Center + IdleOffset, 10f, 0.08f);
        }

        private void UpdateChase(NPC target)
        {
            // True Eye chase mode: steer toward 200px above the target at
            // speed 24, heavily smoothed (velocity = (velocity * 29 +
            // desired) / 30). 53 ticks, then the next attack in the cycle.
            FlyTo(target.Center + new Vector2(0f, -200f), 24f, 1f / 30f);

            if (stateTimer >= ChaseDuration)
            {
                int next = Cycle[cycleIndex % Cycle.Length];
                cycleIndex++;
                SetState(next);
            }
        }

        private void UpdateBolts(NPC target)
        {
            // Hands'/head's bolt triple (hand timing): the eye tracks the
            // predicted target position (20-tick lead, AngleLerp 0.5/tick);
            // telegraph sound NPCHit6 at tick 55; three PhantasmalBolts
            // (462) at ticks 76, 83, 90. Bolt velocity 8, extraUpdates 3 =
            // 32 px/tick effective.
            FlyTo(target.Center + new Vector2(0f, -200f), 6f, 0.05f);

            Vector2 predicted = target.Center + target.velocity * 20f;
            float wantAim = (predicted - Projectile.Center).ToRotation();
            if (stateTimer == 0)
                aimAngle = wantAim;
            else
                aimAngle = Utils.AngleLerp(aimAngle, wantAim, 0.5f);

            if (stateTimer == 55)
                SoundEngine.PlaySound(SoundID.NPCHit6, Projectile.Center);

            if (stateTimer == 76 || stateTimer == 83 || stateTimer == 90)
            {
                Vector2 dir = aimAngle.ToRotationVector2();
                SpawnFriendly(ProjectileID.PhantasmalBolt, Projectile.Center + dir * 20f, dir * 8f);
            }

            if (stateTimer >= BoltsDuration)
                SetState(StateChase);
        }

        private void UpdateEyeRing(NPC target)
        {
            // Hands' Phantasmal Eye ring: ticks 0-27 telegraph (the aim
            // point orbits an ellipse, one revolution per 28 ticks); one
            // eye every 4 ticks at ticks 28-52 (7 total). The eyes are the
            // 873 substitute (see class doc — vanilla 452 homing is
            // player-only). Vanilla orbit angle: 2*pi*(t % 28)/28 - PI/2
            // (the -PI/2 puts the first eye at the TOP of the ellipse).
            FlyTo(target.Center + new Vector2(0f, -200f), 6f, 0.05f);

            if (stateTimer >= 28 && stateTimer <= 52 && (stateTimer - 28) % 4 == 0)
            {
                float orbit = ((stateTimer % 28) / 28f) * MathHelper.TwoPi - MathHelper.PiOver2;
                Vector2 ellipse = new Vector2((float)Math.Cos(orbit) * 30f, (float)Math.Sin(orbit) * 66f);
                Vector2 dir = ellipse.SafeNormalize(Vector2.UnitY);
                SpawnFriendly(ProjectileID.HallowBossRainbowStreak, Projectile.Center + dir * 29f, dir * 8f, targetWho, 0.7f);
            }

            if (stateTimer >= EyeRingDuration)
                SetState(StateChase);
        }

        private void UpdateSphereFan(NPC target)
        {
            // Hands' sphere fan + launch: 6 PhantasmalSpheres (454), one
            // every 30 ticks at ticks 30-180, fanned velocities; the hand
            // decelerates at ticks 210-281; at tick 292 all six launch at
            // once at speed 12 (effective 24 via extraUpdates), aimed from
            // a point 350px above the minion so the volley converges from
            // above. Spheres spawn with ai[0] = 30 (skip the stick phase —
            // see class doc).
            if (stateTimer < 210)
            {
                FlyTo(target.Center + new Vector2(0f, -200f), 8f, 0.06f);
            }
            else if (stateTimer < 282)
            {
                Projectile.velocity *= 0.9f;
            }

            if (stateTimer >= 30 && stateTimer <= 180 && (stateTimer - 30) % 30 == 0)
            {
                int k = (stateTimer - 30) / 30; // sphere index 0-5
                float num = target.Center.X >= Projectile.Center.X ? 1f : -1f;
                Vector2 vel = new Vector2((5f * num + (k - 3.5f) * num * 3f) * 1.2f, (-8f + (k - 4.5f)) * 1.2f);
                int p = SpawnFriendly(ProjectileID.PhantasmalSphere, Projectile.Center, vel, 30f, -1f);
                if (p >= 0)
                    linkedSpheres.Add(p);
            }

            if (stateTimer == 292)
            {
                Vector2 aimFrom = Projectile.Center + new Vector2(0f, -350f);
                Vector2 dir = (target.Center - aimFrom).SafeNormalize(Vector2.UnitY);
                LaunchSpheres(dir);
            }

            if (stateTimer >= SphereFanDuration)
                SetState(StateChase);
        }

        private void UpdateLeech(NPC target)
        {
            // Head's Moon Leech, rethemed (see class doc): VampireKnife
            // clots that drain the enemy and heal the player, on the
            // vanilla clot/blob timings — one at tick 0, then one more at
            // each of ticks 120, 180, 240. Clots spawn at the head's
            // Center + (0, +216) in vanilla — halved to +108 here.
            // No sound (the clots/blobs are silent in vanilla).
            FlyTo(target.Center + new Vector2(0f, -150f), 6f, 0.05f);

            if (stateTimer == 0 || stateTimer == 120 || stateTimer == 180 || stateTimer == 240)
            {
                Vector2 dir = (target.Center - Projectile.Center).SafeNormalize(Vector2.UnitY);
                SpawnFriendly(ProjectileID.VampireKnife, Projectile.Center + new Vector2(0f, 108f), dir * 20f);
            }

            if (stateTimer >= LeechDuration)
                SetState(StateChase);
        }

        private void UpdateDeathray(NPC target, int boltInterval, bool boulders)
        {
            // Head's Phantasmal Deathray (boltInterval 2, with the
            // GetGoodWorld boulder burst) / True Eye's lesser deathray
            // (boltInterval 3, thinner beam, no boulders). The beam itself
            // is the DeathLaser sweep substitute (see class doc).
            // Ticks 0-179 telegraph (dust converges on the eye); tick 180
            // FIRE; ticks 180-359 the sweep runs; ticks 360-374 the eye
            // closes.
            if (stateTimer < 180)
            {
                Projectile.velocity *= 0.95f;
                int dustCount = stateTimer >= 120 ? 2 : (stateTimer >= 60 ? 1 : 0);
                for (int i = 0; i < dustCount; i++)
                {
                    Vector2 at = Projectile.Center + Main.rand.NextVector2Circular(120f, 120f);
                    int d = Dust.NewDust(at, 1, 1, 229, 0f, 0f, 0, default, 1f);
                    Main.dust[d].velocity = (Projectile.Center - at).SafeNormalize(Vector2.Zero) * 4f;
                    Main.dust[d].noGravity = true;
                }

                if (stateTimer == 179)
                {
                    // Lock the sweep: aim at the target, rotated ∓60° — if
                    // the target is to the right, +60° (clockwise); if to
                    // the left, -60°. The beam then sweeps back across the
                    // target at 2π/540 rad/tick (120° total over 180 ticks).
                    float aim = (target.Center - Projectile.Center).ToRotation();
                    bool targetRight = target.Center.X >= Projectile.Center.X;
                    sweepAngle = aim + (targetRight ? (MathHelper.Pi / 3f) : -(MathHelper.Pi / 3f));
                    sweepDir = targetRight ? -1f : 1f;
                }
            }
            else if (stateTimer < 360)
            {
                if (stateTimer == 180)
                    SoundEngine.PlaySound(new SoundStyle("Terraria/Sounds/Zombie_104"), Projectile.Center);

                if ((stateTimer - 180) % boltInterval == 0)
                {
                    Vector2 dir = sweepAngle.ToRotationVector2();
                    SpawnFriendly(ProjectileID.DeathLaser, Projectile.Center + dir * 20f, dir * 20f, timeLeft: 300);
                }
                sweepAngle += sweepDir * (MathHelper.TwoPi / 540f);
                Projectile.velocity *= 0.95f;
            }
            else
            {
                // Eye closes. GetGoodWorld only: the 30-boulder burst,
                // exactly like the vanilla head (seed-exclusive attack).
                // Vanilla fires it when the eye's localAI[1] decays to 0
                // (~tick 374, 15 ticks into the close), and only if the
                // head's tile is not solid.
                if (boulders && stateTimer == 374 && Main.getGoodWorld
                    && !WorldGen.SolidTile((int)(Projectile.Center.X / 16f), (int)(Projectile.Center.Y / 16f)))
                {
                    for (int i = 0; i < 30; i++)
                    {
                        Vector2 vel = new Vector2(Main.rand.Next(-1599, 1600) * 0.01f, Main.rand.Next(-1599, 1) * 0.01f);
                        SpawnFriendly(ProjectileID.MoonBoulder, Projectile.Center, vel);
                    }
                }
                Projectile.velocity *= 0.95f;
            }

            int duration = boulders ? DeathrayDuration : LesserRayDuration;
            if (stateTimer >= duration)
                SetState(StateChase);
        }

        private void UpdateHexRam(NPC target)
        {
            // True Eye's sphere hexagon + ram: ticks 0-14 close and
            // decelerate; ticks 15-74 the aim point traverses a hexagon
            // (radius 30px) in vertex order 0→2→5→3→1→4→0, one 10-tick leg
            // per pair, firing one sphere at the start of each leg (ticks
            // 15, 25, 35, 45, 55, 65); tick 75 hop upward (spheres rise
            // too); ticks 75-104 aim; ticks 105-119 THE RAM at 24 px/tick
            // with all six spheres launched in the same direction; ticks
            // 120-134 recover. The minion's own contact damage (6000) is
            // the ram damage.
            if (stateTimer < 15)
            {
                Projectile.velocity *= 0.8f;
            }
            else if (stateTimer < 75)
            {
                int leg = (stateTimer - 15) / 10; // 0-5
                float legProgress = ((stateTimer - 15) % 10) / 10f;
                int[] order = { 0, 2, 5, 3, 1, 4, 0 };
                Vector2 a = HexVertex(order[leg]);
                Vector2 b = HexVertex(order[leg + 1]);
                Vector2 aimPoint = Vector2.Lerp(a, b, legProgress);

                if ((stateTimer - 15) % 10 == 0)
                {
                    Vector2 dir = aimPoint.SafeNormalize(Vector2.UnitY);
                    int p = SpawnFriendly(ProjectileID.PhantasmalSphere, Projectile.Center + aimPoint, dir * 4f, 30f, -1f);
                    if (p >= 0)
                        linkedSpheres.Add(p);
                }

                for (int i = 0; i < 2; i++)
                {
                    int d = Dust.NewDust(Projectile.Center + aimPoint - Vector2.One * 4f, 0, 0, 229);
                    Main.dust[d].velocity += aimPoint / 15f;
                    Main.dust[d].noGravity = true;
                }
                Projectile.velocity *= 0.9f;
            }
            else if (stateTimer == 75)
            {
                // Hop upward; the linked spheres rise with the eye.
                Projectile.velocity = new Vector2(0f, -7f);
                if (Projectile.owner == Main.myPlayer)
                {
                    foreach (int p in linkedSpheres)
                    {
                        if (p < 0 || p >= Main.maxProjectiles)
                            continue;
                        Projectile s = Main.projectile[p];
                        if (s.active && s.type == ProjectileID.PhantasmalSphere && s.owner == Projectile.owner)
                        {
                            s.velocity += new Vector2(0f, -7f);
                            s.netUpdate = true;
                        }
                    }
                }
            }
            else if (stateTimer < 105)
            {
                // Aim at the target; upward velocity decays.
                Projectile.velocity *= 0.96f;
            }
            else if (stateTimer < 120)
            {
                if (stateTimer == 105)
                {
                    ramDir = (target.Center - Projectile.Center).SafeNormalize(Vector2.UnitY);
                    LaunchSpheres(ramDir);
                }
                Projectile.velocity = ramDir * 24f;
                // Sustained screech — vanilla plays Zombie_102 every tick
                // of the ram window.
                SoundEngine.PlaySound(new SoundStyle("Terraria/Sounds/Zombie_102"), Projectile.Center);
            }
            else
            {
                Projectile.velocity *= 0.92f;
            }

            if (stateTimer >= HexRamDuration)
                SetState(StateChase);
        }

        private void UpdateSpiralDash(NPC target)
        {
            // True Eye's eye spiral dash: ticks 0-14 close and decelerate;
            // ticks 15-44 telegraph wobble; at tick 45 pick a random turn
            // direction and dash along a tightening spiral (speed lerps
            // 8→20, aim rotates by the turn rate each tick), seeding one
            // homing eye every 10 ticks (14 total, ticks 45-175); ticks
            // 185-199 recover. The dash is direction-based, not
            // target-tracking, after tick 45 (like vanilla). The eyes are
            // the 873 substitute (see class doc).
            if (stateTimer < 15)
            {
                Projectile.velocity *= 0.9f;
            }
            else if (stateTimer < 45)
            {
                Projectile.velocity *= 0.95f;
            }
            else if (stateTimer < 185)
            {
                if (stateTimer == 45)
                {
                    spiralTurn = (Main.rand.NextBool() ? 1f : -1f) * (MathHelper.TwoPi / 40f);
                    dashAim = (target.Center - Projectile.Center).ToRotation();
                }
                if (stateTimer == 85 || stateTimer == 125 || stateTimer == 165)
                    spiralTurn *= 0.95f;

                dashAim += spiralTurn;
                float speed = MathHelper.Lerp(8f, 20f, (stateTimer - 45) / 140f);
                Vector2 dir = dashAim.ToRotationVector2();
                Projectile.velocity = dir * speed;

                if ((stateTimer - 45) % 10 == 0)
                {
                    SpawnFriendly(ProjectileID.HallowBossRainbowStreak, Projectile.Center + dir * 17f, dir * 8f, targetWho, 0.7f);
                }
            }
            else
            {
                Projectile.velocity *= 0.88f;
            }

            if (stateTimer >= SpiralDashDuration)
                SetState(StateChase);
        }

        // --- Helpers ---

        // Vanilla hexagon vertices (AI_081): vertex 0 is at the TOP
        // (UnitY * -30), and each vertex rotates 60° from there.
        private static Vector2 HexVertex(int index)
        {
            float angle = index * (MathHelper.TwoPi / 6f);
            return new Vector2((float)Math.Sin(angle) * 30f, -(float)Math.Cos(angle) * 30f);
        }

        /// <summary>
        /// Launches all linked PhantasmalSpheres at once in the given
        /// direction at speed 12 (effective 24 via extraUpdates), exactly
        /// like the vanilla launch (ai[0] = -1). Owner-only.
        /// </summary>
        private void LaunchSpheres(Vector2 direction)
        {
            if (Projectile.owner != Main.myPlayer)
                return;
            foreach (int p in linkedSpheres)
            {
                if (p < 0 || p >= Main.maxProjectiles)
                    continue;
                Projectile s = Main.projectile[p];
                if (!s.active || s.type != ProjectileID.PhantasmalSphere || s.owner != Projectile.owner)
                    continue;
                s.ai[0] = -1f; // launched state: frame 1, extraUpdates 1
                s.velocity = direction * 12f;
                s.netUpdate = true;
            }
            linkedSpheres.Clear();
        }

        private void SetState(int newState)
        {
            Projectile.ai[0] = newState;
            stateTimer = 0;
            stateChanged = true;
            linkedSpheres.Clear(); // spheres belong to one attack only
            Projectile.netUpdate = true;
        }

        private bool FindTarget()
        {
            // Revalidate the current target first (with a little hysteresis
            // so the minion doesn't drop a target that briefly steps out).
            if (targetWho >= 0 && targetWho < Main.maxNPCs)
            {
                NPC t = Main.npc[targetWho];
                if (t.active && t.CanBeChasedBy() && Vector2.Distance(t.Center, Projectile.Center) < TargetRange * 1.2f)
                    return true;
            }
            targetWho = -1;
            float best = TargetRange;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (!npc.active || !npc.CanBeChasedBy())
                    continue;
                float d = Vector2.Distance(npc.Center, Projectile.Center);
                if (d < best)
                {
                    best = d;
                    targetWho = i;
                }
            }
            return targetWho >= 0;
        }

        private void FlyTo(Vector2 point, float speed, float turnRate)
        {
            Vector2 to = point - Projectile.Center;
            float dist = to.Length();
            if (dist < 20f)
            {
                Projectile.velocity *= 0.9f;
                return;
            }
            Vector2 desired = to / dist * Math.Min(speed, 2f + dist * 0.1f);
            Projectile.velocity = Vector2.Lerp(Projectile.velocity, desired, turnRate);
            if (Projectile.velocity.HasNaNs())
                Projectile.velocity = Vector2.Zero;
        }

        /// <summary>
        /// Spawns a friendly version of a vanilla projectile at the given
        /// CENTER position, at the charm's flat 6000 typeless damage.
        /// Owner-only (the owner's client spawns and syncs it). Returns the
        /// projectile index, or -1 if the spawn failed or this client isn't
        /// the owner. Pass timeLeft &gt; 0 to override the vanilla lifetime
        /// (DeathLaser needs 300 — its 2700 default would flood the field).
        /// </summary>
        private int SpawnFriendly(int type, Vector2 center, Vector2 velocity, float ai0 = 0f, float ai1 = 0f, float ai2 = 0f, int timeLeft = -1)
        {
            if (Projectile.owner != Main.myPlayer)
                return -1;
            int p = Projectile.NewProjectile(Projectile.GetSource_FromThis(), center, velocity, type, MinionDamage, 0f, Projectile.owner, ai0, ai1, ai2);
            if (p < 0 || p >= Main.maxProjectiles)
                return -1;
            Projectile spawned = Main.projectile[p];
            spawned.Center = center; // NewProjectile takes a top-left position; we aim by center
            spawned.friendly = true;
            spawned.hostile = false;
            spawned.usesLocalNPCImmunity = true;
            spawned.localNPCHitCooldown = 20;
            spawned.DamageType = DamageClass.Generic;
            if (timeLeft > 0)
                spawned.timeLeft = timeLeft;
            spawned.netUpdate = true;
            return p;
        }
    }
}
