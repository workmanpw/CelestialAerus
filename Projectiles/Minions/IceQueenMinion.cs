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
    /// Ice Queen (NPC 345) as a Brigitte's Charm boss minion (2026-11-15).
    /// Her real aiStyle 60 attack cycle, retargeted at enemies: the
    /// FrostWave strafe, the FrostShard rain hover, and the spinning
    /// FrostShard spray. Half scale, flat 6000 typeless damage, local
    /// immunity frames.
    ///
    /// Attack data from the decompiled Terraria NPC AI (aiStyle 60):
    /// see ~/workspace/tmp/boss_attacks_event.md, Part B, Ice Queen section.
    ///
    /// FRAME DATA (verified 2026-11-15 against decompiled FindFrame
    /// case 345 AND the binary npcFrameCount table): 6 frames. The
    /// boss_frames.md section labeled "Ice Queen (344)" is actually
    /// Everscream's 10-frame loop — do NOT use it. Correct animation:
    /// state 0 (strafe): frame 0 while ai[3] >= 0, frames 1/2 by velocity
    /// direction while ai[3] < 0; state 1 (hover): frames 3-4 alternating
    /// every 6 ticks; state 2 (spin): static frame 5.
    ///
    /// LIFE TIERS: the boss scales speed/cadence by life fraction. A minion
    /// has no life fraction, so tiers escalate by AGE: 0-30s full-life
    /// values, 30-60s &lt;75%, 60-90s &lt;50%, 90s+ &lt;25%. The &lt;10% tier is NOT
    /// used (its 1-tick spin spray would flood the projectile cap).
    ///
    /// DEBUFFS: FrostWave/FrostShard Chilled/Frozen effects are
    /// player-hit-path-only in vanilla — the friendly projectiles deal
    /// damage only. The minion's icy touch applies them on CONTACT
    /// instead (OnHitNPC, vanilla FrostWave odds: Chilled 300/600 ticks,
    /// then a 2/3 roll for Frozen 60/40/20 ticks).
    ///
    /// SOUNDS: none in her AI (verified by grep). FrostWave plays
    /// SoundID.Item8 on its first tick (projectile-side, free from
    /// vanilla 348). No sounds are invented here.
    ///
    /// FIXES (2026-11-18, vs decompiled aiStyle 60 + FindFrame case 345):
    /// (1) the null-owner branch dereferenced player.Center when player
    /// could be null (NullReferenceException) — it now just damps
    /// velocity; (2) the hover state damped opposing velocity INSTEAD of
    /// accelerating — vanilla accelerates toward the target AND damps
    /// opposing velocity in the same tick (velocity.X += accel, then
    /// *= 0.98 if still moving the wrong way); (3) follow/recall
    /// updated to the 2026-11-18 weapon-minion pattern (2000 px
    /// teleport, ramped follow speed).
    /// </summary>
    public class IceQueenMinion : ModProjectile
    {
        public override string Texture => "Terraria/Images/NPC_345";
        // DisplayName comes from the localization file
        // (Mods.CelestialAerus.Projectiles.IceQueenMinion.DisplayName).

        // --- Constants (vanilla aiStyle 60 values, Classic) ---
        private const int MinionDamage = 6000;
        private const float TargetRange = 1000f;

        // States (Projectile.ai[0]); ai[1] = state-duration accumulator
        // (+1/+2/+3 per tick, owner-rolled); ai[2] = strafe direction;
        // ai[3] = fire-cycle counter (state 0) / shot timer (states 1-2).
        private const int StateStrafe = 0;
        private const int StateHover = 1;
        private const int StateSpin = 2;

        // State-duration thresholds (ai[1] > threshold ends the state;
        // average durations ~400 / ~300 / ~250 ticks).
        private const int StrafeDuration = 800;
        private const int HoverDuration = 600;
        private const int SpinDuration = 500;

        // Life-tier tables (index 0 = full life, 1 = <75%, 2 = <50%, 3 = <25%).
        private static readonly float[] StrafeAccel = { 0.45f, 0.55f, 0.7f, 0.8f };
        private static readonly float[] StrafeMaxSpeed = { 7f, 8f, 10f, 11f };
        private static readonly float[] FrostWaveSpeed = { 6f, 7f, 8f, 9f };
        private static readonly int[] FireCycleN = { 14, 13, 12, 11 };
        private static readonly float[] HoverAccel = { 0.10f, 0.12f, 0.15f, 0.20f };
        private static readonly float[] HoverMaxSpeed = { 6f, 7f, 8f, 9f };
        private static readonly int[] RainInterval = { 18, 17, 15, 13 };
        private static readonly int[] SprayInterval = { 8, 7, 5, 2 };

        // Age thresholds for tier escalation (ticks).
        private const int Tier1Age = 1800; // 30 s
        private const int Tier2Age = 3600; // 60 s
        private const int Tier3Age = 5400; // 90 s

        // Distinct hover offset so the boss minions don't stack on one point.
        private static readonly Vector2 HoverOffset = new Vector2(-300f, -320f);

        // --- State (fields; only the owner makes spawn decisions) ---
        private int age;
        private float animCounter;

        public override void SetDefaults()
        {
            // Hitbox matches the 130x140 vanilla hitbox at 0.5 scale.
            Projectile.width = 65;
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
                // No target: hover near the owner, hold the state.
                HoverToward(player.Center + HoverOffset, 8f, 0.3f);
                Projectile.rotation = Projectile.velocity.X * 0.05f;
                UpdateFrame();
                return;
            }

            // State-duration accumulator (owner-rolled, vanilla +1/+2/+3).
            if (Projectile.owner == Main.myPlayer)
                Projectile.ai[1] += Main.rand.Next(1, 4);

            int state = (int)Projectile.ai[0];
            switch (state)
            {
                case StateStrafe:
                    StrafeState(target, tier);
                    // Ends when ai[1] > 800 AND horizontally within 600 px.
                    if (Projectile.owner == Main.myPlayer && Projectile.ai[1] > StrafeDuration
                        && Math.Abs(target.Center.X - Projectile.Center.X) < 600f)
                        PickNextState(target);
                    break;
                case StateHover:
                    HoverState(target, tier);
                    if (Projectile.owner == Main.myPlayer && Projectile.ai[1] > HoverDuration)
                        PickNextState(target);
                    break;
                case StateSpin:
                    SpinState(tier);
                    if (Projectile.owner == Main.myPlayer && Projectile.ai[1] > SpinDuration)
                        PickNextState(target);
                    break;
                default:
                    SetState(StateStrafe);
                    break;
            }

            UpdateFrame();
        }

        private void StrafeState(NPC target, int tier)
        {
            // Strafe direction: re-pick when 0 (toward the target's side);
            // reset to 0 after crossing to the other side by 800+ px.
            float dx = target.Center.X - Projectile.Center.X;
            if (Projectile.ai[2] == 0f)
            {
                Projectile.ai[2] = dx >= 0f ? 1f : -1f;
            }
            else if (Math.Sign(dx) != Math.Sign(Projectile.ai[2]) && Math.Abs(dx) > 800f)
            {
                Projectile.ai[2] = 0f;
            }

            // Horizontal: accelerate along the strafe direction, capped.
            Projectile.velocity.X += Projectile.ai[2] * StrafeAccel[tier];
            Projectile.velocity.X = MathHelper.Clamp(Projectile.velocity.X, -StrafeMaxSpeed[tier], StrafeMaxSpeed[tier]);

            // Vertical: keep the target's top 150-200 px below her bottom.
            float gap = target.Top.Y - Projectile.Bottom.Y;
            if (gap < 150f)
                Projectile.velocity.Y -= 0.2f;
            else if (gap > 200f)
                Projectile.velocity.Y += 0.2f;
            Projectile.velocity.Y = MathHelper.Clamp(Projectile.velocity.Y, -8f, 8f);

            Projectile.rotation = Projectile.velocity.X * 0.05f;

            // FrostWave fire cycle (vanilla): ai[3] counts 0 -> N, resets
            // to -N, counts back up to 0, firing exactly at ai[3] == 0.
            // Only advances while above the target and in range (or while
            // a cycle is already in progress).
            bool inPosition = Projectile.position.Y < target.position.Y
                && (Math.Abs(dx) < 500f || Projectile.localAI[0] < 0f);
            if (inPosition)
            {
                Projectile.localAI[0]++;
                int n = FireCycleN[tier];
                if (Projectile.localAI[0] > n)
                    Projectile.localAI[0] = -n;
                else if (Projectile.localAI[0] == 0f && Projectile.owner == Main.myPlayer)
                    FireFrostWave(target, tier);
            }
            else if (Projectile.localAI[0] < 0f)
            {
                // Finish a cycle in progress even out of range.
                Projectile.localAI[0]++;
            }
        }

        private void FireFrostWave(NPC target, int tier)
        {
            // Spawn point leads her own motion (Center + velocity.X * 7);
            // aimed at the target's center. FrostWave plays Item8 on its
            // first tick (vanilla, free).
            Vector2 spawn = Projectile.Center + new Vector2(Projectile.velocity.X * 7f, 0f);
            Vector2 vel = (target.Center - spawn).SafeNormalize(Vector2.UnitX) * FrostWaveSpeed[tier];
            SpawnFriendlyProjectile(spawn, vel, ProjectileID.FrostWave);
        }

        private void HoverState(NPC target, int tier)
        {
            // Horizontal chase (vanilla aiStyle 60, verbatim): accelerate
            // toward the target AND damp opposing velocity in the SAME
            // tick — the accel is not skipped when moving the wrong way.
            // Over-max velocity decays.
            if (Projectile.Center.X < target.Center.X)
            {
                Projectile.velocity.X += HoverAccel[tier];
                if (Projectile.velocity.X < 0f)
                    Projectile.velocity.X *= 0.98f;
            }
            if (Projectile.Center.X > target.Center.X)
            {
                Projectile.velocity.X -= HoverAccel[tier];
                if (Projectile.velocity.X > 0f)
                    Projectile.velocity.X *= 0.98f;
            }
            if (Projectile.velocity.X > HoverMaxSpeed[tier] || Projectile.velocity.X < -HoverMaxSpeed[tier])
                Projectile.velocity.X *= 0.95f;

            // Vertical: keep the target's top 180-200 px below her bottom.
            float gap = target.Top.Y - Projectile.Bottom.Y;
            if (gap < 180f)
                Projectile.velocity.Y -= 0.1f;
            else if (gap > 200f)
                Projectile.velocity.Y += 0.1f;
            Projectile.velocity.Y = MathHelper.Clamp(Projectile.velocity.Y, -6f, 6f);

            Projectile.rotation = Projectile.velocity.X * 0.01f;

            // FrostShard rain (vanilla cadence by tier).
            Projectile.localAI[0]++;
            if (Projectile.localAI[0] >= RainInterval[tier])
            {
                Projectile.localAI[0] = 0f;
                if (Projectile.owner == Main.myPlayer)
                    DropShard();
            }
        }

        private void DropShard()
        {
            // Dropped from beneath her (14 px above her bottom edge);
            // skipped if that tile is solid. Initial velocity carries a
            // little of her drift plus 3 px/tick down; gravity (0.2/tick)
            // does the rest. ai[0] = random visual frame 0-4.
            Vector2 spawn = new Vector2(Projectile.Center.X, Projectile.position.Y + Projectile.height - 14f);
            int tileX = (int)(spawn.X / 16f);
            int tileY = (int)(spawn.Y / 16f);
            if (tileX >= 0 && tileX < Main.maxTilesX && tileY >= 0 && tileY < Main.maxTilesY
                && WorldGen.SolidTile(tileX, tileY))
                return;
            Vector2 vel = new Vector2(Projectile.velocity.X * 0.25f, Math.Max(Projectile.velocity.Y, 0f) + 3f);
            SpawnFriendlyProjectile(spawn, vel, ProjectileID.FrostShard, ai0: Main.rand.Next(5));
        }

        private void SpinState(int tier)
        {
            // No steering: velocity decays, rotation spins (vanilla:
            // rotation += 0.2/tick, a full turn in ~31 ticks).
            Projectile.velocity *= 0.95f;
            Projectile.rotation += 0.2f;

            // Radial spray: one shard per firing tick in a random direction.
            Projectile.localAI[0]++;
            if (Projectile.localAI[0] >= SprayInterval[tier])
            {
                Projectile.localAI[0] = 0f;
                if (Projectile.owner == Main.myPlayer)
                    SprayShard();
            }
        }

        private void SprayShard()
        {
            // Random direction at speed 15, from her center shifted 20 px
            // up plus the shot direction x 4. ai[0] defaults to 0 (frame 0).
            Vector2 dir = new Vector2(Main.rand.Next(-1000, 1001), Main.rand.Next(-1000, 1001)).SafeNormalize(Vector2.UnitX);
            Vector2 spawn = Projectile.Center + new Vector2(0f, -20f) + dir * 4f;
            SpawnFriendlyProjectile(spawn, dir * 15f, ProjectileID.FrostShard);
        }

        private void PickNextState(NPC target)
        {
            // Next state is uniform random 0/1/2, except state 0 is forced
            // if horizontally more than 1000 px from the target.
            int next = Main.rand.Next(3);
            if (Math.Abs(target.Center.X - Projectile.Center.X) > 1000f)
                next = StateStrafe;
            SetState(next);
        }

        private void SetState(int state)
        {
            Projectile.ai[0] = state;
            Projectile.ai[1] = 0f;
            Projectile.ai[2] = 0f;
            Projectile.localAI[0] = 0f;
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

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            // The minion's icy touch (see header): vanilla FrostWave odds.
            target.AddBuff(BuffID.Chilled, Main.rand.Next(2) == 0 ? 300 : 600);
            if (Main.rand.Next(3) < 2) // 2/3 roll for Frozen
            {
                if (Main.rand.Next(16) == 0)
                    target.AddBuff(BuffID.Frozen, 60);
                else if (Main.rand.Next(12) == 0)
                    target.AddBuff(BuffID.Frozen, 40);
                else if (Main.rand.Next(8) == 0)
                    target.AddBuff(BuffID.Frozen, 20);
            }
        }

        private void UpdateFrame()
        {
            // Vanilla FindFrame case 345 (verified in the decompile):
            // state 0: frame 0 while ai[3] >= 0, frames 1/2 by velocity
            // direction while ai[3] < 0; state 1: frames 3-4 alternating
            // every 6 ticks; state 2: static frame 5.
            int state = (int)Projectile.ai[0];
            if (state == StateStrafe)
            {
                animCounter = 0f;
                if (Projectile.localAI[0] >= 0f)
                    Projectile.frame = 0;
                else
                    Projectile.frame = Projectile.velocity.X < 0f ? 1 : 2;
            }
            else if (state == StateHover)
            {
                if (Projectile.frame < 3 || Projectile.frame > 4)
                    Projectile.frame = 3;
                animCounter++;
                if (animCounter > 5f)
                {
                    animCounter = 0f;
                    Projectile.frame = Projectile.frame == 3 ? 4 : 3;
                }
            }
            else if (state == StateSpin)
            {
                animCounter = 0f;
                Projectile.frame = 5;
            }
            // Any other ai[0] (e.g. the one-tick transition): frame freezes.
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D texture = TextureAssets.Projectile[Projectile.type].Value;
            // Frame count is HARDCODED (6 — verified against the binary
            // npcFrameCount table AND decompiled FindFrame case 345).
            // Main.npcFrameCount returns 0/1 for NPCs whose textures
            // haven't loaded yet, which drew the whole sprite sheet.
            // Projectile.frame is set every tick by UpdateFrame().
            const int frameCount = 6;
            int frameHeight = texture.Height / frameCount;
            Rectangle frame = new Rectangle(0, Projectile.frame * frameHeight, texture.Width, frameHeight);
            Vector2 origin = frame.Size() / 2f;
            Main.EntitySpriteDraw(texture, Projectile.Center - Main.screenPosition, frame, lightColor, Projectile.rotation, origin, Projectile.scale, SpriteEffects.None, 0);
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
