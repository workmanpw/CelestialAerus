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
    /// Empress of Light boss minion (Brigitte's Charm, 2026-11-15): a
    /// half-scale Empress that fights for the player with her REAL attacks,
    /// taken from the decompiled AI_120_HallowBoss (see
    /// ~/workspace/tmp/boss_attacks_batch1.md, Part 4). She runs the vanilla
    /// phase-1 attack cycle, then the vanilla phase-2 (Expert) cycle after
    /// 30 s active, with a hub dash between attacks, and plays the boss's
    /// real sounds.
    ///
    /// PHASES (fixed 2026-11-18): vanilla has TWO separate cycles, not one
    /// (the v1 file ran a single 13-attack cycle with the phase-2 attacks
    /// tacked on the end). Phase 1 (hub selection, num37 = 10 slots):
    /// {bolts, dash, sundance, dash, rainbow, bolts, dash, lances, dash,
    /// rainbow}. Phase 2 (Expert, num37 = 10 slots): {walls, bolts, dash,
    /// barrage, rainbow, bolts, sundance, lances, dash, spiral}. The
    /// phase-2 transition (vanilla case 10) resets the cycle counter, so
    /// the first phase-2 attack is always the walls. Minions have no life
    /// fractions, so the phase runs on age (30 s, like the other bosses);
    /// the 200-tick transition state itself is skipped. The frame is the
    /// persistent phase frame (FindFrame 636: phase 2 ? 1 : 0), not a
    /// per-attack frame.
    ///
    /// DRAWING: vanilla never flips the Empress (AI_120 never sets
    /// spriteDirection) and never rotates her. The minion draws her
    /// unflipped, unrotated, always.
    ///
    /// Adaptations (the boss targets the player; the minion targets enemies):
    /// - All spawn rings, aims and leads are computed from the TARGET ENEMY's
    ///   position and velocity instead of the player's.
    /// - No enrage (never the 9999 damage), no despawn, no invulnerability
    ///   windows, no dead case 3, no Expert hub-exit velocity burst.
    /// - SUBSTITUTION — Sun Dance (vanilla 923) CANNOT be reused: it dies
    ///   instantly unless its anchor NPC is an active type-636 Empress
    ///   (aiStyle 180, verified in the decompile). The minion fires a
    ///   spiral of friendly HallowBossRainbowStreaks (873) in the same
    ///   3-wave pattern instead, with Item159 played per wave.
    /// - Damage: every projectile is spawned friendly at a flat 6000
    ///   typeless (Generic) damage with local immunity frames, per the
    ///   charm spec. Timings use the vanilla Classic phase-1 values.
    /// </summary>
    public class EmpressOfLightMinion : ModProjectile
    {
        // --- Charm spec ---
        private const int MinionDamage = 6000;
        private const float TargetRange = 1000f;

        // --- States (match the vanilla ai[0] values for readability) ---
        private const int StateIdle = 0;
        private const int StateHub = 1;
        private const int StateBolts = 2;
        private const int StateLances = 4;
        private const int StateRainbow = 5;
        private const int StateSunDance = 6;
        private const int StateWalls = 7;
        private const int StateDash = 8;
        private const int StateBarrage = 11;
        private const int StateSpiral = 12;

        // Attack cycles (vanilla AI_120 hub selection): phase 1 is the
        // 10-slot Classic cycle; phase 2 is the 10-slot Expert cycle
        // (Classic has 9 slots and no barrage — the minion uses the
        // Expert cycle so the barrage appears).
        private static readonly int[] CyclePhase1 = { 2, 8, 6, 8, 5, 2, 8, 4, 8, 5 };
        private static readonly int[] CyclePhase2 = { 7, 2, 8, 11, 5, 2, 6, 4, 8, 12 };

        // Durations in ticks (vanilla Classic phase-1 values, num17 = 0).
        private const int HubDurationP1 = 45;
        private const int HubDurationP2 = 20;
        private const int BoltsDuration = 150;
        private const int LancesDuration = 120;
        private const int RainbowDuration = 72;
        private const int SunDanceDuration = 300;
        private const int WallsDuration = 260;
        private const int DashDuration = 110;
        private const int BarrageDuration = 120;
        private const int SpiralDuration = 150;

        private const int Phase2Age = 1800; // 30 s: phase 2 begins (minions have no life fractions)

        // Idle hover offset, relative to the player — distinct per boss so
        // the charm's minions don't all stack on one point. (Other bosses
        // already use (380,-120), (0,-240), (140,-140), (-140,-180),
        // (220,-260), (0,-380), (-220,-260); this one is far left.)
        private static readonly Vector2 IdleOffset = new Vector2(-360f, -80f);

        // --- State (stateTimer/cycleIndex/targetWho are owner-side only;
        // clients don't run minion AI, so they need no sync) ---
        private int stateTimer = 0;
        private bool stateChanged = false;
        private int cycleIndex = 0;
        private int targetWho = -1;
        private int dashDir = 1; // +1 = dash right, -1 = dash left
        private bool initialized = false;
        private int age;
        private bool phase2Entered;

        public override string Texture => "Terraria/Images/NPC_636";

        public override void SetStaticDefaults()
        {
            // The Empress texture has 2 frames: 0 = phase 1, 1 = phase 2
            // (NPC FindFrame: frame.Y = IsInPhase2() ? frameHeight : 0).
            Main.projFrames[Type] = 2;
        }

        public override void SetDefaults()
        {
            // Vanilla hitbox 100x100, scaled to 50% (the charm's boss scale).
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
                // Spawn intro sound (vanilla case 0 plays Item161 at tick 10).
                SoundEngine.PlaySound(SoundID.Item161, Projectile.Center);
            }

            age++;

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

            // Phase 2 begins at 30 s. Vanilla's phase transition (case 10)
            // resets the cycle counter, so the first phase-2 attack is
            // always the walls (slot 0 of the phase-2 cycle).
            bool phase2 = age >= Phase2Age;
            if (phase2 && !phase2Entered)
            {
                phase2Entered = true;
                cycleIndex = 0;
            }

            // The frame is the persistent phase frame (vanilla FindFrame
            // 636: phase 2 ? 1 : 0) — not a per-attack frame.
            int state = (int)Projectile.ai[0];
            Projectile.frame = phase2 ? 1 : 0;

            // Losing the target mid-attack returns to idle; a fresh attack
            // starts when a new target is found.
            if (!hasTarget && state != StateIdle)
            {
                SetState(StateIdle);
                state = StateIdle;
            }

            switch (state)
            {
                case StateIdle: UpdateIdle(player, target); break;
                case StateHub: UpdateHub(target, phase2); break;
                case StateBolts: UpdateBolts(target); break;
                case StateLances: UpdateLances(target); break;
                case StateRainbow: UpdateRainbow(target); break;
                case StateSunDance: UpdateSunDance(target); break;
                case StateWalls: UpdateWalls(target); break;
                case StateDash: UpdateDash(target); break;
                case StateBarrage: UpdateBarrage(target); break;
                case StateSpiral: UpdateSpiral(target); break;
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
            // The Empress texture is a sprite sheet with 2 frames stacked
            // vertically (frame count read from the game's own
            // npcFrameCount table in the binary). The count is HARDCODED
            // rather than relying on the default draw's projFrames lookup,
            // so the frame is always drawn correctly. Projectile.frame is
            // the persistent phase frame (vanilla FindFrame logic: frame 0
            // phase 1, frame 1 phase 2). Never flipped, never rotated:
            // vanilla AI_120 never sets spriteDirection or rotation.
            const int frameCount = 2;
            Texture2D texture = TextureAssets.Projectile[Projectile.type].Value;
            int frameHeight = texture.Height / frameCount;
            int frameIndex = (int)MathHelper.Clamp(Projectile.frame, 0, frameCount - 1);
            Rectangle frame = new Rectangle(0, frameIndex * frameHeight, texture.Width, frameHeight);
            Vector2 origin = frame.Size() / 2f;
            Main.EntitySpriteDraw(texture, Projectile.Center - Main.screenPosition, frame, lightColor, Projectile.rotation, origin, Projectile.scale, SpriteEffects.None, 0);
            return false;
        }

        private void UpdateIdle(Player player, NPC target)
        {
            if (target != null)
            {
                // Found an enemy: dash in via the hub, like the real boss.
                SetState(StateHub);
                return;
            }
            FlyTo(player.Center + IdleOffset, 10f, 0.08f);
        }

        private void UpdateHub(NPC target, bool phase2)
        {
            // Vanilla case 1: DashTo a point 300px above the target for the
            // first 11 ticks (ai[1] <= 10), then coast down. 45 ticks in
            // phase 1, 20 in phase 2, then pick the next attack from the
            // current phase's cycle.
            if (stateTimer <= 10)
            {
                Vector2 targetPoint = target.Center + new Vector2(0f, -300f);
                float dist = Vector2.Distance(Projectile.Center, targetPoint);
                if (dist > 200f)
                    targetPoint -= (targetPoint - Projectile.Center).SafeNormalize(Vector2.UnitY) * 100f;
                float lerp = Utils.GetLerpValue(100f, 600f, dist, true);
                // Vanilla DashTo: velocity = Lerp(dash, glide, lerp) —
                // close in she dashes at up to 18, far away she glides
                // at (target - Center) / 6 (fast when far).
                Vector2 glide = (targetPoint - Projectile.Center) / 6f;
                Vector2 dash = (targetPoint - Projectile.Center).SafeNormalize(Vector2.UnitY) * Math.Min(dist, 18f);
                Projectile.velocity = Vector2.Lerp(dash, glide, lerp);
            }
            else
            {
                if (Projectile.velocity.Length() > 16f)
                    Projectile.velocity /= 2f;
                Projectile.velocity *= 0.92f;
            }

            int hubDuration = phase2 ? HubDurationP2 : HubDurationP1;
            if (stateTimer >= hubDuration)
            {
                int[] cycle = phase2 ? CyclePhase2 : CyclePhase1;
                int next = cycle[cycleIndex % cycle.Length];
                cycleIndex++;
                SetState(next);
            }
        }

        private void UpdateBolts(NPC target)
        {
            // Vanilla case 2 (Prismatic Bolts): hover at target + (-150,
            // -250); fire 20 Rainbow Streaks (873), one every 3 ticks
            // during ticks 0-59. The vanilla 873 already supports friendly
            // homing at NPCs — ai[0] is the target NPC index (verified in
            // AI_171: the friendly branch homes at Main.npc[ai[0]]).
            FlyTo(target.Center + new Vector2(-150f, -250f), 12f, 0.08f);

            if (stateTimer == 0)
                SoundEngine.PlaySound(SoundID.Item164, Projectile.Center);

            if (stateTimer < 60 && stateTimer % 3 == 0)
            {
                Vector2 vel = new Vector2(0f, -6f).RotatedBy(MathHelper.PiOver2 * Main.rand.NextFloat(-1f, 1f));
                SpawnFriendly(ProjectileID.HallowBossRainbowStreak, Projectile.Center + new Vector2(-55f, -30f), vel, targetWho, stateTimer / 60f);
                // Magic effect type 1 (simplified): small sparkle at the hand.
                Dust.NewDust(Projectile.Center + new Vector2(-55f, -30f), 4, 4, 267, 0f, 0f, 0, default, 0.5f);
            }

            if (stateTimer >= BoltsDuration)
                SetState(StateHub);
        }

        private void UpdateLances(NPC target)
        {
            // Vanilla case 4 (Ethereal Lances): hover at target + (0, -350);
            // 25 FairyQueenLances (919), one every 4 ticks during ticks
            // 0-99, from a ring around the target, aimed at its predicted
            // position (90-tick lead) with the side-flip rule.
            FlyTo(target.Center + new Vector2(0f, -350f), 12f, 0.08f);

            if (stateTimer == 0)
                SoundEngine.PlaySound(SoundID.Item162, Projectile.Center);

            if (stateTimer < 100 && stateTimer % 4 == 0)
            {
                int k = stateTimer / 4; // lance index 0-24
                float angle = MathHelper.Pi / 8f + k * (MathHelper.Pi / 4f);
                Vector2 dir = angle.ToRotationVector2();
                // Classic wider fan: DETERMINISTIC +/-0.5 on X (away from
                // zero), not random — vanilla adds +0.5 if X > 0 else -0.5.
                dir.X += dir.X > 0f ? 0.5f : -0.5f;
                dir = dir.SafeNormalize(Vector2.UnitY);

                // Side-flip: if the target is moving toward the spawn side,
                // spawn on the opposite side instead. Vanilla uses
                // SafeNormalize with NO magnitude threshold (a zero
                // velocity falls back to UnitY); the old >0.5 check and
                // raw Normalize risked a NaN on a still target.
                if (Vector2.Dot(target.velocity.SafeNormalize(Vector2.UnitY), dir) > 0f)
                    dir = -dir;

                Vector2 spawn = target.Center + dir * 300f - target.velocity * 30f;
                // Renormalize to 300 ONLY if the velocity drag pulled the
                // spawn inside the ring (vanilla's conditional).
                if (Vector2.Distance(spawn, target.Center) < 300f)
                    spawn = target.Center + (spawn - target.Center).SafeNormalize(Vector2.UnitY) * 300f;

                Vector2 predicted = target.Center + target.velocity * 90f;
                float aim = (predicted - spawn).ToRotation();
                SpawnFriendly(ProjectileID.FairyQueenLance, spawn, Vector2.Zero, aim, stateTimer / 100f);
            }

            if (stateTimer >= LancesDuration)
                SetState(StateHub);
        }

        private void UpdateRainbow(NPC target)
        {
            // Vanilla case 5 (Everlasting Rainbow): hover at target +
            // (0, -350); at tick 0 only, 13 Lasting Rainbows (872) in a
            // full circle from the right hand. No targeting involved.
            FlyTo(target.Center + new Vector2(0f, -350f), 12f, 0.08f);

            if (stateTimer == 0)
            {
                SoundEngine.PlaySound(SoundID.Item163, Projectile.Center);
                float randomPhase = Main.rand.NextFloat() * MathHelper.TwoPi;
                Vector2 hand = Projectile.Center + new Vector2(55f, -30f);
                for (int k = 0; k < 13; k++)
                {
                    float frac = k / 13f;
                    Vector2 dir = Vector2.UnitY.RotatedBy(MathHelper.PiOver2 + MathHelper.TwoPi * frac + randomPhase);
                    Vector2 spawn = hand + dir.RotatedBy(-MathHelper.PiOver2) * 30f;
                    SpawnFriendly(ProjectileID.HallowBossLastingRainbow, spawn, dir * 8f, 0f, frac);
                }
            }

            if (stateTimer >= RainbowDuration)
                SetState(StateHub);
        }

        private void UpdateSunDance(NPC target)
        {
            // SUBSTITUTION (see class doc): vanilla FairyQueenSunDance (923)
            // dies without a type-636 NPC anchor. The minion fires a spiral
            // of friendly HallowBossRainbowStreaks (873) in the same 3-wave
            // pattern instead — the prismatic bolts home at the enemy like
            // the dance's beams sweep it. Hover slowly toward
            // target + (-80, -500).
            FlyTo(target.Center + new Vector2(-80f, -500f), 3.6f, 0.05f);

            if (stateTimer % 60 == 0 && stateTimer < 180)
            {
                // Vanilla phase: (k/6 + 0.5/6 + side*0.5/6) — the offset
                // depends on which SIDE the target is on (1 if the target
                // is to the right), NOT on the wave number; all three
                // waves share the same phase pattern.
                int side = target.Center.X > Projectile.Center.X ? 1 : 0;
                SoundEngine.PlaySound(SoundID.Item159, Projectile.Center);
                for (int k = 0; k < 6; k++)
                {
                    float phase = (k / 6f + 0.5f / 6f + side * 0.5f / 6f) % 1f;
                    Vector2 dir = (MathHelper.TwoPi * phase).ToRotationVector2();
                    SpawnFriendly(ProjectileID.HallowBossRainbowStreak, Projectile.Center + new Vector2(0f, -100f), dir * 8f, targetWho, phase);
                }
            }

            if (stateTimer >= SunDanceDuration)
                SetState(StateHub);
        }

        private void UpdateWalls(NPC target)
        {
            // Vanilla case 7 (Lance Walls, phase 2 only in the boss fight):
            // 4 volleys x 14 lances (classic), one volley every 60 ticks.
            // Walls are built relative to the target enemy.
            FlyTo(target.Center + new Vector2(0f, -350f), 4.8f, 0.06f);

            if (stateTimer % 60 == 0 && stateTimer < 240)
            {
                int volley = stateTimer / 60;
                SoundEngine.PlaySound(SoundID.Item162, Projectile.Center);
                SpawnLanceWall(target, volley);
            }

            if (stateTimer >= WallsDuration)
                SetState(StateHub);
        }

        private void SpawnLanceWall(NPC target, int volley)
        {
            // Vanilla case 7 geometry (num76 = 13*150 = 1950, num77 = 0.4,
            // num78 = 1.4): each wall is 14 lances (num80 = k/13) placed
            // at origin + step * (num80 - 0.5), so the wall is CENTERED
            // on its anchor — the horizontal walls start 1365 px left of
            // where a naive anchor + k*step would put them.
            const int count = 14;
            for (int k = 0; k < count; k++)
            {
                Vector2 spawn;
                float aim;
                float along = k / (float)(count - 1);
                switch (volley)
                {
                    case 0: // vertical line left of the target, firing right
                        spawn = new Vector2(target.Center.X - 975f, target.Center.Y - 975f + k * 150f);
                        aim = 0f;
                        break;
                    case 1: // vertical line right of the target, firing left
                        spawn = new Vector2(target.Center.X + 975f, target.Center.Y - 900f + k * 150f);
                        aim = MathHelper.Pi;
                        break;
                    case 2: // horizontal line above, firing down-right
                        // Anchor (-780, -780), step +210 in X, centered:
                        // X runs target.X - 2145 .. +585.
                        spawn = new Vector2(target.Center.X - 2145f + k * 210f, target.Center.Y - 780f);
                        aim = MathHelper.PiOver4;
                        break;
                    default: // horizontal line above, firing down-left
                        // Anchor (+855, -780), step -210 in X, centered:
                        // X runs target.X + 2220 .. -510.
                        spawn = new Vector2(target.Center.X + 2220f - k * 210f, target.Center.Y - 780f);
                        aim = MathHelper.PiOver4 * 3f;
                        break;
                }
                SpawnFriendly(ProjectileID.FairyQueenLance, spawn, Vector2.Zero, aim, along);
            }
        }

        private void UpdateDash(NPC target)
        {
            // Vanilla cases 8/9 (horizontal dash): telegraph (fly to the far
            // side, 40 ticks), then a 50-speed horizontal ram through the
            // target (ticks 41-90, velocity *= 0.7 at tick 90). The minion's
            // own contact damage (6000) is the ram damage.
            if (stateTimer == 0)
            {
                // Pick the side: start on the far side, cross through the
                // target (vanilla: an 8 becomes a 9 when the target is to
                // the right of the boss).
                dashDir = target.Center.X >= Projectile.Center.X ? 1 : -1;
            }

            if (stateTimer < 40)
            {
                Vector2 side = target.Center + new Vector2(-550f * dashDir, 0f);
                FlyTo(side, 12f, 0.15f);
            }
            else if (stateTimer == 40)
            {
                Projectile.velocity *= 0.3f;
            }
            else if (stateTimer <= 90)
            {
                Projectile.velocity = Vector2.Lerp(Projectile.velocity, new Vector2(50f * dashDir, 0f), 0.05f);
                if (stateTimer == 90)
                    Projectile.velocity *= 0.7f;
                // Dash trail dust (magic effect type 5, simplified).
                if (Main.rand.Next(2) == 0)
                    Dust.NewDust(Projectile.Center, 10, 10, 267, 0f, 0f, 0, default, 1f);
            }
            else
            {
                Projectile.velocity *= 0.92f;
            }

            if (stateTimer == 20)
                SoundEngine.PlaySound(SoundID.Item160, Projectile.Center);

            if (stateTimer >= DashDuration)
                SetState(StateHub);
        }

        private void UpdateBarrage(NPC target)
        {
            // Vanilla case 11 (Targeted Lance Barrage, phase 2 expert only
            // in the boss fight): 34 lances, one every 3 ticks over 100
            // ticks, each spawning 100px behind the target's movement and
            // aimed at its predicted position (90-tick lead).
            FlyTo(target.Center + new Vector2(0f, -350f), 12f, 0.08f);

            if (stateTimer == 0)
                SoundEngine.PlaySound(SoundID.Item162, Projectile.Center);

            if (stateTimer < 100 && stateTimer % 3 == 0)
            {
                // Vanilla: v = -velocity, SafeNormalize(-UnitY) — no
                // magnitude threshold; a still target falls back to -UnitY.
                Vector2 behind = (-target.velocity).SafeNormalize(-Vector2.UnitY);
                Vector2 spawn = target.Center + behind * 100f;
                Vector2 predicted = target.Center + target.velocity * 90f;
                float aim = (predicted - spawn).ToRotation();
                SpawnFriendly(ProjectileID.FairyQueenLance, spawn, Vector2.Zero, aim, stateTimer / 100f);
            }

            if (stateTimer >= BarrageDuration)
                SetState(StateHub);
        }

        private void UpdateSpiral(NPC target)
        {
            // Vanilla case 12 (Bolt Spiral, phase 2 only in the boss fight):
            // rise on entry (velocity (0, -12), decaying *0.95 every tick
            // — she does NOT fly anywhere during the spiral), then 8
            // Rainbow Streaks (classic), one every 6 ticks during ticks
            // 10-59 — firing when ai[1] % 6 == 0, i.e. ticks 12, 18, ...,
            // 54 — velocity rotating a full 360 degrees.
            if (stateTimer == 0)
            {
                SoundEngine.PlaySound(SoundID.Item165, Projectile.Center);
                Projectile.velocity = new Vector2(0f, -12f);
            }

            Projectile.velocity *= 0.95f;

            if (stateTimer >= 10 && stateTimer < 60 && stateTimer % 6 == 0)
            {
                float progress = (stateTimer - 10) / 50f;
                Vector2 vel = new Vector2(0f, -20f).RotatedBy(MathHelper.TwoPi * progress);
                SpawnFriendly(ProjectileID.HallowBossRainbowStreak, Projectile.Center + new Vector2(-55f, -30f), vel, targetWho, progress);
            }

            if (stateTimer >= SpiralDuration)
                SetState(StateHub);
        }

        // --- Helpers ---

        private void SetState(int newState)
        {
            Projectile.ai[0] = newState;
            stateTimer = 0;
            stateChanged = true;
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
        /// the owner.
        /// </summary>
        private int SpawnFriendly(int type, Vector2 center, Vector2 velocity, float ai0 = 0f, float ai1 = 0f, float ai2 = 0f)
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
            spawned.netUpdate = true;
            return p;
        }
    }
}
