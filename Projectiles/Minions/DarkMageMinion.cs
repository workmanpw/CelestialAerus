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
    /// Dark Mage (NPC 564, DD2DarkMageT1) as a Brigitte's Charm boss minion
    /// (2026-11-15). The Old One's Army caster with his real attacks,
    /// retargeted at enemies: the energy bolt and the skeleton summon.
    /// Half scale, flat 6000 typeless damage, local immunity frames.
    ///
    /// Attack data from the decompiled Terraria NPC AI (AI_109_DarkMage):
    /// see ~/workspace/tmp/boss_attacks_event.md, Dark Mage section.
    /// Frame data from decompiled FindFrame case 564/565 (NOT from
    /// boss_frames.md — its "5 aim poses" claim is FALSE): the texture is
    /// a 5-column x 9-row GRID of 80x80 cells, 41 frames total. Idle is a
    /// 5-frame hover cycle (0-4, 8 ticks/frame, faster when moving);
    /// the bolt cast plays 5,6,7 x3 then 8-12 (7 t/f); the summon cast
    /// plays 26-29 x3 then 30-40 (8 t/f). ai[2] is NOT used in FindFrame
    /// at all — there are no aim poses.
    ///
    /// FIXES (2026-11-18, vs decompiled AI_109 + FindFrame case 564/565
    /// + DrawNPC): (1) the sprite was BROKEN — the backup drew a 5-frame
    /// vertical strip with invented aim poses, so the game showed garbage
    /// bands from the wrong part of the sheet. Rewritten to the 41-frame
    /// grid with vanilla's cast sequences and hover cycle, the Bottom
    /// anchor, the (52 / W-52, H-10) origin, the 4 shimmer ghosts and the
    /// GlowMask[225] + 4 glow ghosts; (2) spriteDirection was -direction
    /// — vanilla sets spriteDirection = direction (AI_109 line 98 and
    /// every FindFrame cast branch); (3) the null-owner branch
    /// dereferenced player.Center when player could be null
    /// (NullReferenceException) — it now just damps velocity; (4) the
    /// light was a plain AddLight without the x1.5 factor — vanilla is a
    /// PlotTileLine from Top to Bottom with (0.3, 0.05, 0.45) x 1.5;
    /// (5) follow/recall updated to the 2026-11-18 weapon-minion
    /// pattern (2000 px teleport, ramped follow speed).
    ///
    /// DROPPED — HEAL (projectile 674 DD2DarkMageHeal): the heal orb heals
    /// EVERY NPC within 1000 px with no faction check. A friendly port would
    /// heal the enemies the player is fighting (up to 1500 HP per cast) and
    /// would never heal the player or projectile minions. The heal step is
    /// skipped entirely; the cast cycle is bolt -&gt; summon -&gt; bolt...
    ///
    /// ADAPTED — SUMMON: the vanilla raise reads the DD2 event's grave list
    /// (dead-goblin spots), which is empty outside the event. The minion
    /// spawns up to 8 friendly DD2SkeletonT1 (566) directly on solid ground
    /// near the target instead, capped at 8 alive. The skeletons are
    /// VISUAL ONLY (like the Eye of Cthulhu's servants): vanilla skeleton
    /// AI targets players/crystals, so as friendly NPCs they cannot attack.
    /// The raise orb (673, pure visual) is still spawned at the cast moment.
    ///
    /// SOUNDS: DD2_DarkMageSummonSkeleton at the summon cast; the bolt sound
    /// (DD2_DarkMageAttack) and the skeleton-rise sound (DD2_SkeletonSummoned)
    /// play from the projectiles' own AI, free. Heal sounds unused (no heal).
    /// </summary>
    public class DarkMageMinion : ModProjectile
    {
        public override string Texture => "Terraria/Images/NPC_564";
        // DisplayName comes from the localization file
        // (Mods.CelestialAerus.Projectiles.DarkMageMinion.DisplayName).

        // --- Constants (vanilla AI_109_DarkMage values) ---
        private const int MinionDamage = 6000;
        private const float TargetRange = 1000f;

        private const float HoverSpeed = 2f;     // vanilla: 0.5 (raised so the minion can position)
        private const float HoverAccel = 0.1f;   // vanilla X accel
        private const float HoldDistance = 300f; // hold this far from the target while casting

        private const int BoltCastTime = 97;     // bolt cast lasts 97 ticks
        private const int BoltFireAt = 32;       // bolt fires at ai[0] == 32 (65 ticks in)
        private const int BoltCooldown = 20;     // shortest cooldown of the three attacks
        private const float BoltSpeed = 14f;

        private const int SummonCastTime = 183;  // summon cast lasts 183 ticks
        private const int SummonSoundAt = 80;    // cast sound at ai[0] == 80 (103 ticks in)
        private const int SummonFireAt = 64;     // orb + skeletons at ai[0] == 64 (119 ticks in)
        private const int SummonCooldown = 120;
        private const int MaxSkeletons = 8;

        // Attack selector values (vanilla ai[1]): 0 = bolt, 1 = heal (DROPPED),
        // 2 = summon. The selector alternates 0 -> 2 -> 0, skipping the heal.
        private const int AttackBolt = 0;
        private const int AttackSummon = 2;

        // Distinct hover offset so the boss minions don't stack on one point.
        private static readonly Vector2 HoverOffset = new Vector2(-260f, -60f);

        // --- State (fields; only the owner makes spawn decisions) ---
        // attackTimer: > 0 = an attack is running and counts down;
        // < 0 = cooldown, counts up toward 0; == 0 = pick the next attack.
        private int attackTimer;
        private int attackSelector;

        // Idle hover-cycle counter (vanilla FindFrame frameCounter for the
        // idle branch: += |velocity| * 0.1 + 1 per tick, wraps at 40,
        // frame = counter / 8). Reset to 0 while a cast runs (vanilla's
        // shared counter is always >= 40 when a cast ends, so the idle
        // cycle restarts at frame 0).
        private float idleCounter;

        // Cast animation sequences (vanilla FindFrame case 564/565).
        // Bolt: 5,6,7 x3 then 8-12, 7 ticks per step (14 steps = 98 ticks,
        // the cast is 97, so ~one pass). Summon: 26-29 x3 then 30-40,
        // 8 ticks per step (23 steps = 184 ticks, the cast is 183).
        // (The heal sequence 13-25 is not needed: the heal is dropped.)
        private static readonly int[] BoltFrames = { 5, 6, 7, 5, 6, 7, 5, 6, 7, 8, 9, 10, 11, 12 };
        private static readonly int[] SummonFrames = { 26, 27, 28, 29, 26, 27, 28, 29, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40 };

        public override void SetStaticDefaults()
        {
            // Hardcoded frame count (41 — the Dark Mage's texture is a
            // 5x9 grid of 80x80 cells; see FindFrame case 564/565).
            // PreDraw computes the grid cell itself; Main.npcFrameCount
            // is never consulted.
            Main.projFrames[Type] = 41;
        }

        public override void SetDefaults()
        {
            // Hitbox matches the 34x62 vanilla hitbox at 0.5 scale.
            Projectile.width = 17;
            Projectile.height = 31;
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
            float distToOwner = Vector2.Distance(Projectile.Center, player.Center);
            if (distToOwner > 2000f)
            {
                Projectile.Center = player.Center + HoverOffset;
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

            // Face the target (or the player when idle). Vanilla sets
            // spriteDirection = direction (AI_109 and every FindFrame
            // cast branch) — NOT -direction.
            Vector2 faceDir = target != null
                ? target.Center - Projectile.Center
                : player.Center - Projectile.Center;
            int direction = faceDir.X < 0f ? -1 : 1;
            Projectile.spriteDirection = direction;

            // Animation (vanilla FindFrame case 564/565): cast sequences
            // while casting, the hover cycle otherwise. NOT aim poses —
            // ai[2] is never used in FindFrame.
            UpdateAnimation();

            // Slight tilt with horizontal velocity (vanilla).
            Projectile.rotation = Projectile.velocity.X * 0.04f;

            // Violet mage light (vanilla AI_109): a line of light from
            // Top to Bottom, color (0.3, 0.05, 0.45) x 1.5.
            DelegateMethods.v3_1 = new Vector3(0.3f, 0.05f, 0.45f) * 1.5f;
            Utils.PlotTileLine(Projectile.Top, Projectile.Bottom, Projectile.width, DelegateMethods.CastLightOpen);

            // --- Attacks ---
            if (attackTimer > 0)
            {
                // An attack is running: stand still (vanilla: velocity *= 0.9).
                Projectile.velocity *= 0.9f;
                attackTimer--;

                if (attackSelector == AttackBolt && attackTimer == BoltFireAt)
                {
                    if (Projectile.owner == Main.myPlayer && target != null)
                        FireBolt(target, direction);
                }
                else if (attackSelector == AttackSummon && attackTimer == SummonSoundAt)
                {
                    SoundEngine.PlaySound(SoundID.DD2_DarkMageSummonSkeleton, Projectile.Center);
                }
                else if (attackSelector == AttackSummon && attackTimer == SummonFireAt)
                {
                    if (Projectile.owner == Main.myPlayer)
                        SummonSkeletons(target, direction);
                }

                if (attackTimer == 0)
                {
                    // Attack finished: cooldown, then advance the selector
                    // (0 -> 2 -> 0, skipping the dropped heal step 1).
                    attackTimer = attackSelector == AttackBolt ? -BoltCooldown : -SummonCooldown;
                    attackSelector = attackSelector == AttackBolt ? AttackSummon : AttackBolt;
                }
                return;
            }

            if (attackTimer < 0)
            {
                attackTimer++;
                // Keep hovering during the cooldown (fall through to movement).
            }
            else // attackTimer == 0: pick the next attack.
            {
                if (attackSelector == AttackBolt)
                {
                    // Bolt gate (vanilla): target within 1000 px + line of sight.
                    if (target != null && Collision.CanHitLine(Projectile.Center, 1, 1, target.Center, 1, 1))
                        attackTimer = BoltCastTime;
                    // Gate failed: keep hovering and retry next tick.
                }
                else // AttackSummon
                {
                    // Minion gate: skip the summon if the skeleton cap is
                    // already reached (the vanilla gate is graves; the minion
                    // has no graves, so the cap is the gate).
                    if (CountFriendlyNPCs(NPCID.DD2SkeletonT1) >= MaxSkeletons)
                    {
                        attackSelector = AttackBolt;
                    }
                    else
                    {
                        attackTimer = SummonCastTime;
                    }
                }
            }

            // --- Movement (only while no attack is running) ---
            if (target != null)
            {
                // Hold ~300 px from the target on the player's side.
                Vector2 holdPoint = target.Center + (player.Center - target.Center).SafeNormalize(Vector2.Zero) * HoldDistance;
                HoverToward(holdPoint, HoverSpeed, HoverAccel);
            }
            else
            {
                HoverToward(player.Center + HoverOffset, HoverSpeed, HoverAccel);
            }
        }

        private void FireBolt(NPC target, int direction)
        {
            // Muzzle: Center + (direction x 10, -16) vanilla; halved for the
            // 0.5-scale minion.
            Vector2 muzzle = Projectile.Center + new Vector2(direction * 5f, -8f);
            Vector2 vel = (target.Center - muzzle).SafeNormalize(Vector2.UnitX) * BoltSpeed;
            // The bolt sound (DD2_DarkMageAttack) plays from the projectile's
            // own AI on its first tick — free.
            SpawnFriendlyProjectile(muzzle, vel, ProjectileID.DD2DarkMageBolt);
        }

        private void SummonSkeletons(NPC target, int direction)
        {
            // The raise orb (673) is a pure visual that plays
            // DD2_SkeletonSummoned at its own ai[0] == 70. Spawn position:
            // Center + (direction x 24, -40) vanilla; halved for 0.5 scale.
            Vector2 orbPos = Projectile.Center + new Vector2(direction * 12f, -20f);
            SpawnFriendlyProjectile(orbPos, Vector2.Zero, ProjectileID.DD2DarkMageRaise, 0);

            // Spawn up to 8 friendly skeletons on solid ground near the
            // target (vanilla raises them at grave spots up to 850 px away).
            int existing = CountFriendlyNPCs(NPCID.DD2SkeletonT1);
            int toSpawn = MaxSkeletons - existing;
            Vector2 anchor = target != null ? target.Center : Projectile.Center;
            for (int i = 0; i < toSpawn; i++)
            {
                float x = anchor.X + Main.rand.Next(-300, 301);
                // Search straight down up to 50 tiles for solid ground.
                int tileX = (int)(x / 16f);
                int startTileY = (int)((anchor.Y - 100f) / 16f);
                float spawnY = anchor.Y;
                for (int tileY = startTileY; tileY < startTileY + 50; tileY++)
                {
                    Tile tile = Framing.GetTileSafely(tileX, tileY);
                    if (tile.HasTile && Main.tileSolid[tile.TileType] && !Main.tileSolidTop[tile.TileType])
                    {
                        spawnY = tileY * 16f - 60f;
                        break;
                    }
                }
                int idx = NPC.NewNPC(Projectile.GetSource_FromThis(), (int)x, (int)spawnY, NPCID.DD2SkeletonT1);
                if (idx >= 0 && idx < Main.maxNPCs)
                {
                    NPC skeleton = Main.npc[idx];
                    skeleton.friendly = true;
                    skeleton.netUpdate = true;
                }
            }
        }

        /// <summary>
        /// Vanilla FindFrame case 564/565. Cast frames are keyed on the
        /// elapsed cast ticks (the cast timer counts DOWN from the cast
        /// time, so elapsed = castTime - attackTimer at animation time —
        /// the attack branch decrements attackTimer AFTER this runs, so
        /// elapsed is 0 on the first cast tick, exactly like vanilla's
        /// frameCounter which starts at 0 when the cast begins).
        /// </summary>
        private void UpdateAnimation()
        {
            if (attackTimer > 0)
            {
                // A cast is running: step through its sequence.
                idleCounter = 0f;
                if (attackSelector == AttackBolt)
                {
                    int elapsed = BoltCastTime - attackTimer;
                    int step = Math.Min(elapsed / 7, BoltFrames.Length - 1);
                    Projectile.frame = BoltFrames[step];
                }
                else // AttackSummon
                {
                    int elapsed = SummonCastTime - attackTimer;
                    int step = Math.Min(elapsed / 8, SummonFrames.Length - 1);
                    Projectile.frame = SummonFrames[step];
                }
                return;
            }

            // Idle hover cycle: counter += |velocity| * 0.1 + 1 per tick,
            // wraps at 40, frame = counter / 8 -> frames 0-4 (8 ticks per
            // frame at standstill, faster while moving).
            idleCounter += Projectile.velocity.Length() * 0.1f + 1f;
            if (idleCounter >= 40f || idleCounter < 0f)
                idleCounter = 0f;
            Projectile.frame = (int)(idleCounter / 8f);
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

        private static int CountFriendlyNPCs(int type)
        {
            int count = 0;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (npc != null && npc.active && npc.type == type && npc.friendly)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Spawns a projectile owned by this minion's owner, flipped to
        /// friendly with local immunity frames — vanilla boss projectiles
        /// default to hostile, which would damage the player instead of
        /// enemies. Pass damage 0 for pure visuals (the raise orb).
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
            // Vanilla DrawNPC (type 564/565): the texture is a 5-column x
            // 9-row GRID of 80x80 cells and frame.Y is an INDEX, not a
            // pixel offset — texture.Frame(5, 9, index / 9, index % 9)
            // picks the cell. Drawing it as a vertical strip showed
            // garbage bands from the wrong part of the sheet.
            Rectangle frame = texture.Frame(5, 9, Projectile.frame / 9, Projectile.frame % 9);
            // Drawn from the Bottom, origin 10 px above the cell's bottom
            // edge and 52 px from the facing edge (verbatim from DrawNPC).
            Vector2 drawPos = Projectile.Bottom - Main.screenPosition;
            Vector2 origin = frame.Size() * new Vector2(0.5f, 1f);
            origin.Y -= 10f;
            origin.X = Projectile.spriteDirection == 1 ? 52f : frame.Width - 52f;
            // Vanilla flips opposite to the usual rule: SpriteEffects is
            // val16 ^ 1, i.e. facing right -> None, facing left -> Flip.
            SpriteEffects effects = Projectile.spriteDirection == 1 ? SpriteEffects.None : SpriteEffects.FlipHorizontally;

            // Shimmer ghosts (vanilla: 4 ghosts on a ring, radius 4 px x
            // shimmer at full scale — halved to 2 px for the 0.5-scale
            // minion; alpha x (1 - shimmer)).
            float shimmer = 0.625f + (float)Math.Sin(Main.GlobalTimeWrappedHourly * MathHelper.TwoPi * 0.75f + MathHelper.Pi) * 0.125f;
            for (int i = 0; i < 4; i++)
            {
                Color ghostColor = lightColor * (1f - shimmer);
                Vector2 ghostPos = drawPos + ((i / 4f) * MathHelper.TwoPi).ToRotationVector2() * 2f * shimmer;
                Main.EntitySpriteDraw(texture, ghostPos, frame, ghostColor, Projectile.rotation, origin, Projectile.scale, effects, 0);
            }

            Main.EntitySpriteDraw(texture, drawPos, frame, lightColor, Projectile.rotation, origin, Projectile.scale, effects, 0);

            // Glow mask (GlowMask[225]: White, alpha halved) + 4 glow
            // ghosts (color x 0.35 x (1 - pulse), same halved ring).
            Texture2D glow = TextureAssets.GlowMask[225].Value;
            Color glowColor = Color.White;
            glowColor.A /= 2;
            Main.EntitySpriteDraw(glow, drawPos, frame, glowColor, Projectile.rotation, origin, Projectile.scale, effects, 0);
            float glowPulse = 0.5f + (float)Math.Sin(Main.GlobalTimeWrappedHourly * MathHelper.TwoPi * 0.75f) * 0.5f;
            for (int i = 0; i < 4; i++)
            {
                Color glowGhostColor = glowColor * 0.35f * (1f - glowPulse);
                Vector2 ghostPos = drawPos + ((i / 4f) * MathHelper.TwoPi).ToRotationVector2() * 2f * glowPulse;
                Main.EntitySpriteDraw(glow, ghostPos, frame, glowGhostColor, Projectile.rotation, origin, Projectile.scale, effects, 0);
            }
            return false;
        }
    }
}
