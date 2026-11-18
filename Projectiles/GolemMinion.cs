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
    /// Golem as a Brigitte's Charm boss minion (2026-11-15). The body with his
    /// real attacks, retargeted at enemies: stomp jump, fist punches (as melee
    /// lunges by the body), head fireballs, and eye lasers (phase 2).
    /// Half scale, flat 6000 typeless damage, local immunity frames.
    ///
    /// Attack data from the decompiled Terraria 1.4.4.9 NPC AI
    /// (aiStyle 45 body / 46 head / 47 fists):
    /// see ~/workspace/tmp/boss_attacks_batch1.md, Golem section.
    ///
    /// DRAWING (fixed 2026-11-18): the vanilla Golem is FOUR NPCs — body
    /// (245), head (246), left fist (247), right fist (248) — and the body
    /// texture has NO head or fists on it. The v1 minion drew the body
    /// alone, a headless Golem. The minion now draws all four parts in
    /// PreDraw at the vanilla anchors (halved for 0.5 scale): head at
    /// body.Center + (-1.5, -28.5) [vanilla aiStyle 46 anchor (-3, -57)],
    /// fists at (-42, -4.5) and (+39, -4.5) [vanilla AI_047 anchors
    /// (-84, -9) / (+78, -9)]. The head blinks frame 1 near the ends of
    /// each fireball cycle (vanilla FindFrame 246: localAI[0] == 1 when
    /// the fireball timer is &lt;20 or &gt;280 of 300). The head's side
    /// frames (+2/+4) only apply to the DETACHED head in vanilla, so the
    /// attached minion head never uses them. Vanilla never flips any
    /// Golem part (no spriteDirection in AI_045/046/047), so nothing is
    /// flipped here either.
    ///
    /// SIMPLIFICATIONS (v1):
    /// - One minion, not four NPCs: the head and fists are NOT separate
    ///   minions. Fist punches are melee lunges by the body (60-tick charge,
    ///   30-tick windup pinned in place, speed-12 launch, boom on launch,
    ///   return after 700 px — the vanilla fist timings). Fireballs and eye
    ///   lasers fire from the head position on the body.
    /// - The head never detaches (Attacks 6/7 skipped); phase 2 (faster
    ///   fireballs + eye lasers) begins after 30 s active instead of at
    ///   &lt;50% head HP. The phase-2 fireball muzzle shift (+/-20 toward
    ///   the target's side) is adapted from the detached head's +/-40
    ///   shift in vanilla.
    /// - The stomp jump is a flying lunge, not a gravity jump: the minion is
    ///   tileCollide = false, so it dives at the target's position and
    ///   "lands" (boom + dust) on arrival instead of touching ground.
    /// - The body-invulnerable-until-head-dies mechanic, the Temple/Jungle
    ///   "enrage" (balance x2), the 3000 px despawn, and the dead ai[0]==3
    ///   fist chase state (no setter exists in 1.4.4) are NOT ported.
    ///
    /// SOUNDS: SoundID.Item14 (boom) on fist launch and on stomp landing —
    /// the only two sounds in the Golem AI. No fireball/laser sounds exist
    /// in vanilla, so none are played for those attacks.
    /// </summary>
    public class GolemMinion : ModProjectile
    {
        public override string Texture => "Terraria/Images/NPC_245";
        // DisplayName comes from the localization file
        // (Mods.CelestialAerus.Projectiles.GolemMinion.DisplayName).

        // --- Constants (vanilla aiStyle 45/46/47 values) ---
        private const int MinionDamage = 6000;
        private const float TargetRange = 1000f;

        // States (Projectile.ai[0]); Projectile.ai[1] is the state timer.
        private const int StateHover = 0;
        private const int StateStompTelegraph = 1;
        private const int StateStompFlight = 2;
        private const int StateFistWindup = 3;
        private const int StateFistFlight = 4;

        // Stomp (Attack 1): ~300 ticks between jumps solo; 20-tick crouch telegraph.
        private const int StompCooldown = 300;
        private const int StompTelegraphTime = 20;
        private const int StompFlightTimeout = 75;
        private const float StompSpeed = 12.1f; // vanilla jump Y velocity (X is 4*direction; magnitude ~12.7)
        private const float StompLandDistance = 80f;

        // Fist punch (Attack 2): 60-tick charge, 30-tick windup, speed 12, 700 px range.
        private const int FistChargeTime = 60;
        private const int FistWindupTime = 30;
        private const float FistSpeed = 12f;
        private const float FistMaxRange = 700f;
        private const int FistFlightTimeout = 60;

        // Head fireball (Attacks 3/4): every 300 ticks; ~150 in phase 2.
        private const int FireballInterval = 300;
        private const float FireballSpeed = 8f;

        // Eye laser (Attack 5, phase 2): hazard-process timer, threshold 60 + rand(600).
        private const float EyeBeamSpeedTwo = 11f;
        private const float EyeBeamSpeedOne = 12f;

        private const int Phase2Age = 1800; // 30 s: faster fireballs + eye lasers

        // Head offsets at 0.5 scale (vanilla: head anchor (Center.X - 3,
        // Center.Y - 57); fireball muzzle Center + (0, 10); eyes at +/-18 X,
        // -22 Y; side eye at +/-30 X).
        private static readonly Vector2 HeadOffset = new Vector2(-1.5f, -28.5f);
        private static readonly Vector2 FireballMuzzle = new Vector2(0f, 5f);
        // Fist anchors at 0.5 scale (vanilla AI_047: body.Center + velocity
        // + (-84, -9) left / (+78, -9) right; the velocity lead is dropped
        // for the drawn fists — the body moves slowly and the lead would
        // make the fists swim).
        private static readonly Vector2 LeftFistOffset = new Vector2(-42f, -4.5f);
        private static readonly Vector2 RightFistOffset = new Vector2(39f, -4.5f);

        // Distinct hover offset so the boss minions don't stack on one point.
        private static readonly Vector2 HoverOffset = new Vector2(140f, -140f);

        // --- State (fields; only the owner makes spawn decisions) ---
        private int age;
        private int stompTimer;
        private int fireballTimer;
        private int laserTimer;
        private int laserThreshold = 360; // re-rolled each cycle: 60 + rand(600)
        private int fistCharge;
        private int nextFistSide; // 0 = left fist next, 1 = right fist next
        private Vector2 stompTarget;
        private Vector2 fistAnchor;
        private Vector2 fistTarget;

        public override void SetDefaults()
        {
            // Hitbox matches the 140x140 vanilla hitbox at 0.5 scale.
            Projectile.width = 70;
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
                HoverToward(player.Center + HoverOffset, 4f, 0.15f);
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
            int state = (int)Projectile.ai[0];
            int stateTimer = (int)Projectile.ai[1];
            Projectile.ai[1]++;

            // Golem doesn't rotate in vanilla and never flips (no
            // spriteDirection in AI_045/046/047); he just stands there.
            Projectile.rotation = 0f;

            // Animation (vanilla FindFrame, type 245 — 7 frames): frame 0
            // static normally (Golem has no walk cycle); the stomp
            // telegraph runs the jump wind-up frames 0-4 in 4-tick bands
            // over the 20-tick telegraph (vanilla ai[1] -20..-1 maps to
            // stateTimer 0..19). Frames 5-6 exist in the texture but
            // FindFrame never shows them — the PreDraw still divides by 7.
            Projectile.frame = state == StateStompTelegraph ? Math.Min(stateTimer / 4, 4) : 0;

            // --- Head attacks: independent of the body's state, like vanilla ---
            // (the head fires on its own timers while the body jumps and punches).
            // The timers run on ALL clients so the head's blink frame (drawn
            // from fireballTimer in PreDraw) stays in sync; only the owner
            // actually spawns the projectiles. Vanilla resets each timer at
            // its threshold BEFORE any line-of-sight check (the head's
            // fireball has no LOS check at all in vanilla — the minion keeps
            // the gate as an adaptation but resets first, so a blocked shot
            // is lost, not deferred).
            if (target != null)
            {
                // Fireball (Attacks 3/4): every 300 ticks, ~150 in phase 2.
                fireballTimer++;
                int fireballInterval = phase2 ? FireballInterval / 2 : FireballInterval;
                if (fireballTimer >= fireballInterval)
                {
                    fireballTimer = 0;
                    if (Projectile.owner == Main.myPlayer
                        && Collision.CanHit(Projectile.Center, 1, 1, target.Center, 1, 1))
                        FireFireball(target, phase2);
                }

                // Eye laser (Attack 5): phase 2 only, hazard-process timing.
                // Vanilla re-rolls the 60+rand(600) threshold every tick; the
                // minion fixes one per cycle — same mean, simpler.
                if (phase2)
                {
                    laserTimer++;
                    if (laserTimer >= laserThreshold)
                    {
                        laserTimer = 0;
                        laserThreshold = 60 + Main.rand.Next(600);
                        if (Projectile.owner == Main.myPlayer
                            && Collision.CanHit(Projectile.Center, 1, 1, target.Center, 1, 1))
                            FireEyeBeams(target);
                    }
                }
            }

            // --- Body state machine ---
            switch (state)
            {
                case StateHover:
                    HoverToward(player.Center + HoverOffset, 4f, 0.15f);

                    if (Projectile.owner != Main.myPlayer || target == null)
                        break;

                    // Stomp: every ~300 ticks when a target exists.
                    stompTimer++;
                    if (stompTimer >= StompCooldown)
                    {
                        stompTimer = 0;
                        stompTarget = target.Center;
                        SetState(StateStompTelegraph);
                        break;
                    }

                    // Fist punch: 60-tick charge, then windup — but only if
                    // the target is on this fist's side (vanilla side check,
                    // AI_047: left fist needs target X < fist X + 100, right
                    // fist needs target X > fist X - 100 — measured from the
                    // FIST's center, halved to +/-50 here). Otherwise the
                    // charge resets and the fist keeps waiting.
                    fistCharge++;
                    if (fistCharge >= FistChargeTime)
                    {
                        bool leftFist = nextFistSide == 0;
                        float fistX = Projectile.Center.X + (leftFist ? LeftFistOffset.X : RightFistOffset.X);
                        bool sideOk = leftFist
                            ? target.Center.X < fistX + 50f
                            : target.Center.X > fistX - 50f;
                        if (sideOk)
                        {
                            fistCharge = 0;
                            fistAnchor = Projectile.Center;
                            fistTarget = target.Center;
                            nextFistSide = 1 - nextFistSide;
                            SetState(StateFistWindup);
                        }
                        else
                        {
                            fistCharge = 0;
                        }
                    }
                    break;

                case StateStompTelegraph:
                    // 20-tick crouch telegraph: settle and dip slightly.
                    Projectile.velocity *= 0.9f;
                    Projectile.velocity.Y += 0.2f;
                    if (Projectile.velocity.Y > 2f)
                        Projectile.velocity.Y = 2f;
                    if (stateTimer >= StompTelegraphTime)
                    {
                        // Launch at the locked target point.
                        Vector2 dir = (stompTarget - Projectile.Center).SafeNormalize( Vector2.UnitY);
                        Projectile.velocity = dir * StompSpeed;
                        SetState(StateStompFlight);
                    }
                    break;

                case StateStompFlight:
                    // Arc gravity for the jump feel.
                    Projectile.velocity.Y += 0.25f;
                    if (Vector2.Distance(Projectile.Center, stompTarget) <= StompLandDistance
                        || stateTimer >= StompFlightTimeout)
                    {
                        // Landing: boom + dust/gore burst (vanilla landing).
                        SoundEngine.PlaySound(SoundID.Item14, Projectile.Center);
                        for (int i = 0; i < 20; i++)
                        {
                            Dust.NewDust(Projectile.position, Projectile.width, Projectile.height,
                                i % 2 == 0 ? DustID.Stone : DustID.Smoke,
                                Main.rand.NextFloat(-3f, 3f), Main.rand.NextFloat(-4f, 1f));
                        }
                        Projectile.velocity *= 0.3f;
                        SetState(StateHover);
                    }
                    break;

                case StateFistWindup:
                    // 30 ticks pinned to the anchor, dust telegraph during
                    // the first 15 ticks (vanilla AI_047: 1 dust/tick of
                    // type 228, spawned around the fist and drifting inward).
                    Projectile.Center = fistAnchor;
                    Projectile.velocity = Vector2.Zero;
                    if (stateTimer < 15)
                    {
                        Vector2 scatter = Main.rand.NextVector2Circular(40f, 40f);
                        Vector2 inward = scatter * -0.05f;
                        Dust dust = Dust.NewDustPerfect(Projectile.Center + inward + scatter + Main.rand.NextVector2Circular(10f, 10f), 228, inward);
                        dust.fadeIn = 1.5f;
                        dust.scale = 0.5f;
                        dust.noGravity = true;
                    }
                    if (stateTimer >= FistWindupTime)
                    {
                        if (target == null)
                        {
                            // Target gone mid-windup: cancel the punch.
                            SetState(StateHover);
                            break;
                        }
                        fistTarget = target.Center;
                        Vector2 dir = (fistTarget - Projectile.Center).SafeNormalize( Vector2.UnitX);
                        Projectile.velocity = dir * FistSpeed;
                        // Boom on the first tick of the flying punch.
                        SoundEngine.PlaySound(SoundID.Item14, Projectile.Center);
                        SetState(StateFistFlight);
                    }
                    break;

                case StateFistFlight:
                    // Straight-line punch; returns when 700 px from the
                    // anchor, or on timeout. Contact damage is automatic.
                    if (Main.rand.NextBool(2))
                    {
                        Dust.NewDust(Projectile.position, Projectile.width, Projectile.height,
                            DustID.Smoke, -Projectile.velocity.X * 0.1f, -Projectile.velocity.Y * 0.1f);
                    }
                    if (Vector2.Distance(Projectile.Center, fistAnchor) > FistMaxRange
                        || stateTimer >= FistFlightTimeout)
                    {
                        SetState(StateHover);
                    }
                    break;
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            // The Golem is four textures drawn as one assembly (see the
            // class doc): body (NPC_245, 7 frames), head (NPC_246, 6
            // frames), left fist (NPC_247, 1 frame), right fist (NPC_248,
            // 1 frame). Frame counts read from the game's own
            // npcFrameCount table in the binary and HARDCODED (dividing
            // the body by 5 mis-frames every frame — the texture has 2
            // extra frames FindFrame never shows). Nothing is flipped:
            // vanilla never sets spriteDirection for any Golem part.
            Vector2 screenCenter = Projectile.Center - Main.screenPosition;

            // Body (frame animated in AI(): 0 static, 0-4 telegraph).
            const int bodyFrameCount = 7;
            Texture2D bodyTex = TextureAssets.Projectile[Projectile.type].Value;
            int bodyFrameHeight = bodyTex.Height / bodyFrameCount;
            int bodyFrameIndex = (int)MathHelper.Clamp(Projectile.frame, 0, bodyFrameCount - 1);
            Rectangle bodyFrame = new Rectangle(0, bodyFrameIndex * bodyFrameHeight, bodyTex.Width, bodyFrameHeight);
            Main.EntitySpriteDraw(bodyTex, screenCenter, bodyFrame, lightColor, 0f, bodyFrame.Size() / 2f, Projectile.scale, SpriteEffects.None, 0);

            // Fists (single frame each, pinned at their anchors).
            Texture2D leftFistTex = TextureAssets.Npc[NPCID.GolemFistLeft].Value;
            Rectangle leftFistFrame = new Rectangle(0, 0, leftFistTex.Width, leftFistTex.Height);
            Main.EntitySpriteDraw(leftFistTex, screenCenter + LeftFistOffset, leftFistFrame, lightColor, 0f, leftFistFrame.Size() / 2f, Projectile.scale, SpriteEffects.None, 0);
            Texture2D rightFistTex = TextureAssets.Npc[NPCID.GolemFistRight].Value;
            Rectangle rightFistFrame = new Rectangle(0, 0, rightFistTex.Width, rightFistTex.Height);
            Main.EntitySpriteDraw(rightFistTex, screenCenter + RightFistOffset, rightFistFrame, lightColor, 0f, rightFistFrame.Size() / 2f, Projectile.scale, SpriteEffects.None, 0);

            // Head (vanilla FindFrame 246, attached: frame 1 while the
            // fireball timer is <20 or >interval-20, else frame 0).
            const int headFrameCount = 6;
            bool phase2 = age >= Phase2Age;
            int fireballInterval = phase2 ? FireballInterval / 2 : FireballInterval;
            int headFrameIndex = (fireballTimer < 20 || fireballTimer > fireballInterval - 20) ? 1 : 0;
            Texture2D headTex = TextureAssets.Npc[NPCID.GolemHead].Value;
            int headFrameHeight = headTex.Height / headFrameCount;
            Rectangle headFrame = new Rectangle(0, headFrameIndex * headFrameHeight, headTex.Width, headFrameHeight);
            Main.EntitySpriteDraw(headTex, screenCenter + HeadOffset, headFrame, lightColor, 0f, headFrame.Size() / 2f, Projectile.scale, SpriteEffects.None, 0);

            return false;
        }

        private void FireFireball(NPC target, bool phase2)
        {
            // Muzzle at the head position, shifted toward the target's side
            // in phase 2 (adapted from the detached head's +/-40 shift in
            // vanilla aiStyle 46; the attached head fires from Center +
            // (0, 10) with no shift).
            Vector2 muzzle = Projectile.Center + HeadOffset + FireballMuzzle;
            if (phase2)
                muzzle.X += (target.Center.X >= Projectile.Center.X ? 20f : -20f);
            Vector2 vel = (target.Center - muzzle).SafeNormalize( Vector2.UnitX) * FireballSpeed;
            // Vanilla head fireballs are RAW damage 18/24 with no difficulty
            // scaling; the minion uses the flat 6000 like every other attack.
            SpawnFriendlyProjectile(muzzle, vel, ProjectileID.Fireball);
        }

        private void FireEyeBeams(NPC target)
        {
            Vector2 headCenter = Projectile.Center + HeadOffset;
            float dx = target.Center.X - headCenter.X;
            // Two beams when the target is within the head's width of the
            // head's X (vanilla: localAI[1] == 0, i.e. |dx| < head width
            // 70 — halved to 35 here). One beam from the near eye otherwise.
            if (Math.Abs(dx) < 35f)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector2 eye = headCenter + new Vector2(9f * side, -11f);
                    Vector2 vel = (target.Center - eye).SafeNormalize( Vector2.UnitX) * EyeBeamSpeedTwo;
                    // Vanilla pushes the muzzle 3x the velocity (33 px)
                    // along the aim; halved to 1.5x (16.5 px).
                    SpawnFriendlyProjectile(eye + vel * 1.5f, vel, ProjectileID.EyeBeam, timeLeft: 300);
                }
            }
            else
            {
                // Target to one side: ONE beam from the eye on that side.
                int side = dx > 0f ? 1 : -1;
                Vector2 eye = headCenter + new Vector2(15f * side, -11f);
                Vector2 vel = (target.Center - eye).SafeNormalize( Vector2.UnitX) * EyeBeamSpeedOne;
                SpawnFriendlyProjectile(eye + vel * 1.5f, vel, ProjectileID.EyeBeam, timeLeft: 300);
            }
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
        /// friendly — vanilla boss projectiles (Fireball, EyeBeam) default
        /// to hostile, which would damage the player instead of enemies.
        /// DamageType is forced to Generic (typeless) so the shot doesn't
        /// scale with the owner's magic bonuses. Pass timeLeft &lt; 0 to
        /// leave the vanilla lifetime alone.
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
