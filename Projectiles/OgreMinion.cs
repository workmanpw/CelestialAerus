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
    /// Ogre (NPC 576, DD2OgreT2) as a Brigitte's Charm boss minion
    /// (2026-11-15). The Old One's Army bruiser with his real attacks,
    /// retargeted at enemies: melee swipe, leap / ground-pound slam, and
    /// spit. Half scale, flat 6000 typeless damage, local immunity frames.
    ///
    /// Attack data from the decompiled Terraria NPC AI (AI_107_ImprovedWalkers,
    /// Ogre switch + shared attack blocks): see
    /// ~/workspace/tmp/boss_attacks_event.md, Ogre section.
    /// Frame data from decompiled FindFrame case 576/577 (NOT from
    /// boss_frames.md — its "6 frames: idle 0, walk 1-5" claim is FALSE):
    /// the texture is a 5-column x 10-row GRID of 80x80 cells, 48 frames
    /// total. Melee plays 11-20, spit plays 21-38, leap plays 39-47 then
    /// wraps to 37 and sticks at 38; idle is 0, walk is 1-10, airborne
    /// is 43.
    ///
    /// FIXES (2026-11-18, vs decompiled AI_107 + FindFrame case 576/577
    /// + DrawNPC): (1) the sprite was BROKEN — the backup drew a 6-frame
    /// vertical strip with an invented walk cycle, so the game showed
    /// garbage bands from the wrong part of the sheet, and there were no
    /// attack frames at all. Rewritten to the 48-frame grid with
    /// vanilla's melee/spit/leap/walk sequences, the Bottom anchor and
    /// the (94 / W-94, H-4) origin; (2) spriteDirection was -direction
    /// — vanilla sets spriteDirection = direction (AI_107 and every
    /// FindFrame branch); (3) the leap added invented +/-6 forward
    /// speed — vanilla sets ONLY velocity.Y = -13 at the jump; (4) the
    /// attack damping exempted the leap once airborne — vanilla damps
    /// velocity.X x 0.7 on EVERY attack tick; (5) the leap recast
    /// cooldown was set at the trigger — vanilla refreshes localAI[0]
    /// = 300 every tick while the leap runs, so the 5 s gap starts when
    /// the leap ENDS; (6) the spit/smash spawn was missing vanilla's
    /// forward nudge (spawn += un-jittered velocity x 1, halved to
    /// 0.5); (7) the null-owner branch dereferenced player.Center when
    /// player could be null (NullReferenceException) — it now just
    /// damps velocity; (8) follow/recall updated to the 2026-11-18
    /// weapon-minion pattern (2000 px teleport, ramped follow speed).
    ///
    /// ADAPTED — MOVEMENT: the vanilla Ogre is a ground walker (walk speed
    /// 1.0 px/tick, gravity, tile collision). The charm minion has
    /// tileCollide = false, so it FLIES toward enemies at 4 px/tick instead
    /// of walking — a ground-only minion at Ogre speed would lag badly
    /// behind the player. The attack state machine (selection, triggers,
    /// cooldowns, the leap's landing-locked slam) is reproduced verbatim.
    ///
    /// ADAPTED — LEAP LANDING: with no tile collision there is no ground to
    /// land on. The leap is a ballistic arc (vy -13, gravity 0.4/tick
    /// applied manually); "landing" is falling back to the Y the leap
    /// started from. The vanilla freeze-at-41 mechanic is kept: the attack
    /// timer cannot advance past 41 while airborne, so the ground-pound
    /// sound and the smash are guaranteed to fire after landing, never
    /// mid-air. The smash AoE (640 px diameter) covers the target either way.
    ///
    /// NO BOULDER: the Ogre has no boulder attack — its only ranged attack
    /// is the spit (676). Projectile 670 (DD2OgreStomp) has no spawner in
    /// the decompiled code and is not part of the kit.
    ///
    /// SOUNDS: DD2_OgreRoar (ambient 1/700 per tick while chasing),
    /// DD2_OgreAttack (melee), DD2_OgreSpit (spit telegraph),
    /// DD2_OgreGroundPound (landing-locked). The glob's spawn sound
    /// (Item17) plays from the projectile's own AI, free.
    /// </summary>
    public class OgreMinion : ModProjectile
    {
        public override string Texture => "Terraria/Images/NPC_576";
        // DisplayName comes from the localization file
        // (Mods.CelestialAerus.Projectiles.OgreMinion.DisplayName).

        // --- Constants (vanilla AI_107 values) ---
        private const int MinionDamage = 6000;
        private const float TargetRange = 1000f;

        private const float ChaseSpeed = 4f;     // vanilla walk is 1.0; flying minion is faster
        private const float ChaseAccel = 0.2f;

        // Attack selector values (vanilla ai[1]).
        private const int AttackMelee = 0;
        private const int AttackSpit = 1;
        private const int AttackLeap = 2;

        // Selection ranges (vanilla): leap selected <= 430 with cooldown
        // ready; spit selected > 160; melee selected <= 130.
        private const float LeapSelectRange = 430f;
        private const float SpitSelectRange = 160f;
        private const float MeleeSelectRange = 130f;

        // Trigger ranges (vanilla): the attack only starts inside these.
        private const float MeleeTriggerRange = 130f;
        private const float LeapTriggerRange = 250f;
        private const float SpitTriggerRange = 1000f;

        // Attack durations and cooldowns (vanilla).
        private const int MeleeDuration = 44;
        private const int MeleeCooldown = 60;
        private const int MeleeSoundAt = 40;     // DD2_OgreAttack, 4 ticks in

        private const int SpitDuration = 90;
        private const int SpitCooldown = 240;    // longest cooldown of the three
        private const int SpitTelegraphAt = 80;  // DD2_OgreSpit, 10 ticks in
        private const int SpitFireAt = 35;       // glob fires, 55 ticks in
        private const float SpitSpeed = 11f;

        private const int LeapDuration = 90;
        private const int LeapCooldown = 60;
        private const int LeapJumpAt = 56;       // jump at ai[0] == 56 (34 ticks in)
        private const int LeapFreezeAt = 41;     // timer frozen at 41 while airborne
        private const int LeapPoundAt = 40;      // DD2_OgreGroundPound, 1 tick after landing
        private const int LeapSmashAt = 36;      // smash fires, 5 ticks after landing
        private const int LeapRecastCooldown = 300; // 5 s between leaps
        private const float LeapJumpVelocity = -13f;
        private const float LeapGravity = 0.4f;

        // Distinct hover offset so the boss minions don't stack on one point.
        private static readonly Vector2 HoverOffset = new Vector2(300f, 40f);

        // --- State (fields; only the owner makes spawn decisions) ---
        // attackTimer: > 0 = an attack is running and counts down;
        // < 0 = cooldown, counts up toward 0; == 0 = idle, may trigger.
        private int attackTimer;
        private int attackSelector;
        private int leapCooldown;
        private bool leaping;
        private float leapStartY;
        // Shared animation counter (vanilla FindFrame frameCounter):
        // per-frame step timer in the attack branches, speed-scaled walk
        // accumulator in the walk branch.
        private float animCounter;

        public override void SetStaticDefaults()
        {
            // Hardcoded frame count (48 — the Ogre's texture is a 5x10
            // grid of 80x80 cells; see FindFrame case 576/577). PreDraw
            // computes the grid cell itself; Main.npcFrameCount is never
            // consulted.
            Main.projFrames[Type] = 48;
        }

        public override void SetDefaults()
        {
            // Hitbox matches the 96x124 vanilla hitbox at 0.5 scale.
            Projectile.width = 48;
            Projectile.height = 62;
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
                leaping = false;
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
            // spriteDirection = direction (AI_107 and every FindFrame
            // branch) — NOT -direction.
            Vector2 faceDir = target != null
                ? target.Center - Projectile.Center
                : player.Center - Projectile.Center;
            int direction = faceDir.X < 0f ? -1 : 1;
            Projectile.spriteDirection = direction;

            if (leapCooldown > 0)
                leapCooldown--;

            // --- Attack selection (vanilla: re-picked every tick while
            // attackTimer <= 0) ---
            if (attackTimer <= 0 && target != null)
            {
                float dist = Vector2.Distance(Projectile.Center, target.Center);
                if (dist <= LeapSelectRange && leapCooldown <= 0)
                {
                    attackSelector = AttackLeap;
                }
                else if (dist > SpitSelectRange)
                {
                    attackSelector = AttackSpit;
                }
                else if (dist <= MeleeSelectRange)
                {
                    // If the previous attack was spit, force the timer to 0
                    // so the melee can trigger immediately (vanilla).
                    if (attackSelector == AttackSpit)
                        attackTimer = 0;
                    attackSelector = AttackMelee;
                }
                // 131-160 px with the leap on cooldown: keep the previous
                // selector (vanilla edge case — no branch matches).
            }

            // --- Attack trigger (vanilla flag5 block): idle, has a target,
            // line of sight, inside the selected attack's trigger range ---
            if (attackTimer == 0 && target != null)
            {
                float dist = Vector2.Distance(Projectile.Center, target.Center);
                float triggerRange = attackSelector == AttackMelee ? MeleeTriggerRange
                    : attackSelector == AttackLeap ? LeapTriggerRange : SpitTriggerRange;
                if (dist < triggerRange && Collision.CanHit(Projectile.Center, 1, 1, target.Center, 1, 1))
                {
                    attackTimer = attackSelector == AttackMelee ? MeleeDuration
                        : attackSelector == AttackLeap ? LeapDuration : SpitDuration;
                    // (The leap recast cooldown is refreshed while the
                    // leap runs, not at the trigger — see the
                    // attack-running block below.)
                }
            }

            // --- Attack running ---
            if (attackTimer > 0)
            {
                // While any attack runs, the Ogre stops (vanilla AI_107:
                // velocity.X *= 0.7 on EVERY attack tick, including while
                // airborne during the leap — there is no leap exemption).
                Projectile.velocity.X *= 0.7f;

                // While the leap attack runs, the leap recast cooldown is
                // refreshed to 300 every tick (vanilla: localAI[0] = 300
                // while ai[0] > 0 && ai[1] == 2) — so the 5 s gap starts
                // when the leap ENDS, not when it triggers.
                if (attackSelector == AttackLeap)
                    leapCooldown = LeapRecastCooldown;

                attackTimer--;

                // Leap airborne freeze (vanilla): the timer cannot advance
                // past 41 until the Ogre lands.
                if (attackSelector == AttackLeap && leaping && attackTimer < LeapFreezeAt)
                    attackTimer = LeapFreezeAt;

                if (attackSelector == AttackMelee)
                {
                    if (attackTimer == MeleeSoundAt)
                        SoundEngine.PlaySound(SoundID.DD2_OgreAttack, Projectile.Center);
                    // Contact damage is the minion's own body — no projectile.
                    if (attackTimer == 0)
                        attackTimer = -MeleeCooldown;
                }
                else if (attackSelector == AttackSpit)
                {
                    if (attackTimer == SpitTelegraphAt)
                        SoundEngine.PlaySound(SoundID.DD2_OgreSpit, Projectile.Center);
                    if (attackTimer == SpitFireAt && Projectile.owner == Main.myPlayer && target != null)
                        FireSpit(target, direction);
                    if (attackTimer == 0)
                        attackTimer = -SpitCooldown;
                }
                else // AttackLeap
                {
                    if (attackTimer == LeapJumpAt)
                    {
                        // Jump: vanilla sets ONLY velocity.Y = -13 — no
                        // horizontal velocity is added (the Ogre leaps
                        // straight up and the smash's 640 px AoE covers
                        // the target).
                        Projectile.velocity.Y = LeapJumpVelocity;
                        leaping = true;
                        leapStartY = Projectile.Center.Y;
                    }
                    if (leaping)
                    {
                        // Manual gravity (the minion has no tile collision).
                        Projectile.velocity.Y += LeapGravity;
                        // Landing: fell back to the leap's start height.
                        if (Projectile.velocity.Y > 0f && Projectile.Center.Y >= leapStartY)
                        {
                            leaping = false;
                            Projectile.velocity.Y = 0f;
                            Projectile.velocity.X *= 0.7f;
                        }
                    }
                    if (attackTimer == LeapPoundAt)
                        SoundEngine.PlaySound(SoundID.DD2_OgreGroundPound, Projectile.Center);
                    if (attackTimer == LeapSmashAt && Projectile.owner == Main.myPlayer)
                        FireSmash(target, direction);
                    if (attackTimer == 0)
                        attackTimer = -LeapCooldown;
                }
            }
            else
            {
                // --- Movement (only while no attack is running) ---
                if (attackTimer < 0)
                    attackTimer++;

                if (target != null)
                {
                    FlyToward(target.Center, ChaseSpeed, ChaseAccel);
                    // Ambient roar: 1/700 per tick while chasing, not attacking.
                    if (Main.rand.Next(700) == 0)
                        SoundEngine.PlaySound(SoundID.DD2_OgreRoar, Projectile.Center);
                }
                else
                {
                    FlyToward(player.Center + HoverOffset, ChaseSpeed, ChaseAccel);
                }
            }

            // --- Animation (vanilla FindFrame case 576/577) ---
            UpdateAnimation();
        }

        private void UpdateAnimation()
        {
            // Vanilla FindFrame case 576/577. The frame index is stored in
            // Projectile.frame; animCounter is the shared frameCounter.
            int frame = Projectile.frame;

            if (attackTimer > 0 && attackSelector == AttackMelee)
            {
                // Melee: enter at 11 (if outside 11-20); durations 13/19:
                // 8t, 14/18: 2t, else 4t; advance while < 20, hold at 20
                // (40 ticks to reach 20; the melee lasts 44).
                if (frame < 11 || frame > 20)
                {
                    frame = 11;
                    animCounter = 0f;
                }
                int duration = 4;
                if (frame == 13 || frame == 19)
                    duration = 8;
                if (frame == 14 || frame == 18)
                    duration = 2;
                if (++animCounter >= duration && frame < 20)
                {
                    animCounter = 0f;
                    frame++;
                }
            }
            else if (attackTimer > 0 && attackSelector == AttackLeap)
            {
                // Leap: enter at 39 (if outside 37-47); durations 42: 6t,
                // 45: 8t, 46: 4t, 47: 26t, 37/38: 7t, else 5t. Frame 46
                // pauses while airborne; frame 38 pauses ALWAYS (the
                // sequence sticks at 38 after wrapping from 47 to 37 — a
                // vanilla quirk, kept verbatim). At 47 the sequence wraps
                // to 37.
                if (frame < 37 || frame > 47)
                {
                    frame = 39;
                    animCounter = 0f;
                }
                int duration = 5;
                if (frame == 42)
                    duration = 6;
                if (frame == 45)
                    duration = 8;
                if (frame == 46)
                    duration = 4;
                if (frame == 47)
                    duration = 26;
                if (frame == 37 || frame == 38)
                    duration = 7;
                bool advance = true;
                if (frame == 46 && leaping)
                    advance = false;
                if (frame == 38)
                    advance = false;
                if (advance)
                    animCounter++;
                if (animCounter >= duration)
                {
                    if (frame < 47)
                    {
                        animCounter = 0f;
                        frame++;
                    }
                    else
                    {
                        frame = 37;
                        animCounter = 0f;
                    }
                }
            }
            else if (attackTimer > 0 && attackSelector == AttackSpit)
            {
                // Spit: enter at 21 (if outside 21-38); 5 t/f; advance
                // while < 38, hold at 38 (85 ticks to reach 38; the spit
                // lasts 90).
                if (frame < 21 || frame > 38)
                {
                    frame = 21;
                    animCounter = 0f;
                }
                if (++animCounter >= 5f && frame < 38)
                {
                    animCounter = 0f;
                    frame++;
                }
            }
            else
            {
                // No attack running. (Vanilla's airborne branch — frame 43
                // when velocity.Y != 0 — cannot occur for the flying
                // minion: the only airborne state is the leap, which is
                // handled by the leap branch above.)
                if (Math.Abs(Projectile.velocity.X) <= 0.5f)
                {
                    animCounter = 0f;
                    frame = 0;
                }
                else
                {
                    // Walk: counter += |vX|, wraps at 60, frame =
                    // 1 + counter / 6 -> frames 1-10.
                    animCounter += Math.Abs(Projectile.velocity.X);
                    if (animCounter >= 60f || animCounter < 0f)
                        animCounter = 0f;
                    frame = 1 + (int)(animCounter / 6f);
                }
            }

            Projectile.frame = frame;
        }

        private void FireSpit(NPC target, int direction)
        {
            // Mouth: Center + (direction x 30, -70) vanilla; halved for the
            // 0.5-scale minion.
            Vector2 mouth = Projectile.Center + new Vector2(direction * 15f, -35f);
            Vector2 aim = target.Center - mouth;
            // Lobbed-arc compensation: aim slightly above the target.
            aim.Y -= Math.Abs(aim.X) * 0.1f;
            // Vanilla fires from the mouth nudged FORWARD along the
            // un-jittered aim velocity (val16 += val14 * num18, num18 = 1)
            // — halved to 0.5 for the 0.5-scale minion. The jitter
            // applies to the velocity only.
            Vector2 baseVel = aim.SafeNormalize(Vector2.UnitX) * SpitSpeed;
            Vector2 vel = baseVel + Utils.RandomVector2(Main.rand, -0.5f, 0.5f);
            // The glob's spawn sound (Item17) plays from the projectile's
            // own AI on its first tick — free.
            SpawnFriendlyProjectile(mouth + baseVel * 0.5f, vel, ProjectileID.DD2OgreSpit);
        }

        private void FireSmash(NPC target, int direction)
        {
            // Spawn offset: Center + (-direction x 20, height/2 - 1) vanilla;
            // the X offset is halved for the 0.5-scale minion. The spawn
            // velocity (11 px/tick at the target) is discarded by the
            // projectile's AI — the smash expands in place.
            Vector2 pos = Projectile.Center + new Vector2(-direction * 10f, Projectile.height / 2f - 1f);
            Vector2 vel = target != null
                ? (target.Center - pos).SafeNormalize(Vector2.UnitX) * 11f
                : Vector2.Zero;
            // Same forward nudge as the spit (vanilla val16 += val14 * num18).
            SpawnFriendlyProjectile(pos + vel * 0.5f, vel, ProjectileID.DD2OgreSmash);
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
                Main.projectile[p].DamageType = DamageClass.Generic;
                Main.projectile[p].usesLocalNPCImmunity = true;
                Main.projectile[p].localNPCHitCooldown = 20;
            }
            return p;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D texture = TextureAssets.Projectile[Projectile.type].Value;
            // Vanilla DrawNPC (type 576/577): the texture is a 5-column x
            // 10-row GRID of 80x80 cells and frame.Y is an INDEX, not a
            // pixel offset — texture.Frame(5, 10, index / 10, index % 10)
            // picks the cell. Drawing it as a vertical strip showed
            // garbage bands from the wrong part of the sheet.
            Rectangle frame = texture.Frame(5, 10, Projectile.frame / 10, Projectile.frame % 10);
            // Drawn from the Bottom, origin 4 px above the cell's bottom
            // edge and 94 px from the facing edge (verbatim from DrawNPC).
            Vector2 drawPos = Projectile.Bottom - Main.screenPosition;
            Vector2 origin = frame.Size() * new Vector2(0.5f, 1f);
            origin.Y -= 4f;
            origin.X = Projectile.spriteDirection == 1 ? 94f : frame.Width - 94f;
            // Vanilla flips opposite to the usual rule: SpriteEffects is
            // val16 ^ 1, i.e. facing right -> None, facing left -> Flip.
            SpriteEffects effects = Projectile.spriteDirection == 1 ? SpriteEffects.None : SpriteEffects.FlipHorizontally;
            Main.EntitySpriteDraw(texture, drawPos, frame, lightColor, Projectile.rotation, origin, Projectile.scale, effects, 0);
            return false;
        }
    }
}
