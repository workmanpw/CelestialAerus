using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace CelestialAerus.Projectiles
{
    /// <summary>
    /// King Slime boss minion (Brigitte's Charm, 2026-11-15): the vanilla
    /// King Slime (NPC 50, aiStyle 15) rewritten as a friendly minion at
    /// 50% visual scale. It runs the real boss's hop cycle (3 small hops
    /// then 1 big jump, accelerating as its owner loses life), teleports
    /// to close gaps it cannot hop across, and spawns friendly
    /// mini-slimes when its owner takes a hit. Attack data from the
    /// decompiled Terraria 1.4.4.9 AI_015_KingSlime (see
    /// ~/workspace/tmp/boss_attacks_batch1.md, Part 1).
    ///
    /// FIXED 2026-11-18 (vs decompiled vanilla Terraria 1.4.5 NPC.cs
    /// AI_015_KingSlime, lines 50119-50480, and FindFrame case 50,
    /// lines 87359-87390):
    /// - TELEPORT TRIGGER WAS WRONG: vanilla's ai[2] is CUMULATIVE — it
    ///   increments on every no-LOS tick and NEVER resets on LOS return
    ///   (only the anti-cheese localAI[0] decays; ai[2] resets only when
    ///   the teleport fires). The backup reset noLOSTimer to 0 whenever
    ///   LOS returned, so the teleport almost never fired in cluttered
    ///   terrain. Reset removed. Also vanilla only triggers while
    ///   GROUNDED (velocity.Y == 0) — the backup could trigger mid-hop.
    ///   Now restricted to the charging (grounded) state. The vertical
    ///   check is now the King's TOP vs the target's BOTTOM (vanilla),
    ///   not center-to-center.
    /// - AMBIENT DUST MISSING: vanilla spawns 1 dust EVERY tick (type 4,
    ///   color (0,80,255,80), alpha 255, scale x1.2, noGravity, velocity
    ///   x0.5) in all states. The backup had none. Added.
    /// - TELEPORT DUST/GORE: vanilla spawns 10 blue dust per tick
    ///   (type 4, color (78,136,255,80), alpha 150, scale 2, noGravity,
    ///   velocity x0.5 in the telegraph, x2 in the regrow) in BOTH
    ///   teleport states, plus gore 734 at tick 60 of the telegraph
    ///   (spawned at Center + (-40, -height/2), halved here). The backup
    ///   had a sparse 1-in-2 single Water dust in the telegraph only.
    ///   Fixed (dust spread halved for the 0.5-scale minion).
    /// - FRAME WIND-UP: vanilla's FindFrame accelerates the grounded
    ///   cycle when aiAction == 1 (charge &gt;= -30, the last 30 ticks
    ///   before the hop): frameCounter += 3/tick instead of 1. The
    ///   backup always used 1. Also +2/tick while moving horizontally
    ///   (vanilla num2 == 1). Fixed.
    /// - TELEPORT FRAMES: vanilla does NOT freeze the frame during the
    ///   teleport — the grounded cycle keeps running (fast, +3/tick,
    ///   during the telegraph since aiAction == 1; normal during the
    ///   regrow). The backup froze at frame 0. Fixed.
    /// - GROUNDED SETTLE: vanilla zeroes |velocity.X| &lt; 0.1 while
    ///   grounded. Added (the backup let it decay asymptotically).
    /// - TRANSLUCENCY: vanilla King Slime has alpha = 30 (SetDefaults).
    ///   PreDraw now draws at 225/255 opacity (lightColor x 0.88).
    /// - DRAW OFFSET: vanilla draws the sprite center 4 px below the
    ///   NPC center (Main.DrawNPC literal +4). Applied as +4 * scale.
    /// - Added Projectile.netImportant = true (weapon-minion pattern).
    ///
    /// VERIFIED CORRECT (no change): hop velocities (-8/-6/-13 Y, 4/4.5/
    /// 3.5 X), charge starts (-120 small, -200 big), charge-rate table
    /// (+2 base, +1 &lt;80%, +1 &lt;60%, +2 &lt;40%, +3 &lt;20%, +4 &lt;10%),
    /// air control (wrong-way x0.93 decay, +0.2 accel under speed 3),
    /// teleport timings (300-tick trigger, 60-tick telegraph shrinking
    /// to half scale, 30-tick regrow), scale formula (0.6 + 0.4 x life
    /// fraction, normalized from vanilla's 0.75-1.25), slime spawns
    /// (1-3, 25% Spiked in Expert, velocities +/-1.5 X / -3..0 Y,
    /// ai[0] = -1000 x rand(0..2), ai[1] = -1), frame cycle (grounded
    /// 0-3 at 8 t/f, airborne 4 then 5 at 4 ticks, landing from 5 snaps
    /// to 4), 49x46 halved hitbox, no sounds (AI_015 plays none).
    /// Gore 734 is the vanilla King Slime teleport gore; gore IDs are
    /// stable across 1.4.4/1.4.5.
    ///
    /// Minion adaptations (the boss has no minion equivalent for these):
    /// - AIR HOPS: the real King Slime is gravity-bound and needs ground
    ///   collision to hop. This minion has tileCollide = false (per the
    ///   charm's minion spec), so the hops are ballistic arcs instead:
    ///   the slime launches with the vanilla jump velocities, falls
    ///   under manual gravity (0.4/tick), and "lands" when the arc timer
    ///   expires (air time = 2*|vy|/gravity per hop). The hop rhythm,
    ///   velocities, and air control are all vanilla.
    /// - The hop-rate acceleration is tied to the OWNER's missing life
    ///   (the vanilla trigger is the King's own missing life; minions
    ///   have no life total). Same for the size shrink: the minion is
    ///   full size at full owner life and shrinks toward 60% as the
    ///   owner is hurt (normalized so full life = the 0.5 charm scale).
    /// - Slime spawns (Attack 3) trigger when the OWNER takes a hit of
    ///   15+ life in a single tick (the vanilla trigger is the King's
    ///   own damage taken, every 5% of max life). The 25% Spiked Slime
    ///   chance is the Expert variant, used in all modes. Spawned
    ///   slimes are FRIENDLY NPCs — their vanilla AI targets players,
    ///   so they cannot attack enemies; they are a faithful visual port
    ///   (vanilla spawn velocities and ai values) capped at 6.
    /// - The teleport is a gap-closer only (no-LOS / too much vertical
    ///   separation for 5 seconds), with the vanilla 60-tick shrink
    ///   telegraph and 30-tick regrow. The invulnerability during the
    ///   teleport is a boss perk and is not ported (minions don't take
    ///   damage). The destination is a spot near the target (vanilla
    ///   finds a ground spot; the flying minion needs none).
    /// - Despawn / corner-teleport is NOT ported (flagged do-not-port).
    /// - All damage is flat 6000, typeless (DamageClass.Generic), per
    ///   the charm's design — the vanilla contact numbers in the spec
    ///   are informational only. Contact damage comes from the minion
    ///   body overlapping enemies (penetrate -1, local immunity frames).
    /// - SOUNDS: none. The King Slime AI plays NO sounds at all (zero
    ///   SoundEngine.PlaySound calls in AI_015_KingSlime) — no hop
    ///   sound, no teleport sound, no roar. Per the spec, no sounds are
    ///   invented for this minion; the sound hooks are intentionally
    ///   empty. (The squish you hear in-game comes from the generic
    ///   slime NPC code, not this AI.)
    /// </summary>
    public class KingSlimeMinion : ModProjectile
    {
        public override string Texture => "Terraria/Images/NPC_50";

        // --- Spec constants (vanilla AI_015_KingSlime) ---
        private const float TargetRange = 1000f;
        private const int MinionDamage = 6000;     // flat, typeless (charm design)

        // Hover offset near the player (no target): BELOW the player, so
        // the boss minions don't all stack on one point.
        private static readonly Vector2 IdleOffset = new Vector2(0f, 120f);

        // Hop cycle (Attack 1): 3 small hops, then 1 big jump.
        private const float HopY1 = -8f;           // small hops (hop 0, 1)
        private const float HopX1 = 4f;
        private const float HopY2 = -6f;           // third hop (hop 2)
        private const float HopX2 = 4.5f;
        private const float HopYBig = -13f;        // big jump (hop 3)
        private const float HopXBig = 3.5f;
        private const float AirTargetSpeed = 3f;   // horizontal air-control target
        private const float AirAccel = 0.2f;
        private const float HopGravity = 0.4f;     // manual gravity for air-hops

        // Air time per hop (ticks to return to launch height at gravity
        // 0.4: 2*|vy|/0.4). Small: 40, third: 30, big: 65.
        private const int AirTimeSmall = 40;
        private const int AirTimeThird = 30;
        private const int AirTimeBig = 65;

        // Grounded charge-up: starts at -120 after a small hop, -200
        // after a big jump; the hop fires when it reaches 0. The last 30
        // ticks (charge >= -30) are the wind-up: vanilla sets aiAction
        // = 1, which triples the frame-cycle speed.
        private const int ChargeStartSmall = -120;
        private const int ChargeStartBig = -200;
        private const float ChargeWindUp = -30f;

        // Teleport (Attack 2): gap-closer.
        private const int TeleportNoLOSTime = 300; // 5 s without LOS / too much vertical gap
        private const float TeleportVerticalGap = 160f;
        private const int TeleportTelegraph = 60;  // shrink telegraph
        private const int TeleportRegrow = 30;     // grow-back after teleport

        // Slime spawns (Attack 3): on owner damage.
        private const int MaxSlimes = 6;           // safety cap; vanilla has none
        private const int OwnerDamageThreshold = 15; // single-tick owner life loss to trigger
        private const float SpikedChance = 0.25f;  // Expert variant, used in all modes

        // States (Projectile.ai[0])
        private const int StateHop = 0;
        private const int StateTeleportOut = 1;    // 60-tick shrink telegraph
        private const int StateTeleportIn = 2;     // 30-tick regrow after teleport

        // Projectile.ai[1] = hop index in the cycle (0-3).
        // Projectile.ai[2] = air timer while airborne (>= 0), or -1 while
        // grounded (charging). In the teleport states it is the state timer.

        // --- State (instance fields) ---
        private NPC target = null;
        private int retargetTimer = 0;
        private float chargeTimer = ChargeStartSmall; // counts up while grounded
        private int noLOSTimer = 0;                   // CUMULATIVE ticks without LOS / too far vertically (vanilla ai[2]: never resets on LOS return, only on teleport)
        private float lastOwnerLife = -1f;            // -1 = uninitialized

        public override void SetDefaults()
        {
            // Vanilla King Slime hitbox 98x92, halved for the minion
            // (0.5 scale). (The vanilla 1.25x NPC scale is a draw-time
            // multiplier; the coded hitbox is 98x92.)
            Projectile.width = 49;
            Projectile.height = 46;
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

        public override void OnSpawn(IEntitySource source)
        {
            // Start grounded (ai[2] = -1), charging the first hop.
            Projectile.ai[0] = StateHop;
            Projectile.ai[1] = 0f;
            Projectile.ai[2] = -1f;
            chargeTimer = ChargeStartSmall;
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

            // AMBIENT DUST (vanilla, end of AI_015): 1 dust EVERY tick in
            // every state — type 4, blue (0,80,255,80), alpha 255, scale
            // x1.2, no gravity, velocity halved.
            int ambient = Dust.NewDust(Projectile.position, Projectile.width, Projectile.height, 4,
                Projectile.velocity.X, Projectile.velocity.Y, 255, new Color(0, 80, 255, 80), Projectile.scale * 1.2f);
            Main.dust[ambient].noGravity = true;
            Main.dust[ambient].velocity *= 0.5f;

            // Animation frames (vanilla FindFrame, NPC 50 — see boss_frames.md).
            // Runs every tick, even with no target. Grounded (charging):
            // walk cycle 0→1→2→3 (faster while moving, fastest in the
            // wind-up); airborne: frame 4 then frame 5 (stretch).
            UpdateFrame();

            // Re-scan for a target every 15 ticks (or if the current one died).
            retargetTimer++;
            if (retargetTimer >= 15 || target == null || !target.active || !target.CanBeChasedBy())
            {
                retargetTimer = 0;
                target = FindTarget(TargetRange);
            }

            // Slime spawns (Attack 3): when the owner takes a hit, spawn
            // friendly mini-slimes (owner-only). Runs in every state.
            CheckOwnerDamage(owner);

            if (target == null)
            {
                // No enemy: drift to the hover point below the player.
                // The hop state machine pauses and resumes when a target
                // appears.
                SteerToward(owner.Center + IdleOffset, 6f, 0.1f);
                UpdateScale(owner);
                return;
            }

            int state = (int)Projectile.ai[0];
            switch (state)
            {
                case StateHop: HopState(owner); break;
                case StateTeleportOut: TeleportOutState(); break;
                case StateTeleportIn: TeleportInState(); break;
                default: EnterHopState(); break;
            }

            UpdateScale(owner);
        }

        // --- Hop cycle (Attack 1) ---

        private void HopState(Player owner)
        {
            // Teleport trigger (Attack 2): vanilla's ai[2] is CUMULATIVE
            // — it increments on every no-LOS / too-far-vertically tick
            // and NEVER resets on LOS return (only the anti-cheese
            // localAI[0] decays; ai[2] resets only when the teleport
            // fires). The trigger also requires the slime to be GROUNDED
            // (velocity.Y == 0 — here: the charging state, ai[2] < 0).
            // The vertical check is the King's TOP vs the target's
            // BOTTOM (vanilla), not center-to-center.
            bool noLOS = !HasLineOfSight(target);
            bool tooFarVertically = Math.Abs(Projectile.Top.Y - target.Bottom.Y) > TeleportVerticalGap;
            if (noLOS || tooFarVertically)
            {
                noLOSTimer++;
                if (noLOSTimer >= TeleportNoLOSTime && Projectile.ai[2] < 0f)
                {
                    noLOSTimer = 0;
                    Projectile.velocity = Vector2.Zero;
                    Projectile.ai[0] = StateTeleportOut;
                    Projectile.ai[2] = 0f;
                    Projectile.netUpdate = true;
                    return;
                }
            }
            // NOTE: no reset here — vanilla ai[2] is cumulative.

            int hop = (int)Projectile.ai[1];

            if (Projectile.ai[2] < 0f)
            {
                // Grounded: charge up for the next hop. Charge rate
                // accelerates as the OWNER loses life (vanilla: the
                // King's own missing life): +2/tick base, +1 below 80%,
                // +1 below 60%, +2 below 40%, +3 below 20%, +4 below 10%.
                float lifeFraction = owner.statLife / (float)Math.Max(1, owner.statLifeMax2);
                float rate = 2f;
                if (lifeFraction < 0.8f) rate += 1f;
                if (lifeFraction < 0.6f) rate += 1f;
                if (lifeFraction < 0.4f) rate += 2f;
                if (lifeFraction < 0.2f) rate += 3f;
                if (lifeFraction < 0.1f) rate += 4f;
                chargeTimer += rate;

                // Settle while charging (no sliding). Vanilla damps X by
                // 0.8 and zeroes it below 0.1.
                Projectile.velocity *= 0.8f;
                if (Projectile.velocity.X > -0.1f && Projectile.velocity.X < 0.1f)
                    Projectile.velocity.X = 0f;

                if (chargeTimer >= 0f)
                {
                    // Launch the hop toward the target.
                    int dir = target.Center.X >= Projectile.Center.X ? 1 : -1;
                    Projectile.spriteDirection = dir;
                    float vx, vy;
                    if (hop == 3) { vx = HopXBig * dir; vy = HopYBig; }
                    else if (hop == 2) { vx = HopX2 * dir; vy = HopY2; }
                    else { vx = HopX1 * dir; vy = HopY1; }
                    Projectile.velocity = new Vector2(vx, vy);
                    Projectile.ai[2] = 0f; // airborne
                    Projectile.netUpdate = true;
                }
            }
            else
            {
                // Airborne: manual gravity + air control.
                Projectile.velocity.Y += HopGravity;

                int dir = target.Center.X >= Projectile.Center.X ? 1 : -1;
                if (Projectile.velocity.X != 0f && Math.Sign(Projectile.velocity.X) != dir)
                    Projectile.velocity.X *= 0.93f; // moving the wrong way: decay
                else if (Math.Abs(Projectile.velocity.X) < AirTargetSpeed)
                    Projectile.velocity.X += AirAccel * dir;

                Projectile.ai[2] += 1f;

                int airTime = hop == 3 ? AirTimeBig : (hop == 2 ? AirTimeThird : AirTimeSmall);
                if (Projectile.ai[2] >= airTime)
                {
                    // "Land": the arc is over. Next hop in the cycle, and
                    // the charge restarts (-200 after a big jump, -120
                    // after a small hop).
                    Projectile.velocity = Vector2.Zero;
                    chargeTimer = hop == 3 ? ChargeStartBig : ChargeStartSmall;
                    Projectile.ai[1] = (hop + 1) % 4;
                    Projectile.ai[2] = -1f; // grounded
                    Projectile.netUpdate = true;
                }
            }
        }

        // --- Teleport (Attack 2) ---

        private void TeleportOutState()
        {
            // 60-tick telegraph: shrink toward half scale, 10 blue dust
            // per tick (vanilla), then teleport instantly near the
            // target with the gore burst (gore 734) at tick 60.
            Projectile.velocity = Vector2.Zero;
            Projectile.ai[2] += 1f;

            if (Projectile.ai[2] < TeleportTelegraph)
            {
                // Vanilla telegraph dust: 10/tick, type 4, color
                // (78,136,255,80), alpha 150, scale 2, no gravity,
                // velocity x0.5. Dust spread halved for the minion.
                for (int i = 0; i < 10; i++)
                {
                    int d = Dust.NewDust(Projectile.position + Vector2.UnitX * -10f, Projectile.width + 20, Projectile.height,
                        4, Projectile.velocity.X, Projectile.velocity.Y, 150, new Color(78, 136, 255, 80), 2f);
                    Main.dust[d].noGravity = true;
                    Main.dust[d].velocity *= 0.5f;
                }
            }

            if (Projectile.ai[2] >= TeleportTelegraph)
            {
                // Gore burst at the teleport moment (vanilla: gore 734
                // at Center + (-40, -height/2), halved here).
                if (Projectile.owner == Main.myPlayer)
                    Gore.NewGore(Projectile.GetSource_FromThis(),
                        Projectile.Center + new Vector2(-20f, -Projectile.height / 2f),
                        Projectile.velocity, 734, Projectile.scale);

                // Teleport near the target (random on-screen spot near it).
                Vector2 dest = target.Center + new Vector2(Main.rand.Next(-100, 101), -100f);
                Projectile.Center = dest;
                Projectile.velocity = Vector2.Zero;
                Projectile.ai[0] = StateTeleportIn;
                Projectile.ai[2] = 0f;
                Projectile.netUpdate = true;
            }
        }

        private void TeleportInState()
        {
            // 30 ticks to grow back to full scale before hopping resumes.
            // Vanilla regrow dust: 10/tick, same as the telegraph but
            // velocity x2.
            Projectile.velocity = Vector2.Zero;
            Projectile.ai[2] += 1f;

            for (int i = 0; i < 10; i++)
            {
                int d = Dust.NewDust(Projectile.position + Vector2.UnitX * -10f, Projectile.width + 20, Projectile.height,
                    4, Projectile.velocity.X, Projectile.velocity.Y, 150, new Color(78, 136, 255, 80), 2f);
                Main.dust[d].noGravity = true;
                Main.dust[d].velocity *= 2f;
            }

            if (Projectile.ai[2] >= TeleportRegrow)
                EnterHopState();
        }

        private void EnterHopState()
        {
            Projectile.ai[0] = StateHop;
            Projectile.ai[1] = 0f;
            Projectile.ai[2] = -1f; // grounded
            chargeTimer = ChargeStartSmall;
            noLOSTimer = 0;
            Projectile.netUpdate = true;
        }

        // --- Slime spawns (Attack 3) ---

        // When the owner takes a hit of 15+ life in a single tick, spawn
        // 1-3 friendly mini-slimes (vanilla: every 5% of max life lost).
        // The threshold throttles it so damage-over-time doesn't spam
        // spawns every tick.
        private void CheckOwnerDamage(Player owner)
        {
            if (Projectile.owner != Main.myPlayer)
                return;
            if (lastOwnerLife < 0f)
            {
                lastOwnerLife = owner.statLife;
                return;
            }
            if (owner.statLife < lastOwnerLife - OwnerDamageThreshold)
                SpawnSlimes();
            lastOwnerLife = owner.statLife;
        }

        private void SpawnSlimes()
        {
            int count = Main.rand.Next(1, 4); // 1-3 slimes
            for (int i = 0; i < count; i++)
            {
                if (CountFriendlySlimes() >= MaxSlimes)
                    return;
                // Expert: 25% Spiked Slime, else Blue Slime.
                int type = Main.rand.NextFloat() < SpikedChance ? NPCID.SlimeSpiked : NPCID.BlueSlime;
                // Spawned at a random position inside the King's body.
                Vector2 pos = Projectile.Center + new Vector2(Main.rand.Next(-20, 21), Main.rand.Next(-20, 21));
                // Vanilla spawn values: ai[0] = -1000 x rand(0..2), ai[1] = -1.
                int idx = NPC.NewNPC(Projectile.GetSource_FromThis(), (int)pos.X, (int)pos.Y, type,
                    0, -1000f * Main.rand.Next(3), -1f);
                if (idx < 0 || idx >= Main.maxNPCs)
                    continue;
                NPC slime = Main.npc[idx];
                slime.friendly = true;
                // Random upward pop: X +/-1.5, Y -3.0 to 0.
                slime.velocity = new Vector2(Main.rand.NextFloat(-1.5f, 1.5f), Main.rand.NextFloat(-3f, 0f));
                slime.netUpdate = true;
            }
        }

        private int CountFriendlySlimes()
        {
            int count = 0;
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (npc.active && npc.friendly && (npc.type == NPCID.BlueSlime || npc.type == NPCID.SlimeSpiked))
                    count++;
            }
            return count;
        }

        // --- Scale (life shrink + teleport shrink) ---

        // The vanilla King shrinks as it loses life (scale 1.25 at full
        // life -> 0.75 near death). The minion mirrors it against the
        // OWNER's life, normalized so full owner life = the 0.5 charm
        // scale and near death = 0.3. The teleport shrink multiplies on
        // top (down to half scale during the telegraph, back up after).
        private void UpdateScale(Player owner)
        {
            float lifeFraction = owner.statLife / (float)Math.Max(1, owner.statLifeMax2);
            float lifeScale = 0.6f + 0.4f * lifeFraction;
            float teleportScale = 1f;
            int state = (int)Projectile.ai[0];
            if (state == StateTeleportOut)
                teleportScale = 1f - (Projectile.ai[2] / TeleportTelegraph) * 0.5f;
            else if (state == StateTeleportIn)
                teleportScale = 0.5f + (Projectile.ai[2] / TeleportRegrow) * 0.5f;
            Projectile.scale = 0.5f * lifeScale * teleportScale;
        }

        // --- Shared helpers ---

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

        // Vanilla FindFrame animation (NPC 50, 6 frames — see boss_frames.md),
        // adapted to the minion's simulated hop cycle (tileCollide=false, so
        // "grounded" is the charging state, ai[2] < 0, and "airborne" is the
        // hop arc, ai[2] >= 0):
        //  - Grounded: walk cycle 0→1→2→3 at 8 ticks/frame. Landing from
        //    frame 5 snaps to frame 4 (squash pose) for one cycle. The
        //    counter accelerates: +2/tick while moving horizontally
        //    (vanilla num2 == 1), +3/tick in the wind-up (charge >= -30,
        //    vanilla aiAction == 1 sets num2 == 4).
        //  - Airborne: frame 4 (launch stretch) for the first 3 ticks, then
        //    frame 5 (full stretch) until landing.
        //  - Teleport states: vanilla does NOT freeze the frame — the
        //    grounded cycle keeps running (fast, +3/tick, during the
        //    telegraph since aiAction == 1; normal during the regrow).
        private void UpdateFrame()
        {
            int state = (int)Projectile.ai[0];

            if (state == StateHop && Projectile.ai[2] >= 0f)
            {
                // Airborne: frame 4 for the first 3 ticks, then frame 5.
                if (Projectile.frame < 4)
                {
                    Projectile.frame = 4;
                    Projectile.frameCounter = 0;
                }
                Projectile.frameCounter++;
                if (Projectile.frameCounter >= 4)
                    Projectile.frame = 5;
                return;
            }

            // Grounded (charging, idle, or teleporting): walk cycle 0→1→2→3.
            if (Projectile.frame >= 5)
            {
                // Landed on frame 5: snap to frame 4 (squash pose).
                Projectile.frame = 4;
                Projectile.frameCounter = 0;
            }

            // Frame-counter acceleration (vanilla FindFrame num2):
            // +3 in the wind-up / telegraph (aiAction == 1), +2 while
            // moving horizontally, +1 otherwise.
            int increment = 1;
            bool windingUp = (state == StateHop && chargeTimer >= ChargeWindUp) || state == StateTeleportOut;
            if (windingUp)
                increment = 3;
            else if (Math.Abs(Projectile.velocity.X) > 0.1f)
                increment = 2;
            Projectile.frameCounter += increment;

            if (Projectile.frameCounter >= 8)
            {
                Projectile.frameCounter = 0;
                int f = Projectile.frame + 1;
                if (f >= 4) f = 0;
                Projectile.frame = f;
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D texture = TextureAssets.Projectile[Projectile.type].Value;
            // Frame count is HARDCODED (6 — the King's texture has 6 frames;
            // see boss_frames.md). Main.npcFrameCount returns 0/1 for NPCs
            // whose textures haven't loaded yet, which drew the whole sprite
            // sheet. Projectile.frame is set every tick by UpdateFrame().
            const int frameCount = 6;
            int frameHeight = texture.Height / frameCount;
            Rectangle frame = new Rectangle(0, Projectile.frame * frameHeight, texture.Width, frameHeight);
            Vector2 origin = frame.Size() / 2f;
            // Vanilla draws the sprite center 4 px below the NPC center
            // (Main.DrawNPC literal +4); scaled by the minion's scale.
            // The King is slightly translucent (alpha = 30 in SetDefaults).
            Vector2 drawPos = Projectile.Center + new Vector2(0f, 4f * Projectile.scale) - Main.screenPosition;
            Color drawColor = lightColor * (1f - 30f / 255f);
            Main.EntitySpriteDraw(texture, drawPos, frame, drawColor, Projectile.rotation, origin, Projectile.scale, SpriteEffects.None, 0);
            return false;
        }
    }
}
