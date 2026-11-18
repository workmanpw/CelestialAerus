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
    /// Santa-NK1 (NPC 346) as a Brigitte's Charm boss minion (2026-11-15).
    /// His real aiStyle 61 attack cycle, retargeted at enemies: the Chain
    /// Gun barrage, the Spike mine, the Missile burst, and the Present
    /// rain. Half scale, flat 6000 typeless damage, local immunity frames.
    ///
    /// Attack data from the decompiled Terraria NPC AI (aiStyle 61):
    /// see ~/workspace/tmp/boss_attacks_event.md, Part B, Santa-NK1 section.
    ///
    /// FRAME DATA (verified 2026-11-15 against decompiled FindFrame
    /// case 346 AND the binary npcFrameCount table): 9 frames. The
    /// boss_frames.md section labeled "Santa-NK1" is actually Ice
    /// Queen's 6-frame case — do NOT use it. Correct animation: cruise
    /// (ai[0]==0): frames 1-5 cycle, speed-scaled pacing, frozen while
    /// standing still; barrage (ai[0]==1): frames 6-8 cycle, 3 ticks per
    /// frame. Frame 0 is never selected.
    ///
    /// LIFE TIERS: the boss scales speed/cadence by life fraction. A minion
    /// has no life fraction, so tiers escalate by AGE: 0-30s full-life
    /// values, 30-60s &lt;75%, 60-90s &lt;50%, 90s+ &lt;25%.
    ///
    /// ADAPTATION: vanilla drops Spike mines and Present bursts with no
    /// target at all. The minion gates ALL random attacks on having a
    /// target, so it doesn't litter the world with mines and presents
    /// when nothing is around to fight.
    ///
    /// SOUNDS: none in his AI (verified by grep). BulletDeadeye (180)
    /// plays SoundID.Item11 on its first tick (projectile-side, free
    /// from vanilla). Missile (350), Present (351) and Spike (352) are
    /// silent. No sounds are invented here.
    ///
    /// FIXES (2026-11-18, vs decompiled aiStyle 61 + FindFrame case 346):
    /// (1) the null-owner branch dereferenced player.Center when player
    /// could be null (NullReferenceException) — it now just damps
    /// velocity; (2) the chain-gun muzzle (+50, rand(15,36)) and the
    /// chimney (-dir*24, -64) were NOT halved for the 0.5-scale minion
    /// — the gun fired from ~25 px outside the drawn sprite. Both are
    /// now halved (+25 / rand(8,18) and -dir*12 / -32); (3) the vertical
    /// movement was wrong in both branches: the ground box was
    /// [bottom, bottom+20] instead of vanilla's [bottom-20, bottom],
    /// and the rise/sink velocity logic never zeroed opposing velocity
    /// and subtracted 0.2 in the wrong cases. Ported verbatim from
    /// aiStyle 61 with the box halved to 40x10; (4) the barrage ended
    /// at ai[1] >= 240 — vanilla ends at ai[1] > 240; (5) follow/recall
    /// updated to the 2026-11-18 weapon-minion pattern (2000 px
    /// teleport, ramped follow speed); (6) Projectile.light = 0.3f
    /// (vanilla AddLight (0.3, 0.26, 0.05) at his center).
    /// </summary>
    public class SantaNK1Minion : ModProjectile
    {
        public override string Texture => "Terraria/Images/NPC_346";
        // DisplayName comes from the localization file
        // (Mods.CelestialAerus.Projectiles.SantaNK1Minion.DisplayName).

        // --- Constants (vanilla aiStyle 61 values, Classic) ---
        private const int MinionDamage = 6000;
        private const float TargetRange = 1000f;

        // States (Projectile.ai[0]); ai[1] = tick counter in the state.
        private const int StateCruise = 0;
        private const int StateBarrage = 1;
        private const int CruiseTime = 300;
        private const int BarrageTime = 240;

        // Life-tier tables (index 0 = full life, 1 = <75%, 2 = <50%, 3 = <25%).
        private static readonly float[] CruiseSpeed = { 2f, 3f, 4f, 5f };
        private static readonly int[] ChainGunInterval = { 16, 14, 11, 8 };
        private static readonly int[] SpikeDenom = { 600, 540, 450, 300 };
        private static readonly int[] MissileDenom = { 1200, 1080, 900, 600 };
        private static readonly int[] PresentDenom = { 2700, 2430, 2025, 1350 };

        // Burst lengths (ticks) and firing cadences.
        private const int MissileBurstLength = 100;
        private const int MissileBurstInterval = 12;
        private const int PresentBurstLength = 100;
        private const int PresentBurstInterval = 9;

        // Age thresholds for tier escalation (ticks).
        private const int Tier1Age = 1800; // 30 s
        private const int Tier2Age = 3600; // 60 s
        private const int Tier3Age = 5400; // 90 s

        // Distinct hover offset so the boss minions don't stack on one point.
        private static readonly Vector2 HoverOffset = new Vector2(300f, -60f);

        // --- State (fields; only the owner makes spawn decisions) ---
        private int age;
        private float animCounter;
        private int missileBurst = -1; // -1 = inactive, else ticks since trigger
        private int presentBurst = -1; // -1 = inactive, else ticks since trigger (starts at 2)

        public override void SetDefaults()
        {
            // Hitbox matches the 112x140 vanilla hitbox at 0.5 scale.
            Projectile.width = 56;
            Projectile.height = 70;
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

            // Vanilla AddLight (0.3, 0.26, 0.05) at his center.
            Projectile.light = 0.3f;
        }

        public override void AI()
        {
            Player player = Main.player[Projectile.owner];
            if (player == null || !player.active || player.dead)
            {
                // Owner gone: drift to a stop and idle (player may be
                // null here — never dereference it).
                Projectile.velocity *= 0.95f;
                UpdateFrame();
                return;
            }

            age++;

            // FOLLOW (2026-11-18 weapon-minion pattern): recall + ramped
            // follow so the minion keeps up; teleport is only the last
            // resort when far off-screen (2000px).
            float distToOwner = Vector2.Distance(Projectile.Center, player.Center);
            if (distToOwner > 2000f)
            {
                Projectile.Center = player.Center;
                Projectile.velocity = Vector2.Zero;
                Projectile.netUpdate = true;
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
            int tier = age < Tier1Age ? 0 : age < Tier2Age ? 1 : age < Tier3Age ? 2 : 3;

            if (target == null)
            {
                // No target: hover near the owner; the state machine keeps
                // alternating but nothing fires.
                HoverToward(player.Center + HoverOffset, 8f, 0.3f);
                AdvanceStateMachine();
                UpdateFrame();
                return;
            }

            // Face the target (vanilla FindFrame sets spriteDirection =
            // direction every tick).
            Projectile.direction = target.Center.X >= Projectile.Center.X ? 1 : -1;
            Projectile.spriteDirection = Projectile.direction;

            // Movement: horizontal cruise toward the target, stopping dead
            // while firing (state 1) or within 50 px horizontally.
            int state = (int)Projectile.ai[0];
            float dx = target.Center.X - Projectile.Center.X;
            if (state == StateBarrage || Math.Abs(dx) < 50f)
            {
                Projectile.velocity.X *= 0.9f;
                if (Math.Abs(Projectile.velocity.X) < 0.1f)
                    Projectile.velocity.X = 0f;
            }
            else
            {
                float dirX = dx >= 0f ? 1f : -1f;
                Projectile.velocity.X = (Projectile.velocity.X * 20f + dirX * CruiseSpeed[tier]) / 21f;
            }

            // Vertical (vanilla aiStyle 61, verbatim): dive onto the
            // target when directly above it; rise if ground is within
            // the box overlapping his lower half; otherwise sink. The
            // ground box is [bottom-20, bottom] at full scale, halved
            // to a 40x10 box at [bottom-10, bottom] for the minion.
            bool directlyAbove = target.Left.X >= Projectile.Left.X && target.Right.X <= Projectile.Right.X
                && Projectile.Bottom.Y < target.Bottom.Y - 16f;
            if (directlyAbove)
            {
                Projectile.velocity.Y += 0.5f;
            }
            else if (GroundBelow())
            {
                // Solid ground under his lower half: stop falling, then
                // rise gently (-0.025) or firmly (-0.2) if already
                // rising fast; never faster than -4.
                if (Projectile.velocity.Y > 0f)
                    Projectile.velocity.Y = 0f;
                if (Projectile.velocity.Y > -0.2f)
                    Projectile.velocity.Y -= 0.025f;
                else
                    Projectile.velocity.Y -= 0.2f;
                if (Projectile.velocity.Y < -4f)
                    Projectile.velocity.Y = -4f;
            }
            else
            {
                // Open air below: stop rising, then sink gently (+0.025)
                // or firmly (+0.5) if already falling.
                if (Projectile.velocity.Y < 0f)
                    Projectile.velocity.Y = 0f;
                if (Projectile.velocity.Y < 0.1f)
                    Projectile.velocity.Y += 0.025f;
                else
                    Projectile.velocity.Y += 0.5f;
            }
            if (Projectile.velocity.Y > 10f)
                Projectile.velocity.Y = 10f;

            // Chain Gun (state 1 only, while the target is in range).
            if (state == StateBarrage && (int)Projectile.ai[1] % ChainGunInterval[tier] == 0)
                FireChainGun(target);

            // Random attacks (owner-only rolls; all gated on a target).
            if (Projectile.owner == Main.myPlayer)
                RandomAttacks(target, tier);

            AdvanceStateMachine();
            UpdateFrame();
        }

        private void AdvanceStateMachine()
        {
            // Deterministic alternation: 300 ticks cruise, 240 ticks
            // barrage. The timer runs on all clients so the cycle stays
            // in sync; only spawns are owner-gated.
            Projectile.ai[1]++;
            if (Projectile.ai[0] == StateCruise && Projectile.ai[1] >= CruiseTime)
            {
                Projectile.ai[0] = StateBarrage;
                Projectile.ai[1] = 0f;
                Projectile.netUpdate = true;
            }
            else if (Projectile.ai[0] == StateBarrage && Projectile.ai[1] > BarrageTime)
            {
                Projectile.ai[0] = StateCruise;
                Projectile.ai[1] = 0f;
                Projectile.netUpdate = true;
            }
        }

        private void FireChainGun(NPC target)
        {
            // BulletDeadeye (180): speed 15, +/-30% per-axis jitter, aimed
            // at the target's center with +/-40 px error, from the gun on
            // his facing side, slightly below center. Vanilla muzzle is
            // Center.X + direction*50, Center.Y + rand(15, 36) at full
            // scale — halved for the 0.5-scale minion.
            Vector2 spawn = new Vector2(
                Projectile.Center.X + Projectile.direction * 25f,
                Projectile.Center.Y + Main.rand.Next(8, 18));
            Vector2 aim = target.Center + new Vector2(Main.rand.Next(-40, 41), Main.rand.Next(-40, 41));
            Vector2 vel = (aim - spawn).SafeNormalize(Vector2.UnitX) * 15f;
            vel.X *= 1f + Main.rand.Next(-20, 21) * 0.015f;
            vel.Y *= 1f + Main.rand.Next(-20, 21) * 0.015f;
            SpawnFriendlyProjectile(spawn, vel, ProjectileID.BulletDeadeye);
        }

        private void RandomAttacks(NPC target, int tier)
        {
            // Spike mine: per-tick roll, any state. Dropped from the
            // chimney (behind/above him) at ~1 px/tick, then falls under
            // gravity and sits as a lingering mine (20 s lifetime).
            if (Main.rand.Next(SpikeDenom[tier]) == 0)
            {
                Vector2 chimney = new Vector2(
                    Projectile.Center.X - Projectile.direction * 12f,
                    Projectile.Center.Y - 32f);
                Vector2 vel = new Vector2(Main.rand.Next(1, 100) * Projectile.direction, 1f)
                    .SafeNormalize(Vector2.UnitX) * 1f;
                SpawnFriendlyProjectile(chimney, vel, ProjectileID.Spike);
            }

            // Missile burst: per-tick trigger, then a 100-tick burst firing
            // every 12 ticks (8 missiles). Firing requires the target in
            // range; the burst ticks down regardless.
            if (missileBurst < 0)
            {
                if (Main.rand.Next(MissileDenom[tier]) == 0)
                    missileBurst = 0;
            }
            else
            {
                missileBurst++;
                if (missileBurst % MissileBurstInterval == 0 && missileBurst > 0 && missileBurst < MissileBurstLength)
                    FireMissile(target);
                if (missileBurst >= MissileBurstLength)
                    missileBurst = -1;
            }

            // Present burst: per-tick trigger, then a 100-tick burst firing
            // every 9 ticks (11 presents). The counter starts at 2 (the
            // vanilla "armed" value), so the first present fires 7 ticks
            // after the trigger. No range check (vanilla fires regardless).
            if (presentBurst < 0)
            {
                if (Main.rand.Next(PresentDenom[tier]) == 0)
                    presentBurst = 2;
            }
            else
            {
                presentBurst++;
                if (presentBurst % PresentBurstInterval == 0 && presentBurst < PresentBurstLength)
                    FirePresent();
                if (presentBurst >= PresentBurstLength)
                    presentBurst = -1;
            }
        }

        private void FireMissile(NPC target)
        {
            // Missile (350): speed 12.5, aimed with +/-50 px error and
            // +/-30% jitter, from the chimney. Flies through tiles.
            // Vanilla chimney is Center.X - direction*24, Center.Y - 64
            // at full scale — halved for the 0.5-scale minion.
            Vector2 chimney = new Vector2(
                Projectile.Center.X - Projectile.direction * 12f,
                Projectile.Center.Y - 32f);
            Vector2 aim = target.Center + new Vector2(Main.rand.Next(-50, 51), Main.rand.Next(-50, 51));
            Vector2 vel = (aim - chimney).SafeNormalize(Vector2.UnitX) * 12.5f;
            vel.X *= 1f + Main.rand.Next(-20, 21) * 0.015f;
            vel.Y *= 1f + Main.rand.Next(-20, 21) * 0.015f;
            SpawnFriendlyProjectile(chimney, vel, ProjectileID.Missile);
        }

        private void FirePresent()
        {
            // Present (351): speed 11, lobbed upward in a spread (+/-~18
            // degrees from vertical), from the chimney. Arcs over and
            // rains down slowly (3 px/tick terminal fall).
            Vector2 chimney = new Vector2(
                Projectile.Center.X - Projectile.direction * 12f,
                Projectile.Center.Y - 32f);
            Vector2 dir = new Vector2(Main.rand.Next(-100, 101), -300).SafeNormalize(Vector2.UnitY * -1f);
            Vector2 vel = dir * 11f;
            vel.X *= 1f + Main.rand.Next(-20, 21) * 0.01f;
            vel.Y *= 1f + Main.rand.Next(-20, 21) * 0.01f;
            SpawnFriendlyProjectile(chimney, vel, ProjectileID.Present);
        }

        private bool GroundBelow()
        {
            // Vanilla aiStyle 61 checks an 80x20 box overlapping the
            // NPC's lower 20 px: Collision.SolidCollision((Center.X - 40,
            // position.Y + height - 20), 80, 20). Halved for the
            // 0.5-scale minion: a 40x10 box at [bottom-10, bottom].
            Vector2 boxPos = new Vector2(Projectile.Center.X - 20f, Projectile.position.Y + Projectile.height - 10f);
            return Collision.SolidCollision(boxPos, 40, 10);
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
            // Vanilla FindFrame case 346 (verified in the decompile):
            // barrage (ai[0]==1): frames 6-7-8, 3 ticks per frame; cruise
            // (ai[0]==0): frames 1-5, counter += 1 + |vX|/2 per tick,
            // advance when > 4; frame frozen while standing still.
            // Frame 0 is never selected.
            if (Projectile.ai[0] == StateBarrage)
            {
                if (Projectile.frame < 6 || Projectile.frame > 8)
                    Projectile.frame = 6;
                animCounter++;
                if (animCounter > 2f)
                {
                    animCounter = 0f;
                    Projectile.frame++;
                    if (Projectile.frame > 8)
                        Projectile.frame = 6;
                }
            }
            else if (Projectile.velocity.X == 0f)
            {
                animCounter = 0f;
            }
            else
            {
                if (Projectile.frame < 1 || Projectile.frame > 5)
                    Projectile.frame = 1;
                animCounter += 1f + Math.Abs(Projectile.velocity.X) / 2f;
                if (animCounter > 4f)
                {
                    animCounter = 0f;
                    Projectile.frame++;
                    if (Projectile.frame > 5)
                        Projectile.frame = 1;
                }
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D texture = TextureAssets.Projectile[Projectile.type].Value;
            // Frame count is HARDCODED (9 — verified against the binary
            // npcFrameCount table AND decompiled FindFrame case 346).
            // Main.npcFrameCount returns 0/1 for NPCs whose textures
            // haven't loaded yet, which drew the whole sprite sheet.
            // Projectile.frame is set every tick by UpdateFrame(). The
            // sprite flips with spriteDirection (vanilla FindFrame sets
            // spriteDirection = direction every tick).
            const int frameCount = 9;
            int frameHeight = texture.Height / frameCount;
            Rectangle frame = new Rectangle(0, Projectile.frame * frameHeight, texture.Width, frameHeight);
            Vector2 origin = frame.Size() / 2f;
            SpriteEffects effects = Projectile.spriteDirection == -1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            Main.EntitySpriteDraw(texture, Projectile.Center - Main.screenPosition, frame, lightColor, Projectile.rotation, origin, Projectile.scale, effects, 0);
            return false;
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
}
