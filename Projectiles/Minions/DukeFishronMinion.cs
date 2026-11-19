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
    /// Duke Fishron as a Brigitte's Charm boss minion (2026-11-15). His real
    /// attack cycle, retargeted at enemies: charges, the Detonating Bubble
    /// volley, the Sharknado summon (substituted — see below), and in
    /// phase 2 the bubble spiral and the homing Sharknado. Half scale,
    /// flat 6000 typeless damage, local immunity frames.
    ///
    /// Attack data from the decompiled Terraria 1.4.4.9 NPC AI (aiStyle 69):
    /// see ~/workspace/tmp/boss_attacks_batch1.md, Duke Fishron section.
    ///
    /// STATE MAP (vanilla ai[0], verified against AI_069 + FindFrame 370
    /// in the decompile — the v1 file had states 2 and 3 swapped):
    /// - ai[0] 2 = Detonating Bubbles: 80 ticks, one NPC 371 bubble every
    ///   4 ticks, drifting at the hover point (target + (+/-300, -200),
    ///   speed 5, accel 0.3). Pose frames 6/7 (frame 6 while ai[2] &lt; 10).
    /// - ai[0] 3 = Sharknado summon: 90 ticks, decelerating; at tick 60
    ///   two SharknadoBolts (385) launch from the snout. Swim loop
    ///   outside ticks 60-80, pose frame 6 in 60-80, frame 7 in 71-74.
    /// - ai[0] 7 = bubble spiral (phase 2): 120 ticks, one NPC 371 bubble
    ///   every 4 ticks ejected perpendicular at speed 6 while the
    ///   velocity rotates 2*pi/60 rad/tick. Pose frames 6/7.
    /// - ai[0] 8 = homing Sharknado (phase 2): 90 ticks, decelerating; at
    ///   tick 60 one homing SharknadoBolt (385) fires. Same frames as
    ///   ai[0] 3. Cycle: phase 1 charges until ai[3] 10 -&gt; bubbles,
    ///   11 -&gt; sharknado; phase 2 charges until 6 -&gt; spiral,
    ///   7 -&gt; homing.
    ///
    /// SUBSTITUTIONS (vanilla Sharknado content cannot be reused):
    /// - Detonating Bubbles (NPC 371) and spiral: FlaironBubble (405) —
    ///   the Flairon's friendly homing bubbles, which home at enemies
    ///   and burst on contact. Same timings (every 4 ticks, 20/30 total),
    ///   same snout/perpendicular launches.
    /// - Sharknado summon (2x SharknadoBolt 385): MiniSharkron (408) x2 —
    ///   the Tempest Staff's friendly homing sharks — on the same tick-60
    ///   volley with the same drift-apart launch. (The SharknadoBolt
    ///   death-spawns a hostile tornado (384) whose aiStyle 64 sheds
    ///   hostile Sharkron NPCs (372/373); a friendly port would attack
    ///   the player.)
    /// - Homing bolt (phase 2): a single MiniSharkron (408) — a friendly
    ///   homing shark — in place of the player-targeting homing
    ///   SharknadoBolt (whose aiStyle 65 reads a PLAYER index and has no
    ///   enemy equivalent).
    ///
    /// SIMPLIFICATIONS (v1):
    /// - Phase 2 begins after 30 s active (minions have no life fractions);
    ///   the cycle counter resets on the phase change, like vanilla.
    /// - No phase 3 (Expert-only teleport cycle) and no enrage (ocean-surface
    ///   checks have no minion meaning). No phase-transition invulnerability
    ///   pauses. The dead ai[0]==13 spiral is not implemented.
    /// - The flag6 land-check (player near the world edge/top skips the
    ///   bubbles and jumps to the sharknado, entering it at tick 50) is
    ///   not ported — position checks have no minion meaning.
    ///
    /// SOUNDS: Zombie_20 roars (bubble volley / spiral / homing-bolt
    /// moments), Zombie_9 roar (sharknado volley), SoundID.NPCDeath19 on
    /// every bubble spawn — the exact sounds and moments from the
    /// decompiled AI, using SoundStyle paths because tModLoader removed
    /// the legacy int overloads. Charges and hovers are silent in vanilla
    /// and stay silent here.
    /// </summary>
    public class DukeFishronMinion : ModProjectile
    {
        public override string Texture => "Terraria/Images/NPC_370";
        // DisplayName comes from the localization file
        // (Mods.CelestialAerus.Projectiles.DukeFishronMinion.DisplayName).

        // --- Constants (vanilla aiStyle 69 values, Classic) ---
        private const int MinionDamage = 6000;
        private const float TargetRange = 1000f;

        // States (Projectile.ai[0]); ai[1] is the state timer, ai[2] the cycle counter.
        private const int StateHover = 0;
        private const int StateCharge = 1;
        private const int StateBubbleVolley = 2;  // Detonating Bubbles (vanilla ai[0] 2)
        private const int StateSharknado = 3;     // Sharknado summon substitute (vanilla ai[0] 3)
        private const int StateSpiral = 4;        // phase 2 bubble spiral (vanilla ai[0] 7)
        private const int StateHomingShark = 5;   // phase 2 homing bolt substitute (vanilla ai[0] 8)

        // Hover (vanilla: 60 ticks; 30 early in phase 1, 20 early in phase 2;
        // speed 7.5, accel 0.45 — 8 / 0.5 early in phase 2).
        private const int HoverTime = 60;
        private const int HoverTimeEarlyP1 = 30;
        private const int HoverTimeEarlyP2 = 20;
        private const float HoverSpeed = 7.5f;
        private const float HoverAccel = 0.45f;
        private const float HoverSpeedEarlyP2 = 8f;
        private const float HoverAccelEarlyP2 = 0.5f;

        // Charge (vanilla: speed 16, 30 ticks, straight line, no steering).
        private const float ChargeSpeed = 16f;
        private const int ChargeTime = 30;

        // Bubble volley (vanilla Detonating Bubbles: 80 ticks, one bubble
        // every 4 ticks = 20 total, drifting at the hover point).
        private const int BubbleVolleyTime = 80;
        private const int BubbleSpawnInterval = 4;
        private const float BubbleDriftSpeed = 5f;
        private const float BubbleDriftAccel = 0.3f;

        // Sharknado (vanilla: 90 ticks, 2-bolt volley at tick 60).
        private const int SharknadoTime = 90;
        private const int SharknadoFireTick = 60;

        // Spiral (vanilla: 120 ticks, one bubble every 4 ticks = 30 total;
        // velocity rotates 2*pi/60 rad/tick — one full loop per 60 ticks).
        private const int SpiralTime = 120;
        private const int SpiralSpawnInterval = 4;
        private const float SpiralEntrySpeed = 20f;
        private const float SpiralTurnRate = 0.1047f; // 2*pi/60
        private const float SpiralEjectSpeed = 6f;

        // Homing shark (vanilla homing bolt: 90 ticks, fires at tick 60).
        private const int HomingSharkTime = 90;
        private const int HomingSharkFireTick = 60;

        private const int Phase2Age = 1800; // 30 s

        // Distinct hover offset so the boss minions don't stack on one point.
        private static readonly Vector2 HoverOffset = new Vector2(0f, -240f);

        // Roars (legacy int overloads are gone in tModLoader — use SoundStyle paths).
        private static readonly SoundStyle RoarSound = new SoundStyle("Terraria/Sounds/Zombie_20");
        private static readonly SoundStyle SharknadoRoarSound = new SoundStyle("Terraria/Sounds/Zombie_9");

        // --- State (fields; only the owner makes spawn decisions) ---
        private int age;
        private bool phase2Entered;
        private int hoverSide = 1; // +/-1: which side of the target to hover on
        private Vector2 chargeTarget;

        public override void SetDefaults()
        {
            // Hitbox matches the 150x100 vanilla hitbox at 0.5 scale.
            Projectile.width = 75;
            Projectile.height = 50;
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
                SetState(StateHover);
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
            if (phase2 && !phase2Entered)
            {
                // Phase change resets the cycle counter, like vanilla.
                phase2Entered = true;
                Projectile.ai[2] = 0;
            }

            int state = (int)Projectile.ai[0];
            int stateTimer = (int)Projectile.ai[1];
            int cycle = (int)Projectile.ai[2];
            Projectile.ai[1]++;

            // Face the target while hovering; face the velocity while charging.
            if (state == StateHover && target != null)
            {
                // Vanilla sets the hover side ONCE at hover entry
                // (ai[1] = 300 * sign(center - target) when ai[1] == 0);
                // it does not flip mid-hover if the target crosses over.
                if (stateTimer == 0)
                    hoverSide = Projectile.Center.X >= target.Center.X ? 1 : -1;
                FaceDirection(target.Center - Projectile.Center);
            }
            else if (state == StateCharge || state == StateSpiral)
            {
                if (Projectile.velocity.LengthSquared() > 1f)
                    FaceDirection(Projectile.velocity);
            }

            // Animation (vanilla FindFrame, type 370 — 8 frames). Swim loop
            // 0-5 (6 ticks/frame phase 1, 5 in phase 2) while hovering and
            // during the sharknado/homing states outside their pose
            // windows; charge/bubble/spiral states hold pose frame 6 for
            // the first 10 ticks, then frame 7.
            int swimLimit = phase2 ? 4 : 5;
            switch (state)
            {
                case StateCharge:
                case StateBubbleVolley:
                case StateSpiral:
                    // Vanilla ai[0] 1/6 (charge), 2 (bubbles), 7 (spiral):
                    // frame 6 while ai[2] < 10, frame 7 after.
                    Projectile.frame = stateTimer < 10 ? 6 : 7;
                    break;
                case StateSharknado:
                case StateHomingShark:
                    // Vanilla ai[0] 3/8: swim cycle outside ticks 60-80,
                    // frame 6 during 60-80, frame 7 during 71-74.
                    if (stateTimer >= 60 && stateTimer <= 80)
                        Projectile.frame = (stateTimer > 70 && stateTimer < 75) ? 7 : 6;
                    else
                        AdvanceSwimCycle(swimLimit);
                    break;
                default: // Hover: swim loop.
                    AdvanceSwimCycle(swimLimit);
                    break;
            }

            switch (state)
            {
                case StateHover:
                    // Hover point: 300 px to the side and 200 px above the
                    // target (vanilla: player.Center + (+/-300, -200)).
                    Vector2 hoverPoint = target != null
                        ? target.Center + new Vector2(hoverSide * 300f, -200f)
                        : player.Center + HoverOffset;
                    // Early in the cycle the hover is shorter and (phase 2)
                    // faster (vanilla: phase 1 with ai[3] < 10, phase 2
                    // with ai[3] < 6).
                    bool early = (!phase2 && cycle < 10) || (phase2 && cycle < 6);
                    int hoverTime = early ? (phase2 ? HoverTimeEarlyP2 : HoverTimeEarlyP1) : HoverTime;
                    float hoverSpeed = (early && phase2) ? HoverSpeedEarlyP2 : HoverSpeed;
                    float hoverAccel = (early && phase2) ? HoverAccelEarlyP2 : HoverAccel;
                    HoverToward(hoverPoint, hoverSpeed, hoverAccel);

                    if (target == null)
                    {
                        // No target: hold the hover, don't advance the cycle.
                        Projectile.ai[1] = 0;
                        break;
                    }

                    // Only the owner picks the next attack; the state change
                    // syncs to other clients via netUpdate.
                    if (Projectile.owner != Main.myPlayer)
                        break;

                    if (stateTimer < hoverTime)
                        break;

                    // Pick the next attack from the cycle.
                    // Phase 1: 5 charges -> bubble volley -> 5 charges -> sharknado.
                    // Phase 2: 3 charges -> spiral -> 3 charges -> homing shark.
                    if (!phase2)
                    {
                        if (cycle == 10)
                        {
                            Projectile.ai[2] = 1;
                            SoundEngine.PlaySound(RoarSound, Projectile.Center);
                            SetState(StateBubbleVolley);
                        }
                        else if (cycle == 11)
                        {
                            Projectile.ai[2] = 0;
                            SetState(StateSharknado);
                        }
                        else
                        {
                            Projectile.ai[2] = cycle + 2;
                            StartCharge(target);
                        }
                    }
                    else
                    {
                        if (cycle == 6)
                        {
                            Projectile.ai[2] = 1;
                            SoundEngine.PlaySound(RoarSound, Projectile.Center);
                            SetState(StateSpiral);
                            // Spiral entry: straight at the target at speed 20.
                            Projectile.velocity = (target.Center - Projectile.Center).SafeNormalize( Vector2.UnitX) * SpiralEntrySpeed;
                        }
                        else if (cycle == 7)
                        {
                            Projectile.ai[2] = 0;
                            SetState(StateHomingShark);
                        }
                        else
                        {
                            Projectile.ai[2] = cycle + 2;
                            StartCharge(target);
                        }
                    }
                    break;

                case StateCharge:
                    // Straight line, no steering — velocity was set on entry.
                    // Bubble dust along the body (vanilla: 7/tick of type 172;
                    // 3/tick here to keep the dust load reasonable).
                    for (int i = 0; i < 3; i++)
                    {
                        Dust.NewDust(Projectile.position, Projectile.width, Projectile.height, 172,
                            -Projectile.velocity.X * 0.05f, -Projectile.velocity.Y * 0.05f);
                    }
                    if (stateTimer >= ChargeTime)
                        SetState(StateHover);
                    break;

                case StateBubbleVolley:
                    // Drift at the hover point (vanilla: target + (+/-300,
                    // -200), speed 5, accel 0.3), reusing the hover side
                    // chosen at the last hover entry.
                    if (target != null)
                        HoverToward(target.Center + new Vector2(hoverSide * 300f, -200f), BubbleDriftSpeed, BubbleDriftAccel);
                    // One bubble every 4 ticks, 20 total (vanilla
                    // Detonating Bubbles, NPC 371).
                    if (Projectile.owner == Main.myPlayer && target != null
                        && stateTimer % BubbleSpawnInterval == 0 && stateTimer < BubbleVolleyTime)
                    {
                        SpawnBubble(target);
                        SoundEngine.PlaySound(SoundID.NPCDeath19, Projectile.Center);
                    }
                    if (stateTimer >= BubbleVolleyTime)
                        SetState(StateHover);
                    break;

                case StateSharknado:
                    // Decelerate while firing (vanilla: velocity *= 0.98,
                    // velocity.Y lerps to 0 at 0.02/tick).
                    Projectile.velocity *= 0.98f;
                    Projectile.velocity.Y = MathHelper.Lerp(Projectile.velocity.Y, 0f, 0.02f);
                    if (Projectile.owner == Main.myPlayer && target != null
                        && stateTimer == SharknadoFireTick)
                    {
                        // Two sharks from the snout, drifting apart/down —
                        // the vanilla (direction*2, 8) / (-direction*2, 8)
                        // SharknadoBolt launch.
                        SoundEngine.PlaySound(SharknadoRoarSound, Projectile.Center);
                        Vector2 muzzle = SnoutPosition(target);
                        int dir = Projectile.spriteDirection;
                        SpawnFriendlyProjectile(muzzle, new Vector2(2f * dir, 8f), ProjectileID.MiniSharkron);
                        SpawnFriendlyProjectile(muzzle, new Vector2(-2f * dir, 8f), ProjectileID.MiniSharkron);
                    }
                    if (stateTimer >= SharknadoTime)
                        SetState(StateHover);
                    break;

                case StateSpiral:
                    // Circle: rotate the velocity every tick (one full loop
                    // per 60 ticks, two loops over the attack).
                    Projectile.velocity = Projectile.velocity.RotatedBy(-SpiralTurnRate * Projectile.direction);
                    // One bubble every 4 ticks, 30 total, ejected perpendicular
                    // to the flight direction at speed 6 (vanilla Detonating
                    // Bubbles, NPC 371).
                    if (Projectile.owner == Main.myPlayer && target != null
                        && stateTimer % SpiralSpawnInterval == 0 && stateTimer < SpiralTime)
                    {
                        Vector2 ejectDir = Vector2.Normalize(Projectile.velocity)
                            .RotatedBy(MathHelper.PiOver2 * Projectile.direction);
                        Vector2 pos = Projectile.Center
                            + Vector2.Normalize(Projectile.velocity) * (Projectile.width / 2f + 10f)
                            + new Vector2(0f, 22f);
                        SpawnFriendlyProjectile(pos, ejectDir * SpiralEjectSpeed, ProjectileID.FlaironBubble);
                        SoundEngine.PlaySound(SoundID.NPCDeath19, Projectile.Center);
                    }
                    if (stateTimer >= SpiralTime)
                        SetState(StateHover);
                    break;

                case StateHomingShark:
                    // Decelerate while firing, like the sharknado summon
                    // (vanilla: velocity *= 0.98, velocity.Y lerps to 0 at
                    // 0.02/tick).
                    Projectile.velocity *= 0.98f;
                    Projectile.velocity.Y = MathHelper.Lerp(Projectile.velocity.Y, 0f, 0.02f);
                    if (Projectile.owner == Main.myPlayer && target != null
                        && stateTimer == HomingSharkFireTick)
                    {
                        // Single homing shark (vanilla: one homing bolt).
                        SoundEngine.PlaySound(RoarSound, Projectile.Center);
                        SpawnShark(target, snout: false);
                    }
                    if (stateTimer >= HomingSharkTime)
                        SetState(StateHover);
                    break;
            }
        }

        // Vanilla swim cycle (FindFrame, type 370): frameCounter advances
        // past the limit, then the frame steps, wrapping 6 -> 0. The
        // counter is shared across states and never reset, so entering
        // the swim cycle from a pose frame (6/7) holds that frame until
        // the next advance, like vanilla.
        private void AdvanceSwimCycle(int limit)
        {
            Projectile.frameCounter++;
            if (Projectile.frameCounter > limit)
            {
                Projectile.frameCounter = 0;
                Projectile.frame++;
                if (Projectile.frame >= 6)
                    Projectile.frame = 0;
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            // The Fishron texture is a sprite sheet with 8 frames stacked
            // vertically (frame count read from the game's own
            // npcFrameCount table in the binary). The count is HARDCODED:
            // Main.npcFrameCount[type] is not reliable at runtime for NPCs
            // that have never spawned, and a 0/1 fallback draws the raw
            // sprite sheet. Projectile.frame is animated in AI() (vanilla
            // FindFrame logic: swim loop 0-5, pose frames 6-7).
            const int frameCount = 8;
            Texture2D texture = TextureAssets.Projectile[Projectile.type].Value;
            int frameHeight = texture.Height / frameCount;
            int frameIndex = (int)MathHelper.Clamp(Projectile.frame, 0, frameCount - 1);
            Rectangle frame = new Rectangle(0, frameIndex * frameHeight, texture.Width, frameHeight);
            Vector2 origin = frame.Size() / 2f;
            SpriteEffects effects = Projectile.spriteDirection == -1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            Main.EntitySpriteDraw(texture, Projectile.Center - Main.screenPosition, frame, lightColor, Projectile.rotation, origin, Projectile.scale, effects, 0);
            return false;
        }

        private void StartCharge(NPC target)
        {
            // Lock the target's position at charge start (vanilla: no tracking
            // during the charge) and launch in a straight line at speed 16.
            chargeTarget = target.Center;
            Projectile.velocity = (chargeTarget - Projectile.Center).SafeNormalize( Vector2.UnitX) * ChargeSpeed;
            Projectile.direction = Projectile.velocity.X >= 0f ? 1 : -1;
            SetState(StateCharge);
        }

        private void SpawnBubble(NPC target)
        {
            // SUBSTITUTION: FlaironBubble (405) — the Flairon's friendly
            // homing bubble — stands in for the Detonating Bubble NPC
            // (371). Launched from the snout toward the target at a
            // gentle speed 8 (the vanilla bubble spawns with no velocity
            // and floats up; the Flairon bubble homes from here).
            Vector2 pos = SnoutPosition(target);
            Vector2 aim = target.Center + new Vector2(Main.rand.Next(-100, 101), Main.rand.Next(-100, 101));
            Vector2 vel = (aim - pos).SafeNormalize( Vector2.UnitX) * 8f;
            SpawnFriendlyProjectile(pos, vel, ProjectileID.FlaironBubble);
        }

        private void SpawnShark(NPC target, bool snout)
        {
            // SUBSTITUTION: MiniSharkron (408) — the Tempest Staff's friendly
            // homing shark — stands in for the Sharknado's sharks.
            // Initial speed 11-17.67 toward the target, with a small random
            // offset; the shark's own AI homes from there.
            Vector2 pos = snout ? SnoutPosition(target) : Projectile.Center;
            Vector2 aim = target.Center + new Vector2(Main.rand.Next(-100, 101), Main.rand.Next(-100, 101));
            Vector2 vel = (aim - pos).SafeNormalize( Vector2.UnitX) * Main.rand.NextFloat(11f, 17.67f);
            SpawnFriendlyProjectile(pos, vel, ProjectileID.MiniSharkron);
        }

        private Vector2 SnoutPosition(NPC target)
        {
            // In front of the snout, slightly below (vanilla: (width + 20) / 2
            // ahead along the facing direction, Y + 45 — halved for 0.5 scale).
            Vector2 dir = (target.Center - Projectile.Center).SafeNormalize( Vector2.UnitX);
            return Projectile.Center + dir * (Projectile.width / 2f + 10f) + new Vector2(0f, 22f);
        }

        private void FaceDirection(Vector2 dir)
        {
            // Keep the fish right-side-up: flip instead of rotating past vertical.
            // (Vanilla AI_069: spriteDirection = -direction, rotation =
            // atan2 [+PI when spriteDirection == 1]; NPC drawing flips on
            // spriteDirection 1 while projectile drawing flips on -1, so
            // the inverted values here reproduce the vanilla visual exactly.)
            Projectile.spriteDirection = dir.X < 0f ? -1 : 1;
            Projectile.direction = Projectile.spriteDirection;
            Projectile.rotation = dir.ToRotation();
            if (Projectile.spriteDirection == -1)
                Projectile.rotation += MathHelper.Pi;
        }

        private void SetState(int state)
        {
            Projectile.ai[0] = state;
            Projectile.ai[1] = 0;
            Projectile.netUpdate = true;
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
        /// friendly. The Fishron substitutes (MiniSharkron, FlaironBubble)
        /// are already friendly by default; the flip is a no-op for them
        /// and a safety net for anything hostile-origin. DamageType is
        /// forced to Generic (typeless) per the charm spec.
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
