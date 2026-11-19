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
    /// The Destroyer boss minion (Brigitte's Charm, 2026-11-15): the
    /// Destroyer's head, rewritten as a floating friendly minion. It fires
    /// the Destroyer's Death Lasers, periodically charges through its
    /// target like the burrowing head, spawns Probe minions, and rumbles
    /// like the real boss while moving.
    /// Attack data from the decompiled Terraria 1.4.4.9 AI_037_Destroyer
    /// (see ~/workspace/tmp/boss_attacks_batch1.md, Part 3, Destroyer).
    ///
    /// SIMPLIFICATION (v1, per the charm spec): HEAD ONLY. The segmented
    /// worm body is NOT built — the 80 body segments + tail are separate
    /// NPCs with shared-worm AI (segment pinning, shared life pool) that
    /// cannot be ported as a minion. Consequences, all noted:
    /// - In vanilla the HEAD fires nothing; only body segments fire
    ///   Death Lasers (randomized timer, ~1 shot per 18 s per segment,
    ///   ~4.4 shots/s collectively across 80 segments). The head minion
    ///   fires the lasers itself at that collective rate (1 per 15
    ///   ticks = 4/s), so the barrage feels the same.
    /// - Probe release is on-hit in vanilla (1/25 per segment hit, once
    ///   per segment). Minions aren't hit, so it becomes a timed spawn:
    ///   one Probe every 5 s, max 5 alive. (Vanilla allows 80 per fight;
    ///   5 is the sane minion cap.)
    /// - The burrowing/terrain-phasing movement becomes a flying hover
    ///   + periodic charge (speed 16, the vanilla head max speed).
    ///
    /// PROBE SUBSTITUTION: Probes are NPCs (type 139) whose vanilla AI
    /// (aiStyle 5) targets and shoots the PLAYER — spawning a vanilla
    /// Probe would attack the owner. Probes are instead spawned as
    /// DestroyerProbeMinion projectiles (below): same look, same flight
    /// (speed 6, accel 0.05, gentle wobble, catch-up boost), same Pink
    /// Laser attack (every 120 ticks, speed 6), retargeted at enemies.
    /// Two flight details are minion adaptations, not vanilla: vanilla's
    /// wobble is a slow triangle drift (ai[0] -200..200), approximated
    /// here with a sine wobble, and vanilla's dist-&gt;600 "catch-up"
    /// flag (flag3 in AI_005) is set but never used — dead code — so
    /// the boost past 600 px is the minion's own addition to keep
    /// Probes from lagging behind. The Probe faces its TARGET (vanilla
    /// AI_005: rotation = atan2 to the target, flipped when the target
    /// is right), not its velocity.
    ///
    /// Other adaptations:
    /// - Daytime despawn (dives below the rock layer) is dropped
    ///   (flagged do-not-port in the spec).
    /// - The burrow rumble plays while the minion is moving (every ~15
    ///   ticks); in vanilla it plays only while the head is inside
    ///   tiles, which a flying minion never is.
    /// - Sound correction: the spec's Part 3 resolved the rumble
    ///   (legacy PlaySound(15, ...)) as "Zombie bank -> Zombie_1", but
    ///   the decompiled SoundID shows type 15 IS the Roar bank
    ///   (Roar = 15; Zombie = 29). With the default style 1 it is
    ///   Roar_1 — the low growl — which is what plays here.
    /// - All damage is flat 6000, typeless (DamageClass.Generic), per the
    ///   charm's design — the vanilla damage values in the spec are
    ///   informational only.
    /// </summary>
    public class DestroyerMinion : ModProjectile
    {
        // --- Spec constants (vanilla AI_037_Destroyer head + segments) ---
        private const float TargetRange = 1000f;

        // Death Laser (segment attack, fired by the head at the collective
        // rate: 80 segments x ~1/18s each ~= 4.4/s -> 1 per 15 ticks).
        private const int LaserInterval = 15;
        private const float LaserSpeed = 8f;
        private const float LaserJitter = 1.0f;    // +/- per axis (Rand(-20,21) * 0.05)
        private const float LaserSpawnAhead = 5f;

        // Burrowing charge (head max speed 16, accel 0.15 when aligned).
        private const float ChargeSpeed = 16f;
        private const float ChargeAccel = 0.15f;
        private const int ChargeInterval = 480;    // charge every 8 s
        private const int ChargeMaxDuration = 90;  // give up after 1.5 s
        private const float ChargeExitDistance = 60f;

        // Probe spawn (timed conversion of the on-hit release).
        private const int ProbeInterval = 300;     // one Probe every 5 s
        private const int MaxProbes = 5;

        // Burrow rumble (gated 10-20 ticks in vanilla; 15 here).
        private const int RumbleInterval = 15;

        // Hover movement.
        private const float HoverSpeed = 8f;
        private const float HoverAccel = 0.1f;
        private const float HoverHeight = 250f;    // hover above the target

        private const int MinionDamage = 6000;     // flat, typeless (charm design)

        // Burrow rumble: legacy sound type 15 (Roar bank), style 1 (the
        // default when no style is given). SoundID.Roar is a const int,
        // not a SoundStyle — use the path-based SoundStyle instead.
        private static readonly SoundStyle RumbleSound = new SoundStyle("Terraria/Sounds/Roar_1");

        // Hover offset near the player (no target): each boss minion gets a
        // distinct offset so the entourage doesn't stack on one point.
        private static readonly Vector2 PlayerHoverOffset = new Vector2(380f, -120f);

        // --- State ---
        // Projectile.ai[1] = state: 0 = hover/fire, 1 = charge.
        private int laserTimer = 0;
        private int probeTimer = 0;
        private int chargeTimer = 0;
        private int chargeStateTimer = 0;
        private int rumbleTimer = 0;
        private int retargetTimer = 0;
        private NPC target = null;

        public override string Texture => "Terraria/Images/NPC_134";

        public override void SetDefaults()
        {
            // Vanilla Destroyer head hitbox 38x38, halved = 19 -> min 20.
            Projectile.width = 20;
            Projectile.height = 20;
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

            Projectile.light = 0.5f;
        }

        public override void AI()
        {
            Player owner = Main.player[Projectile.owner];
            if (owner == null || !owner.active)
            {
                Projectile.velocity *= 0.95f;
                return;
            }

            // RECALL + FAST FOLLOW (2026-11-18 pattern, shared with the
            // weapon minions): teleport back if too far; fly toward the
            // player at a distance-ramped speed when lagging behind
            // (16-60 px/tick), so the minion catches up.
            float distToOwner = Vector2.Distance(Projectile.Center, owner.Center);
            if (distToOwner > 2000f)
            {
                Projectile.Center = owner.Center;
                Projectile.velocity = Vector2.Zero;
                Projectile.netUpdate = true;
                for (int i = 0; i < 10; i++)
                    Dust.NewDust(Projectile.position, Projectile.width, Projectile.height, DustID.GoldFlame);
            }
            else if (distToOwner > 400f)
            {
                float followSpeed = MathHelper.Clamp(distToOwner * 0.05f, 16f, 60f);
                Vector2 toOwner = (owner.Center - Projectile.Center).SafeNormalize(Vector2.Zero);
                Projectile.velocity = Vector2.Lerp(Projectile.velocity, toOwner * followSpeed, 0.2f);
            }

            // Burrow rumble: every ~15 ticks while moving (vanilla: while
            // the head is inside tiles, gated 10-20 ticks by soundDelay).
            rumbleTimer++;
            if (rumbleTimer >= RumbleInterval && Projectile.velocity.Length() > 1f)
            {
                rumbleTimer = 0;
                SoundEngine.PlaySound(RumbleSound, Projectile.Center);
            }

            // Re-scan for a target every 15 ticks (or if the current one died).
            retargetTimer++;
            if (retargetTimer >= 15 || target == null || !target.active || !target.CanBeChasedBy())
            {
                retargetTimer = 0;
                target = FindTarget(TargetRange);
            }

            // The head always faces its movement direction (vanilla:
            // rotation = velocity direction + 1.57).
            if (Projectile.velocity.LengthSquared() > 0.25f)
                Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.PiOver2;

            if (target == null)
            {
                // No enemy: drift to the hover point near the player.
                SteerToward(owner.Center + PlayerHoverOffset, HoverSpeed, HoverAccel);
                return;
            }

            int state = (int)Projectile.ai[1];

            if (state == 0)
            {
                // --- Hover above the target, firing Death Lasers. ---
                Vector2 hoverPoint = target.Center + new Vector2(0f, -HoverHeight);
                SteerToward(hoverPoint, HoverSpeed, HoverAccel);

                // Death Laser barrage (collective segment rate). Vanilla
                // resets the timer at the threshold BEFORE the line-of-
                // sight check, so a blocked shot is lost, not deferred
                // to the instant sight opens.
                laserTimer++;
                if (laserTimer >= LaserInterval)
                {
                    laserTimer = 0;
                    if (HasLineOfSight(target))
                        FireDeathLaser();
                }

                // Probe spawn (timed conversion of the on-hit release).
                probeTimer++;
                if (probeTimer >= ProbeInterval)
                {
                    probeTimer = 0;
                    TrySpawnProbe();
                }

                // Periodic charge through the target.
                chargeTimer++;
                if (chargeTimer >= ChargeInterval)
                {
                    chargeTimer = 0;
                    chargeStateTimer = 0;
                    Projectile.ai[1] = 1f;
                    Projectile.netUpdate = true;
                }
            }
            else
            {
                // --- Charge: home toward the target at speed 16. ---
                chargeStateTimer++;
                Vector2 dir = (target.Center - Projectile.Center).SafeNormalize(Vector2.UnitX);
                Projectile.velocity += dir * ChargeAccel;
                if (Projectile.velocity.Length() > ChargeSpeed)
                    Projectile.velocity = Vector2.Normalize(Projectile.velocity) * ChargeSpeed;

                float dist = Vector2.Distance(Projectile.Center, target.Center);
                if (dist < ChargeExitDistance || chargeStateTimer >= ChargeMaxDuration)
                {
                    Projectile.ai[1] = 0f;
                    chargeStateTimer = 0;
                    Projectile.netUpdate = true;
                }
            }
        }

        private void FireDeathLaser()
        {
            Vector2 dir = (target.Center - Projectile.Center).SafeNormalize(Vector2.UnitX);
            // Vanilla: aimed at the target center with a random +/-20 px
            // offset on each axis, then jitter +/-1.0 on each component.
            Vector2 aimPoint = target.Center + new Vector2(Main.rand.Next(-20, 21), Main.rand.Next(-20, 21));
            Vector2 velocity = (aimPoint - Projectile.Center).SafeNormalize(Vector2.UnitX) * LaserSpeed;
            velocity.X += Main.rand.NextFloat(-LaserJitter, LaserJitter);
            velocity.Y += Main.rand.NextFloat(-LaserJitter, LaserJitter);
            Vector2 spawnPos = Projectile.Center + dir * LaserSpawnAhead;
            // Vanilla sets timeLeft = 300 on every Death Laser (the
            // default is 2700 — 45 s — which would flood the field).
            FireFriendly(spawnPos, velocity, ProjectileID.DeathLaser, timeLeft: 300);
            // SoundID.Item33 comes free from the projectile's own AI.
            // (Vanilla: no firing sound in the Destroyer AI itself.)
        }

        private void TrySpawnProbe()
        {
            if (Projectile.owner != Main.myPlayer)
                return;
            if (CountAliveProbes() >= MaxProbes)
                return;
            Vector2 velocity = new Vector2(Main.rand.NextFloat(-2f, 2f), Main.rand.NextFloat(-2f, 2f));
            Projectile.NewProjectile(Projectile.GetSource_FromThis(), Projectile.Center, velocity, ModContent.ProjectileType<DestroyerProbeMinion>(), MinionDamage, 0f, Projectile.owner);
        }

        private int CountAliveProbes()
        {
            int type = ModContent.ProjectileType<DestroyerProbeMinion>();
            int count = 0;
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile p = Main.projectile[i];
                if (p != null && p.active && p.type == type && p.owner == Projectile.owner)
                    count++;
            }
            return count;
        }

        // Spawns a FRIENDLY version of a vanilla (hostile-by-default) boss
        // projectile. Vanilla boss projectiles set hostile = true in
        // SetDefaults, so the flags must be flipped after spawn or the shot
        // would hurt the player instead of the enemy. DamageType is forced
        // to Generic (typeless) so the shot doesn't scale with the owner's
        // magic bonuses (Death Laser is magic = true by default).
        private void FireFriendly(Vector2 position, Vector2 velocity, int type, int timeLeft = -1)
        {
            if (Projectile.owner != Main.myPlayer)
                return;
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
            // The Destroyer head texture is a SINGLE frame (frame count 1,
            // read from the game's own npcFrameCount table in the binary).
            // The count is HARDCODED: Main.npcFrameCount[type] is not
            // reliable at runtime for NPCs that have never spawned, and a
            // 0/1 fallback draws the raw sprite sheet. The head has no
            // frame animation (vanilla FindFrame has no case 134 — frame 0
            // forever, all motion is rotation, set in AI()).
            const int frameCount = 1;
            Texture2D texture = TextureAssets.Projectile[Projectile.type].Value;
            int frameHeight = texture.Height / frameCount;
            Rectangle frame = new Rectangle(0, 0, texture.Width, frameHeight);
            Vector2 origin = frame.Size() / 2f;
            Main.EntitySpriteDraw(texture, Projectile.Center - Main.screenPosition, frame, lightColor, Projectile.rotation, origin, Projectile.scale, SpriteEffects.None, 0);
            return false;
        }
    }

    /// <summary>
    /// Destroyer Probe minion (Brigitte's Charm, 2026-11-15): a friendly
    /// re-implementation of the Destroyer's Probe (NPC 139). A vanilla
    /// Probe cannot be used: its AI (aiStyle 5) targets and shoots the
    /// PLAYER, so spawning one would attack the owner. This projectile
    /// reproduces the Probe's real behavior — flight (speed 6, accel
    /// 0.05, gentle wobble, catch-up boost past 600 px) and its Pink Laser
    /// attack (every 120 ticks, speed 6) — retargeted at enemies.
    /// The wobble and catch-up are minion adaptations (see the head's
    /// class doc): vanilla's wobble is a triangle drift and its 600 px
    /// catch-up flag is dead code. The Probe faces its TARGET (vanilla
    /// AI_005: rotation = atan2 to the target, flipped when the target
    /// is right), not its velocity.
    /// Spawned by the DestroyerMinion (max 5 alive); lives 30 s, then
    /// pops and is replaced. Does not consume a minion slot (it is a
    /// sub-minion of the Destroyer).
    /// Attack data from the decompiled Terraria 1.4.4.9 AI_005_EaterOfSouls
    /// (Probe) — see ~/workspace/tmp/boss_attacks_batch1.md, Part 3.
    /// </summary>
    public class DestroyerProbeMinion : ModProjectile
    {
        // --- Spec constants (vanilla Probe, NPC 139) ---
        private const float TargetRange = 1000f;
        private const float FlySpeed = 6f;
        private const float FlyAccel = 0.05f;
        private const float CatchUpDistance = 600f;
        private const float CatchUpMultiplier = 1.05f;
        private const float CatchUpMaxSpeed = 12f;
        private const float WobbleDistance = 100f;   // wobble only when >100 px away
        private const float WobbleStrength = 0.023f; // per-tick perpendicular nudge
        private const int LaserInterval = 120;       // 2 s between Pink Lasers
        private const float LaserSpeed = 6f;

        private const int MinionDamage = 6000;     // flat, typeless (charm design)

        private int fireTimer = 0;
        private int wobbleTimer = 0;
        private int retargetTimer = 0;
        private NPC target = null;

        public override string Texture => "Terraria/Images/NPC_139";

        public override void SetDefaults()
        {
            // Vanilla Probe hitbox 30x30, halved = 15 -> min 20.
            Projectile.width = 20;
            Projectile.height = 20;
            Projectile.scale = 0.5f;

            Projectile.friendly = true;
            Projectile.hostile = false;
            Projectile.minion = true;
            Projectile.minionSlots = 0; // sub-minion of the Destroyer; no slot cost
            Projectile.damage = MinionDamage;
            Projectile.DamageType = DamageClass.Generic;
            Projectile.knockBack = 0f;
            Projectile.penetrate = -1;

            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 20;
            Projectile.netImportant = true;

            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;

            // Fixed 30 s lifetime; the Destroyer minion spawns replacements.
            Projectile.timeLeft = 1800;

            Projectile.light = 0.4f;
        }

        public override void AI()
        {
            Player owner = Main.player[Projectile.owner];
            if (owner == null || !owner.active)
            {
                Projectile.velocity *= 0.95f;
                return;
            }

            wobbleTimer++;

            // Re-scan for a target every 15 ticks (or if the current one died).
            retargetTimer++;
            if (retargetTimer >= 15 || target == null || !target.active || !target.CanBeChasedBy())
            {
                retargetTimer = 0;
                target = FindTarget(TargetRange);
            }

            if (target == null)
            {
                // No enemy: drift back toward the player.
                SteerToward(owner.Center + new Vector2(0f, -100f), FlySpeed, FlyAccel);
            }
            else
            {
                Vector2 toTarget = target.Center - Projectile.Center;
                float dist = toTarget.Length();

                // Fly at the target (speed 6, accel 0.05).
                if (dist > 1f)
                {
                    Vector2 dir = toTarget / dist;
                    Projectile.velocity += dir * FlyAccel;

                    // Sine wobble: perpendicular nudges when >100 px away.
                    if (dist > WobbleDistance)
                    {
                        Vector2 perpendicular = new Vector2(-dir.Y, dir.X);
                        Projectile.velocity += perpendicular * ((float)Math.Sin(wobbleTimer * 0.1f) * WobbleStrength);
                    }

                    // Catch-up boost past 600 px (up to speed 12).
                    if (dist > CatchUpDistance)
                    {
                        Projectile.velocity *= CatchUpMultiplier;
                        if (Projectile.velocity.Length() > CatchUpMaxSpeed)
                            Projectile.velocity = Vector2.Normalize(Projectile.velocity) * CatchUpMaxSpeed;
                    }
                    else if (Projectile.velocity.Length() > FlySpeed)
                    {
                        Projectile.velocity = Vector2.Normalize(Projectile.velocity) * FlySpeed;
                    }
                }

                // Pink Laser every 120 ticks. Vanilla resets the timer at
                // the threshold BEFORE the line-of-sight check, so a
                // blocked shot is lost, not deferred to the instant
                // sight opens.
                fireTimer++;
                if (fireTimer >= LaserInterval)
                {
                    fireTimer = 0;
                    if (HasLineOfSight(target))
                        FirePinkLaser();
                }
            }

            // Face the TARGET (vanilla AI_005: rotation = atan2 toward the
            // target, +PI when the target is left; spriteDirection 1 =
            // flip when the target is right). Projectile drawing flips on
            // spriteDirection -1, the opposite of NPC drawing, so the
            // values are inverted vs vanilla's NPC assignments — the
            // visual result is the same.
            if (target != null)
            {
                Vector2 toTarget = target.Center - Projectile.Center;
                Projectile.spriteDirection = toTarget.X >= 0f ? -1 : 1;
                Projectile.rotation = (float)Math.Atan2(toTarget.Y, toTarget.X);
                if (Projectile.spriteDirection == 1)
                    Projectile.rotation += MathHelper.Pi;
            }
        }

        private void FirePinkLaser()
        {
            Vector2 dir = (target.Center - Projectile.Center).SafeNormalize(Vector2.UnitX);
            Vector2 velocity = dir * LaserSpeed;
            // Pink Laser keeps its vanilla 600-tick lifetime (vanilla
            // never overrides it, unlike the Death Laser).
            FireFriendly(Projectile.Center, velocity, ProjectileID.PinkLaser);
            // Vanilla: no firing sound for the Probe laser.
        }

        // Spawns a FRIENDLY version of a vanilla (hostile-by-default) boss
        // projectile. Vanilla boss projectiles set hostile = true in
        // SetDefaults, so the flags must be flipped after spawn or the shot
        // would hurt the player instead of the enemy. DamageType is forced
        // to Generic (typeless) so the shot doesn't scale with the owner's
        // magic bonuses (Pink Laser is magic = true by default).
        private void FireFriendly(Vector2 position, Vector2 velocity, int type, int timeLeft = -1)
        {
            if (Projectile.owner != Main.myPlayer)
                return;
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
            // The Probe texture is a SINGLE frame (frame count 1, read from
            // the game's own npcFrameCount table in the binary; the Probe
            // has no FindFrame case, so it is frame 0 forever). The count
            // is HARDCODED — Main.npcFrameCount[type] is not reliable at
            // runtime for NPCs that have never spawned.
            const int frameCount = 1;
            Texture2D texture = TextureAssets.Projectile[Projectile.type].Value;
            int frameHeight = texture.Height / frameCount;
            Rectangle frame = new Rectangle(0, 0, texture.Width, frameHeight);
            Vector2 origin = frame.Size() / 2f;
            // The Probe flips when its target is to the right (vanilla
            // AI_005 sets spriteDirection 1 there; projectiles flip on
            // spriteDirection -1, so the value is inverted in AI()).
            SpriteEffects effects = Projectile.spriteDirection == -1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            Main.EntitySpriteDraw(texture, Projectile.Center - Main.screenPosition, frame, lightColor, Projectile.rotation, origin, Projectile.scale, effects, 0);
            return false;
        }
    }
}
