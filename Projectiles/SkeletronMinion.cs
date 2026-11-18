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
    /// Skeletron boss minion (Brigitte's Charm, 2026-11-15): the vanilla
    /// Skeletron head (NPC 35, aiStyle 11) rewritten as a friendly minion
    /// at 50% visual scale. It runs the real boss's attack cycle — hover
    /// 250 px above its target while throwing skulls, then the spinning
    /// homing head charge — aimed at the owner's enemies instead of the
    /// player. Attack data from the decompiled Terraria 1.4.4.9 aiStyle 11
    /// block (see ~/workspace/tmp/boss_attacks_batch1.md, Part 1). The
    /// skull-shot math and hover steering were verified against the
    /// decompile (lines 27200-27360).
    ///
    /// FIXED 2026-11-18 (vs decompiled vanilla Terraria 1.4.5 NPC.cs +
    /// Main.DrawNPC, cross-checked for tML 1.4.4):
    /// - FRAME COUNT 2 -&gt; 1: the 1.4.4 texture NPC_35 is a single 102 px
    ///   frame. The backup's frameCount 2 sliced it into 51 px halves
    ///   and drew only the TOP HALF of the skull. (The 2nd frame is the
    ///   1.4.5 red-hat variant — FindFrame 80666 sets frame 1 only when
    ///   RedHatSkeletronAdjustmentsEnabled().) Proven by XNB byte
    ///   arithmetic on the 1.4.4 server's NPC_35.xnb (32817 bytes =
    ///   80x102 RGBA + reader overhead).
    /// - SKULL FIRST SHOT: vanilla's skull check (ai[2] % 40 == 0) runs
    ///   BEFORE ai[2]++ in the hover branch, so the skull fires on the
    ///   FIRST hover tick (ai[2] == 0) and every 40 after. The backup
    ///   skipped the first tick (stateTimer &gt; 0) and fired at 40/80/...
    ///   Now fires at stateTimer % 40 == 1 (first hover tick, then
    ///   every 40).
    /// - DRAW OFFSET: vanilla draws the sprite center 4 px below the NPC
    ///   center (Main.DrawNPC literal +4). Applied as +4 * scale (2 px
    ///   at 0.5 scale) so the skull sits where vanilla's does.
    /// - Added Projectile.netImportant = true (matches the weapon-minion
    ///   pattern; keeps the minion's state synced in multiplayer).
    /// - Doc fix: the Roar comment claimed LegacySoundStyle — that type
    ///   does NOT exist in tML 1.4.4 (verified against tModLoader.dll);
    ///   the path-based SoundStyle was already correct.
    ///
    /// VERIFIED CORRECT (no change): hover steering (separate V/H accel
    /// and caps, Classic/Expert), skull aim/jitter/spawn (center +
    /// vel*5), skull speed 5 / interval 40 / timeLeft 300, spin base
    /// speed 1.5/3.5 + distance ramp + no-hands x1.1, spin rotation
    /// += direction * 0.3, roar on the 2nd spin tick (vanilla increments
    /// ai[2] BEFORE the == 2 check in the spin branch), rotation =
    /// velocity.X / 15 while hovering, 40x51 halved hitbox, no spawn
    /// roar (vanilla plays it only for the Dungeon Guardian or in
    /// multiplayer), no frame animation (FindFrame: frame 0 always).
    ///
    /// Minion adaptations (the boss has no minion equivalent for these):
    /// - SINGLE MINION: vanilla Skeletron is three NPCs (head + 2 hands).
    ///   This minion is the skull only — no separate hand minions (per
    ///   the charm's design). The hand swipes (vertical dive, horizontal
    ///   sweep) are NOT ported. Because the minion has no hands, the
    ///   "both hands dead" conditions are always true: the skull fires
    ///   at the faster cadence (every 40 ticks instead of 80) and at
    ///   speed 5 (instead of 3), and the spin gets the no-hands x1.1
    ///   multiplier.
    /// - The skull shot is Expert-only in vanilla; the minion fires it
    ///   in all modes (the charm is an endgame item — the minion is the
    ///   boss at its best). Classic/Expert NUMBERS (hover accel/speeds,
    ///   spin base speed and ramp) still follow the world difficulty
    ///   via Main.expertMode, like the other boss minions.
    /// - Daytime enrage (9999 damage "Dungeon Guardian" mode) is NOT
    ///   ported (flagged do-not-port — it is a punishment, not an
    ///   attack). Dark Caster summons are NOT ported (For the Worthy /
    ///   Zenith only). Despawn is NOT ported.
    /// - The defense drop during the spin (-10) is not ported (minions
    ///   don't take damage).
    /// - All damage is flat 6000, typeless (DamageClass.Generic), per
    ///   the charm's design — the vanilla skull damage (17 flat) and
    ///   contact numbers in the spec are informational only. Contact
    ///   damage comes from the minion body overlapping enemies
    ///   (penetrate -1, local immunity frames).
    /// - The skull is the vanilla ProjectileID.Skull (270), spawned
    ///   friendly: its hostile flag is flipped after spawn (vanilla
    ///   boss projectiles set hostile = true in SetDefaults), exactly
    ///   like the Twins minions' lasers. Same speed, same aim jitter,
    ///   same head-velocity inheritance, same 300-tick timeLeft. No
    ///   substitution was needed — the skull's AI has no hardcoded
    ///   owner or player target.
    /// - Sounds: Roar on entering the spinning charge (2 ticks in, per
    ///   vanilla). NO spawn roar: in vanilla it only plays for the
    ///   Dungeon Guardian or in multiplayer (single-player Skeletron
    ///   does not roar on spawn). No sound on the skull shot or the
    ///   hover (vanilla is silent there). Roar uses the path-based
    ///   SoundStyle "Terraria/Sounds/Roar_0" because SoundID.Roar is a
    ///   const int, not a SoundStyle (same as the Twins minions).
    /// </summary>
    public class SkeletronMinion : ModProjectile
    {
        public override string Texture => "Terraria/Images/NPC_35";

        // --- Spec constants (vanilla aiStyle 11, Skeletron head) ---
        private const float TargetRange = 1000f;
        private const int MinionDamage = 6000;     // flat, typeless (charm design)

        // Hover offset near the player (no target): above-RIGHT, so the
        // boss minions don't all stack on one point.
        private static readonly Vector2 IdleOffset = new Vector2(140f, -160f);
        // Fighting hover: 250 px above the target (vanilla), kept right
        // for anti-stacking.
        private static readonly Vector2 FightHoverOffset = new Vector2(140f, -250f);

        // Hover (800 ticks): separate vertical / horizontal accel and max
        // speeds, ported verbatim from the decompile.
        private const int HoverTime = 800;
        private const float HoverAccelV = 0.02f;   // Classic (Expert: 0.03)
        private const float HoverAccelVExpert = 0.03f;
        private const float HoverMaxV = 2f;        // Classic (Expert: 4)
        private const float HoverMaxVExpert = 4f;
        private const float HoverAccelH = 0.05f;   // Classic (Expert: 0.07)
        private const float HoverAccelHExpert = 0.07f;
        private const float HoverMaxH = 8f;        // Classic (Expert: 9.5)
        private const float HoverMaxHExpert = 9.5f;

        // Skull shot (Attack 1, Expert): both-hands-dead cadence and
        // speed, since the minion has no hands (80 ticks / speed 3 with
        // hands alive).
        private const int SkullInterval = 40;
        private const float SkullSpeed = 5f;
        private const int SkullTimeLeft = 300;

        // Spinning head charge (Attack 2): 400 ticks of homing contact.
        private const int SpinTime = 400;
        private const float SpinBaseSpeed = 1.5f;  // Classic (Expert: 3.5)
        private const float SpinBaseSpeedExpert = 3.5f;
        private const float SpinNoHandsMult = 1.1f; // 0 living hands (always true here)
        private const float SpinRotation = 0.3f;   // rad/tick
        private const int SpinRoarTick = 2;        // roar plays 2 ticks into the spin

        // Roar (legacy sound type 15, style 0). SoundID.Roar is a const
        // int, not a SoundStyle — use the path-based SoundStyle
        // equivalent. (LegacySoundStyle does not exist in tML 1.4.4.)
        private static readonly SoundStyle RoarSound = new SoundStyle("Terraria/Sounds/Roar_0");

        // States (Projectile.ai[0])
        private const int StateHover = 0;
        private const int StateSpin = 1;

        // --- State (instance fields; ai[0] is the synced state) ---
        private NPC target = null;
        private int retargetTimer = 0;
        private int stateTimer = 0;      // ticks in the current state

        public override void SetDefaults()
        {
            // Vanilla Skeletron head hitbox 80x102, halved for the minion
            // (0.5 scale).
            Projectile.width = 40;
            Projectile.height = 51;
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
                Projectile.rotation = Projectile.velocity.X / 15f;
                return;
            }

            bool expert = Main.expertMode || Main.masterMode;
            int state = (int)Projectile.ai[0];

            switch (state)
            {
                case StateHover: Hover(expert); break;
                case StateSpin: Spin(expert); break;
                default: SetState(StateHover); break;
            }
        }

        // --- Hover + skull shots (Attack 1) ---

        private void Hover(bool expert)
        {
            stateTimer++;

            float accelV = expert ? HoverAccelVExpert : HoverAccelV;
            float maxV = expert ? HoverMaxVExpert : HoverMaxV;
            float accelH = expert ? HoverAccelHExpert : HoverAccelH;
            float maxH = expert ? HoverMaxHExpert : HoverMaxH;

            // Hover steering, ported verbatim from the decompile (with the
            // +140 px X offset folded into the horizontal target so the
            // minions don't stack). Vertical: stay 250 px above the
            // target's top. Horizontal: center on the target's X.
            float hoverY = target.position.Y + FightHoverOffset.Y;
            if (Projectile.position.Y > hoverY)
            {
                if (Projectile.velocity.Y > 0f)
                    Projectile.velocity.Y *= 0.98f;
                Projectile.velocity.Y -= accelV;
                if (Projectile.velocity.Y > maxV)
                    Projectile.velocity.Y = maxV;
            }
            else if (Projectile.position.Y < hoverY)
            {
                if (Projectile.velocity.Y < 0f)
                    Projectile.velocity.Y *= 0.98f;
                Projectile.velocity.Y += accelV;
                if (Projectile.velocity.Y < -maxV)
                    Projectile.velocity.Y = -maxV;
            }

            float hoverX = target.Center.X + FightHoverOffset.X;
            if (Projectile.Center.X > hoverX)
            {
                if (Projectile.velocity.X > 0f)
                    Projectile.velocity.X *= 0.98f;
                Projectile.velocity.X -= accelH;
                if (Projectile.velocity.X > maxH)
                    Projectile.velocity.X = maxH;
            }
            if (Projectile.Center.X < hoverX)
            {
                if (Projectile.velocity.X < 0f)
                    Projectile.velocity.X *= 0.98f;
                Projectile.velocity.X += accelH;
                if (Projectile.velocity.X < -maxH)
                    Projectile.velocity.X = -maxH;
            }

            // The head tilts slightly with its horizontal velocity (vanilla).
            Projectile.rotation = Projectile.velocity.X / 15f;

            // Skull shot: every 40 ticks, line of sight required. Vanilla
            // checks ai[2] % 40 == 0 BEFORE ai[2]++, so it fires on the
            // FIRST hover tick (ai[2] == 0) and every 40 after. stateTimer
            // is incremented at the top of Hover(), so it is 1 on the
            // first hover tick: fire at % 40 == 1.
            if (stateTimer % SkullInterval == 1 && HasLineOfSight(target))
                FireSkull();

            if (stateTimer >= HoverTime)
                SetState(StateSpin);
        }

        // Skull shot (Attack 1), ported verbatim from the decompile:
        // aim = target center + jitter +/-20 px per axis, normalized to
        // speed, then +/-0.5 velocity jitter per axis, re-normalized,
        // plus the head's own velocity. Spawn position = head center +
        // final velocity x 5. Damage 6000 (charm flat), knockback 0,
        // timeLeft 300. Spawned friendly (hostile flag flipped after
        // spawn — the vanilla skull sets hostile = true in SetDefaults).
        private void FireSkull()
        {
            if (Projectile.owner != Main.myPlayer)
                return;
            Vector2 aim = target.Center + new Vector2(Main.rand.Next(-20, 21), Main.rand.Next(-20, 21));
            Vector2 vel = (aim - Projectile.Center).SafeNormalize(Vector2.UnitY) * SkullSpeed;
            vel.X += Main.rand.Next(-50, 51) * 0.01f;
            vel.Y += Main.rand.Next(-50, 51) * 0.01f;
            vel = vel.SafeNormalize(Vector2.UnitY) * SkullSpeed;
            vel += Projectile.velocity;
            Vector2 spawnPos = Projectile.Center + vel * 5f;
            int p = Projectile.NewProjectile(Projectile.GetSource_FromThis(), spawnPos, vel,
                ProjectileID.Skull, MinionDamage, 0f, Projectile.owner);
            if (p >= 0 && p < Main.maxProjectiles)
            {
                Main.projectile[p].friendly = true;
                Main.projectile[p].hostile = false;
                Main.projectile[p].usesLocalNPCImmunity = true;
                Main.projectile[p].localNPCHitCooldown = 20;
                Main.projectile[p].timeLeft = SkullTimeLeft;
            }
        }

        // --- Spinning head charge (Attack 2) ---

        private void Spin(bool expert)
        {
            stateTimer++;

            // Roar 2 ticks into the spin (vanilla: ai[2]++ runs BEFORE
            // the == 2 check in the spin branch, so the roar lands on
            // the 2nd spin tick).
            if (stateTimer == SpinRoarTick)
                SoundEngine.PlaySound(RoarSound, Projectile.Center);

            // Continuous homing: velocity is set directly toward the
            // target every tick. Expert: base 3.5 with a distance ramp
            // (x1.05 past 150 px, then x1.1 at each 50 px step past 200 /
            // 250 / 300 / 350 / 400 / 450 / 500 / 550 / 600 px, max
            // ~7.9) and the no-hands x1.1. Classic: flat 1.5.
            float speed;
            if (expert)
            {
                speed = SpinBaseSpeedExpert;
                float dist = Vector2.Distance(Projectile.Center, target.Center);
                if (dist > 150f) speed *= 1.05f;
                if (dist > 200f) speed *= 1.1f;
                if (dist > 250f) speed *= 1.1f;
                if (dist > 300f) speed *= 1.1f;
                if (dist > 350f) speed *= 1.1f;
                if (dist > 400f) speed *= 1.1f;
                if (dist > 450f) speed *= 1.1f;
                if (dist > 500f) speed *= 1.1f;
                if (dist > 550f) speed *= 1.1f;
                if (dist > 600f) speed *= 1.1f;
                speed *= SpinNoHandsMult;
            }
            else
            {
                speed = SpinBaseSpeed;
            }
            Vector2 dir = (target.Center - Projectile.Center).SafeNormalize(Vector2.UnitX);
            Projectile.velocity = dir * speed;

            // The head spins: rotation += direction x 0.3 rad/tick.
            Projectile.direction = target.Center.X >= Projectile.Center.X ? 1 : -1;
            Projectile.rotation += Projectile.direction * SpinRotation;

            if (stateTimer >= SpinTime)
                SetState(StateHover);
        }

        // --- Shared helpers ---

        private void SetState(int state)
        {
            Projectile.ai[0] = state;
            stateTimer = 0;
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

        private bool HasLineOfSight(NPC npc)
        {
            return Collision.CanHit(Projectile.Center, 1, 1, npc.Center, 1, 1);
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

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D texture = TextureAssets.Projectile[Projectile.type].Value;
            // Frame count is HARDCODED (1 in tML 1.4.4 — Skeletron's
            // texture is a single 102 px frame; the 2nd frame is the
            // 1.4.5 red-hat variant). Main.npcFrameCount returns 0/1 for
            // NPCs whose textures haven't loaded yet, which drew the
            // whole sprite sheet. Skeletron has no frame animation
            // (vanilla FindFrame never cycles frames — frame 0 always),
            // so Projectile.frame stays 0 and the skull's motion is all
            // rotation. The frame index is still read from
            // Projectile.frame so the draw stays correct if that changes.
            const int frameCount = 1;
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
