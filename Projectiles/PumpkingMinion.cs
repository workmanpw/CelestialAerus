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
    /// Pumpking as a Brigitte's Charm boss minion (2026-11-15). The Pumpkin
    /// Moon flyer with its real attacks, retargeted at enemies: Greek Fire
    /// spit from above, the high-speed chase ram, and its two PumpkingBlade
    /// helpers (separate friendly projectiles in this file) with their
    /// Flaming Scythe shots, dives, and slashes. Half scale, flat 6000
    /// typeless damage, local immunity frames.
    ///
    /// Attack data from the decompiled Terraria NPC AI (aiStyle 58 for the
    /// Pumpking, aiStyle 59 for the blades): see
    /// ~/workspace/tmp/boss_attacks_event.md, Pumpking section. Frame data
    /// from boss_frames.md §14 (case 327: 12 frames, pose-seek by ai[3] —
    /// targets 0 / 4 / 8, 7 ticks per frame step).
    ///
    /// The mode roll is the whole fight: every 300 ticks (5s) the boss
    /// re-rolls ai[3] from rand(3) — 0 = Greek Fire spit, 1 = chase,
    /// 2 = scythe mode (the blades fire). There is no life-based phase
    /// change. The mode lives in Projectile.localAI[0] (synced) so the blades
    /// can read it.
    ///
    /// ADAPTATIONS (v1):
    /// - Flee state (ai[1] == 2, entered when the target is dead or &gt;2000px
    ///   away) is NOT ported (do-not-port: a minion has no player to lose).
    ///   With no target the minion hovers at its offset near the owner.
    /// - Daytime passive sink is NOT ported (do-not-port).
    /// - The blades are friendly ModProjectiles (PumpkingBladeMinion, below)
    ///   instead of NPCs: same orbit / rise / dive / slide / slash cycle,
    ///   same scythe shots, parent-bound lifetime (they die when the
    ///   Pumpking minion despawns), minionSlots 0 (they are part of the
    ///   Pumpking minion, not independent minions).
    ///
    /// SOUNDS: Pumpking's and the blades' AI play NO sounds (grep-verified).
    /// GreekFire and FlamingScythe are also silent. No sounds are invented
    /// here; there are intentionally no sound calls in this file.
    ///
    /// FIXES (2026-11-18, vs decompiled aiStyle 58/59 + FindFrame cases
    /// 327/328): (1) the mode roll ran at localAI[2] &gt;= 300 — vanilla
    /// rolls at &gt; 300; (2) the hover dead zone damped velocity inside
    /// 50 px — vanilla does NOTHING inside the dead zone (no else
    /// branch); (3) BLADE scythe aim was computed from the blade's
    /// Center — vanilla aims from Center + (0, 30) (halved to (0, 15))
    /// while spawning at Center; (4) BLADE orbit nudge was added to
    /// the normalized velocity (a 4 px/tick shove) — vanilla adds -4/+4
    /// to the direction-to-anchor vector BEFORE normalizing (a gentle
    /// pull); (5) BLADE orbit rotation pointed along velocity — vanilla
    /// points it at the anchor; (6) BLADE rise state had invented X
    /// steering toward the rise point — vanilla has none (velocity.X
    /// *= 0.95, rise -0.3/tick); (7) BLADE rise/slide rotation used
    /// FaceTravel — vanilla points at a FIXED point (parent.Center.X -
    /// 200*side, parent.Top.Y + 230) every tick; (8) BLADE dive/slash
    /// used FaceTravel — vanilla freezes rotation in those states; (9)
    /// follow/recall updated to the 2026-11-18 weapon-minion pattern
    /// (2000 px teleport, ramped follow speed).
    /// </summary>
    public class PumpkingMinion : ModProjectile
    {
        public override string Texture => "Terraria/Images/NPC_327";
        // DisplayName comes from the localization file
        // (Mods.CelestialAerus.Projectiles.PumpkingMinion.DisplayName).

        // --- Constants (vanilla aiStyle 58 values) ---
        private const int MinionDamage = 6000;
        private const float TargetRange = 1000f;
        private const int FrameCount = 12;

        // Attack modes (Projectile.localAI[0]).
        private const int ModeSpit = 0;
        private const int ModeChase = 1;
        private const int ModeScythe = 2;

        // Movement states (Projectile.ai[1]); ai[2] is the movement timer.
        private const int MoveHover = 0;
        private const int MoveChase = 1;

        private const int ModeRollInterval = 300; // re-roll ai[3] every 300 ticks
        private const int HoverDuration = 300;    // hover lasts 300 ticks
        private const int ChaseMaxDuration = 600; // chase caps at 600 ticks

        private const float HoverSpeed = 6f;
        private const float ChaseSpeed = 16f;
        private const float SpitSpeed = 5f;
        private const float HoverHeight = 200f;   // hovers 200px above the target

        // Distinct hover offset so the boss minions don't stack on one point.
        private static readonly Vector2 HoverOffset = new Vector2(0f, -500f);

        // Animation counter (own field — vanilla FindFrame uses frameCounter
        // as a BIDIRECTIONAL step timer; Projectile.frameCounter is an int
        // in tML 1.4.4, so a float field keeps the logic exact).
        private float animCounter;

        public override void SetStaticDefaults()
        {
            Main.projFrames[Type] = FrameCount;
        }

        public override void SetDefaults()
        {
            // Hitbox matches the 100x100 vanilla hitbox at 0.5 scale.
            Projectile.width = 50;
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

            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;

            // The charm's ModPlayer refreshes timeLeft while equipped.
            Projectile.timeLeft = 2;

            Projectile.light = 0.5f;
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

            // --- Spawn the two blades once (owner only) ---
            if (Projectile.ai[0] == 0f && Projectile.owner == Main.myPlayer)
            {
                SpawnBlade(-1f, 0f);
                // Second blade is pre-charged (ai[3] = 150): it starts its
                // dive cycle after only 30 ticks instead of 180, staggering
                // the two blades out of phase — vanilla behavior, verbatim.
                SpawnBlade(1f, 150f);
                Projectile.ai[0] = 1f;
                Projectile.netUpdate = true;
            }

            NPC target = FindTarget(TargetRange);
            int mode = (int)Projectile.localAI[0];
            int moveState = (int)Projectile.ai[1];

            // Face the target (or the owner when idle).
            Vector2 faceDir = target != null
                ? target.Center - Projectile.Center
                : owner.Center - Projectile.Center;
            Projectile.spriteDirection = faceDir.X < 0f ? -1 : 1;

            // --- Movement ---
            if (target == null)
            {
                // No enemy: hover at the offset near the owner.
                HoverToward(owner.Center + HoverOffset, HoverSpeed, 0.1f);
                if (moveState != MoveHover)
                {
                    Projectile.ai[1] = MoveHover;
                    Projectile.ai[2] = 0;
                }
            }
            else if (moveState == MoveHover)
            {
                // Hover 200px above the target. Speed scales with distance
                // when mode is 1 (about to chase): 12 beyond 900px, 10
                // beyond 600, 8 beyond 300; otherwise 6.
                Vector2 hoverPoint = target.Center - new Vector2(0f, HoverHeight);
                float speed = HoverSpeed;
                if (mode == ModeChase)
                {
                    float dist = Vector2.Distance(Projectile.Center, hoverPoint);
                    if (dist > 900f)
                        speed = 12f;
                    else if (dist > 600f)
                        speed = 10f;
                    else if (dist > 300f)
                        speed = 8f;
                }
                // Steering lerp (velocity * 14 + target) / 15, with a 50px
                // dead zone around the hover point (vanilla, verbatim).
                // Inside the dead zone vanilla does NOTHING — no steering,
                // no damping (the `if (dist > 50)` has no else branch).
                Vector2 toHover = hoverPoint - Projectile.Center;
                if (toHover.Length() > 50f)
                {
                    Vector2 targetVel = toHover.SafeNormalize(Vector2.Zero) * speed;
                    Projectile.velocity = (Projectile.velocity * 14f + targetVel) / 15f;
                }

                Projectile.ai[2]++;
                if (Projectile.ai[2] >= HoverDuration)
                {
                    if (mode == ModeChase)
                    {
                        Projectile.ai[1] = MoveChase;
                        Projectile.ai[2] = 0;
                        Projectile.netUpdate = true;
                    }
                    else
                    {
                        Projectile.ai[2] = 0; // re-hover
                    }
                }
            }
            else // MoveChase
            {
                // Chase ram: sustained pursuit at speed 16 with heavy
                // steering inertia (49/50 lerp — a ~50-tick turn time
                // constant; it commits, overshoots, sweeps wide arcs).
                Vector2 targetVel = (target.Center - Projectile.Center).SafeNormalize(Vector2.Zero) * ChaseSpeed;
                Projectile.velocity = (Projectile.velocity * 49f + targetVel) / 50f;

                Projectile.ai[2]++;
                if (Projectile.ai[2] >= ChaseMaxDuration || mode != ModeChase)
                {
                    Projectile.ai[1] = MoveHover;
                    Projectile.ai[2] = 0;
                    Projectile.netUpdate = true;
                }
            }

            // Slight banking tilt (vanilla: rotation = velocity.X * -0.02).
            Projectile.rotation = Projectile.velocity.X * -0.02f;

            // --- Animation (vanilla FindFrame case 327) ---
            UpdateAnimation();

            // --- Mode roll + spit (owner only) ---
            if (Projectile.owner != Main.myPlayer)
                return;

            // localAI[2] is the mode timer: drives the ai[3] re-roll and
            // the spit cadence.
            Projectile.localAI[2]++;
            if (Projectile.localAI[2] > ModeRollInterval)
            {
                Projectile.localAI[2] = 0;
                Projectile.localAI[0] = Main.rand.Next(3);
                Projectile.netUpdate = true;
                mode = (int)Projectile.localAI[0];
            }

            // Greek Fire spit: mode 0 only, when localAI[2] % 30 == 0 and
            // localAI[2] > 30 — shots at 60, 90, ..., 300 (9 per window).
            if (mode == ModeSpit && target != null
                && Projectile.localAI[2] % 30f == 0f && Projectile.localAI[2] > 30f)
            {
                SpitGreekFire(target);
            }
        }

        private void SpawnBlade(float side, float ai3)
        {
            int b = Projectile.NewProjectile(
                Projectile.GetSource_FromThis(),
                Projectile.Center, Vector2.Zero,
                ModContent.ProjectileType<PumpkingBladeMinion>(),
                MinionDamage, 0f, Projectile.owner,
                side, Projectile.whoAmI, 0f);
            if (b >= 0 && b < Main.maxProjectiles)
            {
                // NewProjectile only sets ai[0..2]; the pre-charge timer
                // (ai[3]) is set directly after the spawn.
                Main.projectile[b].localAI[0] = ai3;
                Main.projectile[b].usesLocalNPCImmunity = true;
                Main.projectile[b].localNPCHitCooldown = 20;
            }
        }

        private void UpdateAnimation()
        {
            // Vanilla FindFrame case 327: a seek-and-hold pose system. Each
            // ai[3] value selects a target pose frame (0 / 4 / 8); the
            // animation steps toward it one frame at a time (7 ticks per
            // step), then holds it. No continuous idle/walk cycle.
            int frame = Projectile.frame;
            int mode = (int)Projectile.localAI[0];
            if (mode == 0)
            {
                if (frame > 5)
                    animCounter++;
                else if (frame > 0)
                    animCounter--;
            }
            else if (mode == 1)
            {
                if (frame > 10)
                    animCounter++;
                else if (frame > 4)
                    animCounter--;
                else if (frame < 4)
                    animCounter++;
            }
            else if (mode == 2)
            {
                if (frame < 2)
                    animCounter--;
                else if (frame > 8)
                    animCounter--;
                else if (frame < 8)
                    animCounter++;
            }

            if (animCounter > 6.0f)
            {
                animCounter = 0f;
                Projectile.frame++;
                if (Projectile.frame > 11)
                    Projectile.frame = 0;
            }
            if (animCounter < 0.0f)
            {
                animCounter = 6.0f;
                Projectile.frame--;
                if (Projectile.frame < 0)
                    Projectile.frame = 11;
            }
        }

        private void SpitGreekFire(NPC target)
        {
            // Single shot from Center + (0, 30) — scaled to half size — aimed
            // at the target's top with +/-50px horizontal jitter and a
            // DOWNWARD aim bias (the opposite of the trees' upward lob: the
            // Pumpking fires from above and wants the shots to drop onto the
            // target). Gated on the spawn tile not being solid (vanilla).
            Vector2 spawn = Projectile.Center + new Vector2(0f, 15f);
            if (WorldGen.SolidTile((int)(spawn.X / 16f), (int)(spawn.Y / 16f)))
                return;

            Vector2 aimPoint = new Vector2(target.Center.X, target.position.Y);
            Vector2 aim = aimPoint - spawn;
            aim.X += Main.rand.Next(-50, 51);
            aim.Y += Main.rand.Next(50, 201);
            aim.Y *= 0.2f;
            Vector2 vel = aim.SafeNormalize(Vector2.Zero) * SpitSpeed;
            vel.X *= 1f + Main.rand.Next(-30, 31) * 0.01f;
            vel.Y *= 1f + Main.rand.Next(-30, 31) * 0.01f;

            int type = Main.rand.Next(ProjectileID.GreekFire1, ProjectileID.GreekFire3 + 1);
            SpawnFriendlyProjectile(spawn, vel, type);
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
        /// enemies.
        /// </summary>
        private int SpawnFriendlyProjectile(Vector2 position, Vector2 velocity, int type)
        {
            if (Projectile.owner != Main.myPlayer)
                return -1;
            int p = Projectile.NewProjectile(Projectile.GetSource_FromThis(), position, velocity, type, MinionDamage, 0f, Projectile.owner);
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
            // Frame count is HARDCODED (12 — Pumpking's texture has 12
            // frames; see boss_frames.md §14, confirmed against the
            // Main.npcFrameCount binary table). NEVER Main.npcFrameCount at
            // runtime: it returns 0/1 for unloaded NPCs, which draws the
            // whole sprite sheet.
            int frameHeight = texture.Height / FrameCount;
            Rectangle frame = new Rectangle(0, Projectile.frame * frameHeight, texture.Width, frameHeight);
            Vector2 origin = frame.Size() / 2f;
            SpriteEffects effects = Projectile.spriteDirection == -1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            Main.EntitySpriteDraw(texture, Projectile.Center - Main.screenPosition, frame, lightColor, Projectile.rotation, origin, Projectile.scale, effects, 0);
            return false;
        }
    }

    /// <summary>
    /// PumpkingBlade as a Brigitte's Charm helper projectile (2026-11-15).
    /// One of the two blades the Pumpking minion spawns. Same behavior as
    /// the vanilla NPC 328 (aiStyle 59): orbits the midpoint between its
    /// parent and the target, rises and dives, slides and slashes, and
    /// fires Flaming Scythes while the parent is in mode 2.
    ///
    /// State machine (Projectile.ai[2]): 0 = orbit, 1 = rise, 2 = dive,
    /// 3 = orbit (second half), 4 = horizontal slide, 5 = slash, then back
    /// to 0. ai[0] = side (-1 left / +1 right), ai[1] = parent projectile
    /// index, ai[3] = timer within the orbit states. localAI[1] = scythe
    /// timer (mode 2 only).
    ///
    /// Parent-bound: if the parent Pumpking minion is gone, the blade
    /// decelerates and dies (vanilla behavior, verbatim). Invulnerable in
    /// vanilla (dontTakeDamage) — as a projectile it simply cannot be hit.
    /// minionSlots 0: the blades are part of the Pumpking minion.
    /// </summary>
    public class PumpkingBladeMinion : ModProjectile
    {
        public override string Texture => "Terraria/Images/NPC_328";
        // DisplayName comes from the localization file
        // (Mods.CelestialAerus.Projectiles.PumpkingBladeMinion.DisplayName).

        private const int MinionDamage = 6000;
        private const float TargetRange = 1000f;

        // Behavior states (Projectile.ai[2]).
        private const int StateOrbit = 0;
        private const int StateRise = 1;
        private const int StateDive = 2;
        private const int StateOrbit2 = 3;
        private const int StateSlide = 4;
        private const int StateSlash = 5;

        private const int OrbitDuration = 180; // each orbit state lasts 180 ticks
        private const float DiveSpeed = 18f;   // fastest single velocity in the file
        private const float SlashSpeed = 17f;
        private const float MaxParentDistance = 800f; // Manhattan; strikes abort past this

        public override void SetStaticDefaults()
        {
            // Single-frame texture (npcFrameCount[328] == 1, binary table).
            Main.projFrames[Type] = 1;
        }

        public override void SetDefaults()
        {
            // Hitbox matches the 80x80 vanilla hitbox at 0.5 scale.
            Projectile.width = 40;
            Projectile.height = 40;
            Projectile.scale = 0.5f;

            Projectile.friendly = true;
            Projectile.hostile = false;
            Projectile.minion = true;
            Projectile.minionSlots = 0; // sub-minion of the Pumpking; no slot cost
            Projectile.damage = MinionDamage;
            Projectile.DamageType = DamageClass.Generic;
            Projectile.knockBack = 0f;
            Projectile.penetrate = -1;

            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 20;

            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;

            // Refreshed every tick while the parent lives (below); the charm
            // does not track blades, so the blade keeps itself alive.
            Projectile.timeLeft = 2;

            Projectile.netImportant = true;
        }

        public override void AI()
        {
            // --- Parent binding: die if the parent is gone ---
            int parentIndex = (int)Projectile.ai[1];
            Projectile parent = parentIndex >= 0 && parentIndex < Main.maxProjectiles
                ? Main.projectile[parentIndex]
                : null;
            if (parent == null || !parent.active || parent.type != ModContent.ProjectileType<PumpkingMinion>())
            {
                // Vanilla: decelerate, then kill itself server-side.
                Projectile.velocity *= 0.9f;
                if (Projectile.owner == Main.myPlayer)
                    Projectile.Kill();
                return;
            }

            // Parent alive: keep the blade alive with it.
            Projectile.timeLeft = 2;

            float side = Projectile.ai[0] < 0f ? -1f : 1f;
            Projectile.spriteDirection = (int)-side; // vanilla: spriteDirection = -ai[0]

            // If the parent recalled far away, the blade recalls with it
            // instead of flying 800px across the map at orbit speed.
            if (Vector2.Distance(Projectile.Center, parent.Center) > 800f)
            {
                Projectile.Center = parent.Center;
                Projectile.velocity = Vector2.Zero;
                Projectile.netUpdate = true;
            }

            NPC target = FindTarget(TargetRange);
            int state = (int)Projectile.ai[2];

            switch (state)
            {
                case StateOrbit:
                case StateOrbit2:
                    Orbit(parent, target, side);
                    Projectile.localAI[0]++;
                    if (Projectile.localAI[0] >= OrbitDuration)
                    {
                        Projectile.localAI[0] = 0;
                        // No target: keep orbiting instead of striking air.
                        if (target == null)
                        {
                            Projectile.ai[2] = state; // stay in this orbit state
                        }
                        else
                        {
                            Projectile.ai[2] = state == StateOrbit ? StateRise : StateSlide;
                        }
                        Projectile.netUpdate = true;
                    }
                    break;

                case StateRise:
                    // Vanilla aiStyle 59: NO X steering — velocity.X just
                    // damps (*0.95) while the blade rises at -0.3/tick,
                    // capped at -14. Rotation points at the fixed point
                    // (parent.Center.X - 200*side, parent.Top.Y + 230)
                    // every tick.
                    FaceFixedPoint(parent, side);
                    Projectile.velocity.X *= 0.95f;
                    Projectile.velocity.Y -= 0.3f;
                    if (Projectile.velocity.Y < -14f)
                        Projectile.velocity.Y = -14f;

                    // Once 200px above the parent's top, re-target and dive.
                    if (Projectile.position.Y < parent.Top.Y - 200f)
                    {
                        if (target != null)
                        {
                            Projectile.velocity = (target.Center - Projectile.Center).SafeNormalize(Vector2.Zero) * DiveSpeed;
                            Projectile.ai[2] = StateDive;
                            Projectile.netUpdate = true;
                        }
                        else
                        {
                            Projectile.ai[2] = StateOrbit2;
                            Projectile.localAI[0] = 0;
                        }
                    }
                    break;

                case StateDive:
                    // Ballistic — no steering, and vanilla does NOT update
                    // rotation here: it stays frozen at the rise state's
                    // fixed-point aim. Ends when the blade passes the
                    // target's height, its vertical velocity turns
                    // negative, or it strays 800px (Manhattan) from the
                    // parent. Copy the exit conditions verbatim.
                    bool diveDone = Projectile.velocity.Y < 0f
                        || ManhattanDistance(Projectile.Center, parent.Center) > MaxParentDistance
                        || (target != null && Projectile.position.Y > target.position.Y);
                    if (diveDone)
                    {
                        Projectile.ai[2] = StateOrbit2;
                        Projectile.localAI[0] = 0;
                        Projectile.netUpdate = true;
                    }
                    break;

                case StateSlide:
                    // Accelerate horizontally (0.3 * -side per tick, capped
                    // at +/-14, vertical velocity damped) until 500px to
                    // either side of the parent's center, then slash.
                    // Rotation points at the same fixed point as the rise
                    // state, every tick (vanilla aiStyle 59).
                    FaceFixedPoint(parent, side);
                    Projectile.velocity.X += 0.3f * -side;
                    Projectile.velocity.X = MathHelper.Clamp(Projectile.velocity.X, -14f, 14f);
                    Projectile.velocity.Y *= 0.95f;

                    if (Math.Abs(Projectile.Center.X - parent.Center.X) >= 500f)
                    {
                        if (target != null)
                        {
                            Projectile.velocity = (target.Center - Projectile.Center).SafeNormalize(Vector2.Zero) * SlashSpeed;
                            Projectile.ai[2] = StateSlash;
                            Projectile.netUpdate = true;
                        }
                        else
                        {
                            Projectile.ai[2] = StateOrbit;
                            Projectile.localAI[0] = 0;
                        }
                    }
                    break;

                case StateSlash:
                    // Ballistic, and vanilla does NOT update rotation here
                    // either: it stays frozen at the slide state's
                    // fixed-point aim. Ends when the blade has passed the
                    // target's X-center (in its direction of travel) or
                    // strays 800px (Manhattan) from the parent.
                    bool slashDone = ManhattanDistance(Projectile.Center, parent.Center) > MaxParentDistance;
                    if (target != null)
                    {
                        if (Projectile.velocity.X > 0f && Projectile.Center.X > target.Center.X)
                            slashDone = true;
                        else if (Projectile.velocity.X < 0f && Projectile.Center.X < target.Center.X)
                            slashDone = true;
                    }
                    if (slashDone)
                    {
                        Projectile.ai[2] = StateOrbit;
                        Projectile.localAI[0] = 0;
                        Projectile.netUpdate = true;
                    }
                    break;
            }

            // --- Flaming Scythe (parent mode 2 only, owner only) ---
            if (Projectile.owner == Main.myPlayer && target != null && parent.localAI[0] == 2f)
            {
                Projectile.localAI[1]++;
                if (Projectile.localAI[1] > 90f)
                {
                    Projectile.localAI[1] = 0f;
                    FireScythe(target);
                }
            }
        }

        private void Orbit(Projectile parent, NPC target, float side)
        {
            // Anchor: the midpoint between the target and the parent, offset
            // -/+170*side horizontally and +90 vertically — each blade
            // guards the flank between its parent and the target. If the
            // target-parent Manhattan distance exceeds 700px, anchor on the
            // parent alone so the blade doesn't strand itself mid-map.
            Vector2 anchor;
            if (target != null && ManhattanDistance(target.Center, parent.Center) <= 700f)
                anchor = (target.Center + parent.Center) / 2f;
            else
                anchor = parent.Center;
            anchor += new Vector2(-170f * side, 90f);

            // Direction to the anchor. The length is taken BEFORE the
            // nudge below (vanilla num1048 = Max(1, length)) and sets the
            // speed tier.
            Vector2 toAnchor = anchor - Projectile.Center;
            float dist = Math.Max(1f, toAnchor.Length());
            float speed = 6f;
            if (dist > 1000f)
                speed = 21f;
            else if (dist > 800f)
                speed = 18f;
            else if (dist > 600f)
                speed = 15f;
            else if (dist > 400f)
                speed = 12f;
            else if (dist > 200f)
                speed = 9f;

            // Nudge: if the blade is on the WRONG side of the parent, shift
            // the anchor 4 px toward the blade's own side BEFORE
            // normalizing (vanilla num1045 -= 4 / += 4). On the raw vector
            // this is a gentle pull; on the normalized velocity it would be
            // a 4 px/tick shove.
            if (side < 0f && Projectile.Center.X > parent.Center.X)
                toAnchor.X -= 4f;
            if (side > 0f && Projectile.Center.X < parent.Center.X)
                toAnchor.X += 4f;

            Vector2 targetVel = toAnchor * (speed / dist);
            Projectile.velocity = (Projectile.velocity * 14f + targetVel) / 15f;

            // Rotation points at the anchor (nudged direction), not along
            // the velocity — but only when the nudged direction is longer
            // than 20 px.
            if (Math.Max(1f, toAnchor.Length()) > 20f)
                Projectile.rotation = (float)Math.Atan2(toAnchor.Y, toAnchor.X) + MathHelper.PiOver2;
        }

        private void FaceFixedPoint(Projectile parent, float side)
        {
            // Vanilla aiStyle 59 (rise and slide states): the blade points
            // at a FIXED point — 200 px to the blade's side of the parent's
            // center, 230 px below the parent's top — every tick, whatever
            // its velocity.
            Vector2 point = new Vector2(parent.Center.X - 200f * side, parent.Top.Y + 230f);
            Vector2 toPoint = point - Projectile.Center;
            Projectile.rotation = (float)Math.Atan2(toPoint.Y, toPoint.X) + MathHelper.PiOver2;
        }

        private void FireScythe(NPC target)
        {
            // One scythe per blade, aimed at the target's top from
            // Center + (0, 30) — halved to (0, 15) for the 0.5-scale
            // blade — but SPAWNED at the blade's Center (vanilla
            // aiStyle 59: the aim point val186 is (Center.X, Center.Y
            // + 30), the spawn point is Center). Spawned at ~0.01
            // px/tick — effectively stationary — then self-accelerates
            // (aiStyle 56 multiplies velocity x1.05/tick while Manhattan
            // speed is below 16). The near-zero spawn speed is the
            // scythe's signature: it LOOMS out of the blade and then
            // screams away. Do not spawn it at speed.
            Vector2 aimFrom = Projectile.Center + new Vector2(0f, 15f);
            Vector2 aimPoint = new Vector2(target.Center.X, target.position.Y);
            Vector2 vel = (aimPoint - aimFrom).SafeNormalize(Vector2.UnitX) * 0.01f;
            int p = Projectile.NewProjectile(
                Projectile.GetSource_FromThis(),
                Projectile.Center, vel,
                ProjectileID.FlamingScythe, MinionDamage, 0f, Projectile.owner,
                Projectile.rotation, Projectile.spriteDirection);
            if (p >= 0 && p < Main.maxProjectiles)
            {
                Main.projectile[p].friendly = true;
                Main.projectile[p].hostile = false;
                Main.projectile[p].usesLocalNPCImmunity = true;
                Main.projectile[p].localNPCHitCooldown = 20;
            }
        }

        private static float ManhattanDistance(Vector2 a, Vector2 b)
        {
            return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
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

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D texture = TextureAssets.Projectile[Projectile.type].Value;
            // Single-frame texture (npcFrameCount[328] == 1): draw it whole.
            Rectangle frame = new Rectangle(0, 0, texture.Width, texture.Height);
            Vector2 origin = frame.Size() / 2f;
            SpriteEffects effects = Projectile.spriteDirection == -1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            Main.EntitySpriteDraw(texture, Projectile.Center - Main.screenPosition, frame, lightColor, Projectile.rotation, origin, Projectile.scale, effects, 0);
            return false;
        }
    }
}
