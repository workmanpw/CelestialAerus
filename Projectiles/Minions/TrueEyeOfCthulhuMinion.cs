using System;
using System.Collections.Generic;
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
    /// True Eye of Cthulhu boss minion (Brigitte's Charm, 2026-11-18):
    /// the Moon Lord's detached eye (NPC 400, NPCID.MoonLordFreeEye —
    /// displayed in-game as "True Eye of Cthulhu"; NOTE: type 404 is the
    /// Stardust Worm Tail, not the True Eye) rewritten as a friendly
    /// minion at 50% visual scale. It runs the real eye's 10-step attack
    /// cycle (MoonLordAttacksArray2, 1200 ticks) against the owner's
    /// enemies: chase, Phantasmal Bolt shots, the Phantasmal Sphere
    /// hexagon + launch, the spiraling Phantasmal Eye dash, and the
    /// sweeping Phantasmal Deathray.
    ///
    /// AI ported from the decompiled Terraria 1.4.5 NPC.AI_081_
    /// TrueEyeOfCthulhu (lines 49186-49892) and FindFrame case 400;
    /// drawing from Main.DrawNPC's type-400 branch (line 32138). The
    /// eye's AI is stable across versions (1.3-era content, unchanged
    /// in 1.4.4); all IDs use ProjectileID./NPCID. names, never raw
    /// numbers.
    ///
    /// THE PUPIL: vanilla draws the eye in two parts — the eyeball
    /// (NPC_400, 4 frames of 80x80) and a separate pupil
    /// (TextureAssets.Extra[19]) offset from the center on a 30px
    /// ellipse by localAI[0] (angle) and localAI[1] (extension 0-1),
    /// sized by localAI[2] (scale 0.4-1.15). The minion keeps those as
    /// pupilAngle / pupilExtension / pupilScale fields, updated by each
    /// attack exactly like vanilla, and draws both parts in PreDraw
    /// (offsets and pupil size halved for the 0.5 scale).
    ///
    /// ATTACK CYCLE (MoonLordAttacksArray2 — [attack, ticks]):
    /// [0 chase 53] [1 bolts 90] [0 53] [2 spheres 135] [0 53]
    /// [3 spiral 200] [0 53] [4 deathray 375] [0 53] [2 spheres 135].
    /// - Chase (0): hover 200px above the target at speed 24 (lerped
    ///   over 30 ticks); the pupil tracks the target with a 20-tick
    ///   lead. Separation from other True Eyes within 100px.
    /// - Bolts (1): stops, aims with a 20-tick lead, fires a
    ///   Phantasmal Bolt (462) at speed 8 from the pupil at ticks 76
    ///   and 83 (vanilla also lists tick 90, but the attack ends at
    ///   tick 89 so that shot never fires); Item6 telegraph at 55.
    /// - Spheres (2): the pupil orbits a 6-point hexagon (radius 30),
    ///   spawning a Phantasmal Sphere (454) at each vertex (ticks 15,
    ///   25, 35, 45, 55, 65); the eye hops up at 75 (spheres rise
    ///   with it), aims, then dashes at the target at 105 while all
    ///   six spheres launch together at speed 12.
    /// - Spiral (3): the eye dashes in a spiral (speed 8 ramping to
    ///   20, turn rate decaying 5% every 40 ticks), firing a spread
    ///   shot every 10 ticks from tick 45.
    /// - Deathray (4): 180-tick telegraph (dust converges on the eye),
    ///   then a 180-tick beam sweeping 120 degrees at 2π/540 rad/tick,
    ///   starting 60 degrees off-aim so it sweeps across the target.
    ///
    /// SUBSTITUTIONS (same verified findings as MoonLordMinion — see
    /// its class doc):
    /// - Phantasmal Deathray (455) CANNOT be reused: aiStyle 84 anchors
    ///   only to NPC types 396/400 (it reads the owner's localAI eye
    ///   state). The minion reproduces the sweep with a stream of
    ///   friendly DeathLasers (100) along the same 120-degree arc at
    ///   the same sweep rate, one every 3 ticks (the True Eye's lesser
    ///   ray — thinner than the head's), timeLeft 300, Zombie_104 on
    ///   fire.
    /// - PhantasmalEye (452) CANNOT be reused: its homing phase
    ///   hardcodes Main.player targets (aiStyle 82) — a friendly 452
    ///   would home at the player and self-destruct. The minion fires
    ///   friendly HallowBossRainbowStreaks (873) instead, the only
    ///   vanilla projectile with built-in friendly NPC homing (ai[0]
    ///   = target NPC index, ai[1] = 0.7 hue), in the same spiral
    ///   pattern and timings.
    /// - PhantasmalSphere (454) stick phase skipped: aiStyle 83 anchors
    ///   the sphere to Main.npc[ai[1]] with no validity guard, and the
    ///   minion is a projectile, not an NPC. Spheres spawn with
    ///   ai[0] = 30, ai[1] = -1 (exactly what the True Eye does, minus
    ///   the anchor), fan out and drift immediately, and launch
    ///   together on the vanilla launch tick. The minion tracks its
    ///   spheres by index (linkedSpheres) so it never touches the
    ///   Moon Lord minion's spheres.
    ///
    /// NOT PORTED (boss-only or meaningless for a minion):
    /// - The core-death check (the eye dies when NPC 398 dies — the
    ///   minion has no core NPC; it lives as long as the charm is
    ///   equipped).
    /// - The ai[0] == -2 spawn state (tied to the Moon Lord's head
    ///   attack clock). The minion instead plays the ai[0] == -1 wake-up
    ///   (pupil wiggle / spin / settle) compressed 3x, 180 -> 60 ticks.
    /// - The 1.4.5 telegraph sparkles (DrawPrettyStarSparkle) and the
    ///   1-in-420 ambient roar.
    /// - The sphere-launch roar (vanilla plays it every tick for 15
    ///   ticks — noise pollution for a minion).
    ///
    /// Minion adaptations: all damage is flat 6000, typeless
    /// (DamageClass.Generic), per the charm's design. The cycle pauses
    /// with no target (the eye idles near the player, pupil watching
    /// the owner) and resumes when an enemy appears. When the charm
    /// summons three eyes (matching vanilla's three), each staggers its
    /// cycle clock by a third of the loop (vanilla staggers them the
    /// same way at spawn) so they don't attack in lockstep; the chase
    /// separation keeps them from stacking. State lives in
    /// Projectile.ai[0] (synced) plus private fields (single-player
    /// focus, like the other boss minions — timers are not synced).
    /// Projectile.ai has only 3 elements in tML 1.4.4 (maxAI = 3), so
    /// NO code here touches ai index 3 — ever (it throws every tick).
    /// </summary>
    public class TrueEyeOfCthulhuMinion : ModProjectile
    {
        public override string Texture => "Terraria/Images/NPC_400";

        private const float TargetRange = 1000f;
        private const int MinionDamage = 6000;     // flat, typeless (charm design)

        // Hover offset near the player (no target): above-RIGHT, so the
        // boss minions don't all stack on one point.
        private static readonly Vector2 IdleOffset = new Vector2(280f, -180f);

        // The vanilla 10-step attack cycle (MoonLordAttacksArray2):
        // [attack, duration ticks]. Chase gaps are (1200-935)/5 = 53.
        private static readonly int[] CycleAttacks = { 0, 1, 0, 2, 0, 3, 0, 4, 0, 2 };
        private static readonly int[] CycleDurations = { 53, 90, 53, 135, 53, 200, 53, 375, 53, 135 };

        // Sphere-orbit vertex order (vanilla AI_081 attack 2): the pupil
        // walks the hexagon 0->2->5->3->1->4->0, one leg per 10 ticks.
        private static readonly int[] SphereOrbitOrder = { 0, 2, 5, 3, 1, 4, 0 };

        // States (Projectile.ai[0]) — vanilla's ai[0] values.
        private const int StateIntro = -1;     // wake-up (vanilla ai[0] == -1)
        private const int StateChase = 0;
        private const int StateBolts = 1;
        private const int StateSpheres = 2;
        private const int StateSpiral = 3;
        private const int StateDeathray = 4;

        // --- State (ai[0] is the synced state; the rest are private) ---
        private NPC target = null;
        private int retargetTimer = 0;
        private int cycleTimer = 0;      // ticks into the 1200-tick cycle (vanilla ai[1])
        private int introTimer = 0;
        private bool introDone = false;
        // The pupil (vanilla localAI[0..2]): angle, extension 0-1, scale.
        private float pupilAngle = 0f;
        private float pupilExtension = 0f;
        private float pupilScale = 1f;
        private float aimAngle = 0f;     // sphere-launch aim (vanilla ai[2] in attack 2)
        private float spiralDir = 0f;    // spiral turn rate (vanilla ai[2] in attack 3)
        private float sweepAngle = 0f;   // deathray beam angle
        private float sweepDir = 1f;     // deathray sweep direction (+/-1)
        private List<int> linkedSpheres = new List<int>(); // spheres awaiting launch

        public override void SetDefaults()
        {
            // Vanilla True Eye hitbox 60x60, halved for the minion (0.5 scale).
            Projectile.width = 30;
            Projectile.height = 30;
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
            if (owner == null || !owner.active || owner.dead)
            {
                Projectile.velocity *= 0.95f;
                return;
            }

            // FOLLOW/RECALL (weapon-minion pattern, 2026-11-18):
            // teleport if too far from the owner; ramped follow speed
            // (16 at close range, 60 far away) when chasing home.
            float distToOwner = Vector2.Distance(Projectile.Center, owner.Center);
            if (distToOwner > 2000f)
            {
                Projectile.Center = owner.Center + IdleOffset;
                Projectile.velocity = Vector2.Zero;
                Projectile.netUpdate = true;
            }
            else if (distToOwner > 400f)
            {
                float followSpeed = MathHelper.Clamp(distToOwner * 0.05f, 16f, 60f);
                Vector2 toOwner = (owner.Center - Projectile.Center).SafeNormalize(Vector2.Zero);
                Projectile.velocity = Vector2.Lerp(Projectile.velocity, toOwner * followSpeed, 0.2f);
            }

            // Animation frames (vanilla FindFrame 400 — see UpdateFrame).
            // Runs every tick, even with no target, so the eye animates
            // while idling.
            UpdateFrame();

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
                // cycle pauses (timers don't advance) and resumes when a
                // target appears. The pupil watches the owner.
                SteerToward(owner.Center + IdleOffset, 8f, 0.15f);
                pupilAngle = Utils.AngleLerp(pupilAngle, (owner.Center - Projectile.Center).ToRotation(), 0.1f);
                pupilExtension = MathHelper.Lerp(pupilExtension, 0.5f, 0.1f);
                pupilScale = MathHelper.Lerp(pupilScale, 1f, 0.1f);
                Projectile.rotation = Utils.AngleLerp(Projectile.rotation, 0f, 0.1f);
                return;
            }

            // Wake-up intro (vanilla ai[0] == -1), played once.
            if (!introDone)
            {
                UpdateIntro();
                return;
            }

            // Attack cycle (vanilla MoonLordAttacksArray2 walk): advance
            // the cycle timer, find the current step, and derive the
            // within-attack timer (num3) from the step's start tick.
            cycleTimer++;
            int step = 0;
            int stepStart = 0;
            for (; step < CycleAttacks.Length; step++)
            {
                int dur = CycleDurations[step];
                if (dur + stepStart > cycleTimer)
                    break;
                stepStart += dur;
            }
            if (step >= CycleAttacks.Length)
            {
                step = 0;
                stepStart = 0;
                cycleTimer = 0;
            }
            int attackTimer = cycleTimer - stepStart;
            int attack = CycleAttacks[step];
            int oldAttack = (int)Projectile.ai[0];
            if (attack != oldAttack)
            {
                if (oldAttack == StateSpheres)
                    linkedSpheres.Clear(); // spheres belong to one attack only
                Projectile.ai[0] = attack;
                Projectile.netUpdate = true;
            }

            switch (attack)
            {
                case StateChase: UpdateChase(); break;
                case StateBolts: UpdateBolts(attackTimer); break;
                case StateSpheres: UpdateSpheres(attackTimer); break;
                case StateSpiral: UpdateSpiral(attackTimer); break;
                case StateDeathray: UpdateDeathray(attackTimer); break;
                default: Projectile.ai[0] = StateChase; break;
            }
        }

        // --- Wake-up intro (vanilla ai[0] == -1, compressed 3x) ---

        private void UpdateIntro()
        {
            // Vanilla's -1 branch runs 180 ticks (pupil wiggle 0-60, spin
            // up + extend 60-120, settle 120-180). Compressed to 60 ticks
            // (20-tick phases) so the minion joins the fight promptly.
            Projectile.ai[0] = StateIntro;
            Projectile.velocity *= 0.95f;
            introTimer++;

            if (introTimer < 20)
            {
                pupilScale = MathHelper.Lerp(pupilScale, 0.75f, 0.3f);
                pupilExtension = (float)Math.Sin(introTimer * MathHelper.TwoPi / 5f) * 0.35f;
                pupilAngle = pupilExtension < 0f ? MathHelper.Pi : 0f;
            }
            else if (introTimer < 40)
            {
                pupilScale = MathHelper.Lerp(pupilScale, 1f, 0.3f);
                pupilExtension = Math.Min(0.5f, pupilExtension + 0.05f);
                pupilAngle += MathHelper.Pi / 5f;
            }
            else
            {
                pupilScale = MathHelper.Lerp(pupilScale, 1.15f, 0.3f);
                pupilExtension = Math.Max(0f, pupilExtension - 0.05f);
            }

            if (introTimer >= 60)
            {
                introDone = true;
                introTimer = 0;
                // STAGGER (vanilla MoonLord_SpawnTrueEyeOfCthulhu offsets
                // each eye's cycle clock by how many eyes already exist):
                // when the charm summons three eyes, they start 400 ticks
                // apart (a third of the 1200-tick cycle each) instead of
                // attacking in lockstep. A lone eye starts at 0.
                int eyesBeforeMe = 0;
                for (int i = 0; i < Main.maxProjectiles; i++)
                {
                    Projectile other = Main.projectile[i];
                    if (other != null && other.active && other.type == Projectile.type
                        && other.owner == Projectile.owner && i < Projectile.whoAmI)
                        eyesBeforeMe++;
                }
                cycleTimer = (eyesBeforeMe % 3) * 400;
                Projectile.ai[0] = StateChase;
                Projectile.netUpdate = true;
            }
        }

        // --- Attack 0: chase / hover ---

        private void UpdateChase()
        {
            // Pupil tracks the target with a 20-tick lead (vanilla).
            Vector2 lead = target.Center + target.velocity * 20f - Projectile.Center;
            pupilAngle = Utils.AngleLerp(pupilAngle, lead.ToRotation(), 0.5f);
            pupilExtension = Math.Min(0.7f, pupilExtension + 0.05f);
            pupilScale = MathHelper.Lerp(pupilScale, 1f, 0.2f);

            // Hover 200px above the target at speed 24, lerped over 30
            // ticks (vanilla: velocity = (velocity*29 + desired)/30).
            Vector2 desired = (target.Center - Projectile.Center - Vector2.UnitY * 200f)
                .SafeNormalize(Vector2.Zero) * 24f;
            Projectile.velocity = (Projectile.velocity * 29f + desired) / 30f;

            // Separation: push away from other True Eyes within 100px
            // (vanilla pushes from other type-400 NPCs).
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile other = Main.projectile[i];
                if (other == null || !other.active || i == Projectile.whoAmI || other.type != Projectile.type)
                    continue;
                if (Vector2.Distance(Projectile.Center, other.Center) < 100f)
                {
                    if (Projectile.position.X < other.position.X)
                        Projectile.velocity.X -= 1.25f;
                    else
                        Projectile.velocity.X += 1.25f;
                    if (Projectile.position.Y < other.position.Y)
                        Projectile.velocity.Y -= 1.25f;
                    else
                        Projectile.velocity.Y += 1.25f;
                }
            }
        }

        // --- Attack 1: Phantasmal Bolts (90 ticks) ---

        private void UpdateBolts(int attackTimer)
        {
            if (attackTimer == 0)
            {
                // Vanilla retargets here; the minion already has its target.
                Projectile.netUpdate = true;
            }

            // Stop and aim (vanilla: velocity *= 0.95, zeroed below 1).
            Projectile.velocity *= 0.95f;
            if (Projectile.velocity.Length() < 1f)
                Projectile.velocity = Vector2.Zero;

            // Keep aiming until 19 ticks before the end (vanilla:
            // num3 < num - 14 - 5, with num = 90).
            if (attackTimer < 71)
            {
                Vector2 lead = target.Center + target.velocity * 20f - Projectile.Center;
                pupilAngle = Utils.AngleLerp(pupilAngle, lead.ToRotation(), 0.5f);
                pupilExtension = Math.Min(1f, pupilExtension + 0.05f);
            }
            if (pupilExtension > 1f)
                pupilExtension = 1f;

            if (attackTimer < 20)
                pupilScale = MathHelper.Lerp(pupilScale, 1.1f, 0.2f);
            else
                pupilScale = MathHelper.Lerp(pupilScale, 0.4f, 0.2f);

            // Telegraph sound at tick 55 (vanilla: num - 35).
            if (attackTimer == 55)
                SoundEngine.PlaySound(SoundID.Item6, Projectile.Center);

            // Fire at ticks 76 and 83 (vanilla: num - 14, num - 7).
            // Vanilla also lists tick 90 (num), but the attack ends at
            // tick 89, so that shot never fires — not ported.
            if (attackTimer == 76 || attackTimer == 83)
            {
                Vector2 spawn = Projectile.Center + PupilOffset();
                Vector2 vel = pupilAngle.ToRotationVector2() * 8f;
                SpawnFriendly(ProjectileID.PhantasmalBolt, spawn, vel);
            }
        }

        // --- Attack 2: Phantasmal Spheres (135 ticks) ---

        private void UpdateSpheres(int attackTimer)
        {
            if (attackTimer < 15)
            {
                // Slow down; the pupil retracts (vanilla).
                pupilExtension = Math.Max(0f, pupilExtension - 0.07f);
                pupilScale = MathHelper.Lerp(pupilScale, 0.4f, 0.2f);
                Projectile.velocity *= 0.8f;
                if (Projectile.velocity.Length() < 1f)
                    Projectile.velocity = Vector2.Zero;
            }
            else if (attackTimer < 75)
            {
                // Pupil orbits the hexagon, one 10-tick leg per vertex
                // pair; a sphere spawns at each leg start (ticks 15, 25,
                // 35, 45, 55, 65 — six spheres).
                int leg = (attackTimer - 15) / 10; // 0-5
                float legProgress = ((attackTimer - 15) % 10) / 10f;
                Vector2 a = HexVertex(SphereOrbitOrder[leg]);
                Vector2 b = HexVertex(SphereOrbitOrder[leg + 1]);
                Vector2 orbitPoint = Vector2.Lerp(a, b, legProgress);

                pupilAngle = orbitPoint.ToRotation();
                pupilExtension = MathHelper.Lerp(pupilExtension, orbitPoint.Length() / 30f, 0.5f);

                if ((attackTimer - 15) % 10 == 0)
                {
                    Vector2 dir = orbitPoint.SafeNormalize(Vector2.UnitY * -1f);
                    int p = SpawnFriendly(ProjectileID.PhantasmalSphere,
                        Projectile.Center + orbitPoint, dir * 4f, 30f, -1f);
                    if (p >= 0)
                        linkedSpheres.Add(p);
                }

                for (int i = 0; i < 2; i++)
                {
                    int d = Dust.NewDust(Projectile.Center + orbitPoint - Vector2.One * 4f, 0, 0, 229);
                    if (d >= 0)
                    {
                        Main.dust[d].velocity += orbitPoint / 15f;
                        Main.dust[d].noGravity = true;
                    }
                }
            }
            else if (attackTimer < 105)
            {
                // Rise and aim the launch (vanilla).
                pupilAngle = Utils.AngleLerp(pupilAngle, aimAngle - MathHelper.PiOver2, 0.2f);
                pupilScale = MathHelper.Lerp(pupilScale, 0.75f, 0.2f);
                if (attackTimer == 75)
                {
                    Projectile.netUpdate = true;
                    Projectile.velocity = new Vector2(0f, -7f);
                    // The linked spheres rise with the eye.
                    if (Projectile.owner == Main.myPlayer)
                    {
                        foreach (int p in linkedSpheres)
                        {
                            if (p < 0 || p >= Main.maxProjectiles)
                                continue;
                            Projectile s = Main.projectile[p];
                            if (s.active && s.type == ProjectileID.PhantasmalSphere && s.owner == Projectile.owner)
                            {
                                s.velocity += new Vector2(0f, -7f);
                                s.netUpdate = true;
                            }
                        }
                    }
                }
                Projectile.velocity.Y *= 0.96f;
                aimAngle = (target.Center - Projectile.Center).ToRotation() + MathHelper.PiOver2;
                RotateToward(aimAngle, MathHelper.Pi / 30f);
            }
            else if (attackTimer < 120)
            {
                // Dash at the target; all linked spheres launch with it
                // (vanilla re-sets the same velocity every tick here —
                // the aim is frozen, so once is equivalent).
                if (attackTimer == 105)
                {
                    Projectile.netUpdate = true;
                    Vector2 launchDir = (aimAngle - MathHelper.PiOver2).ToRotationVector2();
                    Projectile.velocity = launchDir * 24f;
                    LaunchLinkedSpheres(launchDir * 12f);
                }
            }
            else
            {
                // Settle; rotation eases back to upright (vanilla).
                Projectile.velocity *= 0.92f;
                Projectile.rotation = Utils.AngleLerp(Projectile.rotation, 0f, 0.2f);
            }
        }

        // --- Attack 3: spiraling Phantasmal Eye dash (200 ticks) ---

        private void UpdateSpiral(int attackTimer)
        {
            if (attackTimer < 15)
            {
                // Slow down; the pupil retracts (vanilla).
                pupilExtension = Math.Max(0f, pupilExtension - 0.07f);
                pupilScale = MathHelper.Lerp(pupilScale, 0.4f, 0.2f);
                Projectile.velocity *= 0.9f;
                if (Projectile.velocity.Length() < 1f)
                    Projectile.velocity = Vector2.Zero;
            }
            else if (attackTimer < 45)
            {
                // Pupil wiggle before the spiral (vanilla).
                pupilAngle = 0f;
                pupilExtension = (float)Math.Sin((attackTimer - 15) * MathHelper.TwoPi / 15f) * 0.5f;
                if (pupilExtension < 0f)
                    pupilAngle = MathHelper.Pi;
            }
            else if (attackTimer < 185)
            {
                // Spiral: the pupil angle advances by the turn rate each
                // tick and the eye dashes along it, speed ramping 8 -> 20.
                if (attackTimer == 45)
                {
                    spiralDir = (Main.rand.Next(2) == 0 ? -1f : 1f) * (MathHelper.TwoPi / 40f);
                    Projectile.netUpdate = true;
                }
                if ((attackTimer - 45) % 40 == 0)
                    spiralDir *= 0.95f;
                pupilAngle += spiralDir;
                pupilExtension = Math.Min(1f, pupilExtension + 0.05f);

                Vector2 pupilVec = pupilAngle.ToRotationVector2() * 30f * pupilExtension;
                Vector2 dashDir = pupilVec.SafeNormalize(Vector2.UnitX);
                float speed = MathHelper.Lerp(8f, 20f, (attackTimer - 45) / 140f);
                Projectile.velocity = dashDir * speed;
                Projectile.rotation = Utils.AngleLerp(Projectile.rotation,
                    Projectile.velocity.ToRotation() + MathHelper.PiOver2, 0.2f);

                // One spread shot every 10 ticks (vanilla: PhantasmalEye;
                // the minion fires the friendly rainbow-streak substitute
                // — see the class doc). Spawn 17px out along the dash
                // (vanilla: |(30,30)| * 0.4), speed 8.
                if ((attackTimer - 45) % 10 == 0)
                {
                    Vector2 spawn = Projectile.Center + dashDir * 17f;
                    SpawnFriendly(ProjectileID.HallowBossRainbowStreak,
                        spawn, dashDir * 8f, target.whoAmI, 0.7f);
                }
            }
            else
            {
                // Settle; rotation eases back to upright (vanilla).
                Projectile.velocity *= 0.88f;
                Projectile.rotation = Utils.AngleLerp(Projectile.rotation, 0f, 0.2f);
                pupilExtension = Math.Max(0f, pupilExtension - 0.07f);
                pupilScale = MathHelper.Lerp(pupilScale, 1f, 0.2f);
            }
        }

        // --- Attack 4: Phantasmal Deathray (375 ticks) ---

        private void UpdateDeathray(int attackTimer)
        {
            if (attackTimer < 180)
            {
                // Telegraph: hover still, pupil retracts, dust converges
                // on the eye (1/tick from 60, 2/tick from 120 — vanilla).
                pupilScale = MathHelper.Lerp(pupilScale, 1f, 0.2f);
                pupilExtension = Math.Max(0f, pupilExtension - 0.05f);
                Projectile.velocity *= 0.95f;
                if (Projectile.velocity.Length() < 1f)
                    Projectile.velocity = Vector2.Zero;

                if (attackTimer >= 60)
                {
                    int dustCount = attackTimer >= 120 ? 2 : 1;
                    for (int i = 0; i < dustCount; i++)
                    {
                        Vector2 at = Projectile.Center + Main.rand.NextVector2Circular(120f, 120f);
                        int d = Dust.NewDust(at, 1, 1, 229, 0f, 0f, 0, default, 1f);
                        if (d >= 0)
                        {
                            Main.dust[d].velocity = (Projectile.Center - at).SafeNormalize(Vector2.Zero) * 4f;
                            Main.dust[d].noGravity = true;
                        }
                    }
                }

                if (attackTimer == 179)
                {
                    // Lock the sweep: aim at the target, rotated 60° off
                    // — if the target is to the right, +60° (clockwise);
                    // if to the left, -60°. The beam then sweeps back
                    // across the target at 2π/540 rad/tick (vanilla).
                    float aim = (target.Center - Projectile.Center).ToRotation();
                    bool targetRight = target.Center.X >= Projectile.Center.X;
                    sweepAngle = aim + (targetRight ? (MathHelper.Pi / 3f) : -(MathHelper.Pi / 3f));
                    sweepDir = targetRight ? -1f : 1f;
                }
            }
            else if (attackTimer < 360)
            {
                if (attackTimer == 180)
                    SoundEngine.PlaySound(new SoundStyle("Terraria/Sounds/Zombie_104"), Projectile.Center);

                // DeathLaser sweep substitute (vanilla 455 can't anchor
                // to a minion — see the class doc): one bolt every 3
                // ticks along the sweeping angle, timeLeft 300.
                if ((attackTimer - 180) % 3 == 0)
                {
                    Vector2 dir = sweepAngle.ToRotationVector2();
                    SpawnFriendly(ProjectileID.DeathLaser, Projectile.Center + dir * 20f, dir * 20f, timeLeft: 300);
                }
                sweepAngle += sweepDir * (MathHelper.TwoPi / 540f);
                pupilAngle = sweepAngle;
                pupilExtension = Math.Min(1f, pupilExtension + 0.05f);
                Projectile.velocity *= 0.95f;
            }
            else
            {
                // Eye closes (vanilla).
                pupilExtension = Math.Max(0f, pupilExtension - 0.07f);
            }
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

        // Rotation easing toward a target angle, at most maxDelta per tick
        // (vanilla's rotation.AngleTowards).
        private void RotateToward(float targetAngle, float maxDelta)
        {
            float diff = MathHelper.WrapAngle(targetAngle - Projectile.rotation);
            Projectile.rotation += MathHelper.Clamp(diff, -maxDelta, maxDelta);
        }

        // The pupil's offset from the eye's center (vanilla DrawNPC
        // type-400 branch): the pupil angle on a 30px ellipse, scaled by
        // the extension — halved for the minion's 0.5 scale.
        private Vector2 PupilOffset()
        {
            return Utils.Vector2FromElipse(pupilAngle.ToRotationVector2(), new Vector2(30f) * pupilExtension) * Projectile.scale;
        }

        // Hexagon vertex for the sphere orbit (vanilla: (0,-30) rotated
        // by index * 2π/6 — vertex 0 is the top).
        private static Vector2 HexVertex(int index)
        {
            float angle = index * (MathHelper.TwoPi / 6f);
            return new Vector2((float)Math.Sin(angle) * 30f, -(float)Math.Cos(angle) * 30f);
        }

        // Launches all linked PhantasmalSpheres at once in the given
        // direction (vanilla: ai[0] = -1, velocity set — the spheres fly
        // straight from then on).
        private void LaunchLinkedSpheres(Vector2 velocity)
        {
            if (Projectile.owner != Main.myPlayer)
                return;
            foreach (int p in linkedSpheres)
            {
                if (p < 0 || p >= Main.maxProjectiles)
                    continue;
                Projectile s = Main.projectile[p];
                if (s.active && s.type == ProjectileID.PhantasmalSphere && s.owner == Projectile.owner)
                {
                    s.ai[0] = -1f;
                    s.velocity = velocity;
                    s.netUpdate = true;
                }
            }
            linkedSpheres.Clear();
        }

        /// <summary>
        /// Spawns a friendly version of a vanilla projectile at the given
        /// CENTER position, at the charm's flat 6000 typeless damage.
        /// Owner-only (the owner's client spawns and syncs it). Returns the
        /// projectile index, or -1 if the spawn failed or this client isn't
        /// the owner. Pass timeLeft &gt; 0 to override the vanilla lifetime
        /// (DeathLaser needs 300 — its 2700 default would flood the field).
        /// </summary>
        private int SpawnFriendly(int type, Vector2 center, Vector2 velocity, float ai0 = 0f, float ai1 = 0f, float ai2 = 0f, int timeLeft = -1)
        {
            if (Projectile.owner != Main.myPlayer)
                return -1;
            int p = Projectile.NewProjectile(Projectile.GetSource_FromThis(), center, velocity, type, MinionDamage, 0f, Projectile.owner, ai0, ai1, ai2);
            if (p < 0 || p >= Main.maxProjectiles)
                return -1;
            Projectile spawned = Main.projectile[p];
            spawned.Center = center; // NewProjectile takes a top-left position; we aim by center
            spawned.friendly = true;
            spawned.hostile = false;
            spawned.usesLocalNPCImmunity = true;
            spawned.localNPCHitCooldown = 20;
            spawned.DamageType = DamageClass.Generic;
            if (timeLeft > 0)
                spawned.timeLeft = timeLeft;
            spawned.netUpdate = true;
            return p;
        }

        // Vanilla FindFrame animation (NPC 400, 4 frames): the frame
        // counter cycles 0-15, frame = counter/4 (4 ticks/frame), and
        // runs double speed during the chase (0) and spiral (3) attacks.
        private void UpdateFrame()
        {
            Projectile.frameCounter++;
            int state = (int)Projectile.ai[0];
            if (state == StateChase || state == StateSpiral)
                Projectile.frameCounter++;
            if (Projectile.frameCounter >= 16)
                Projectile.frameCounter = 0;
            Projectile.frame = (Projectile.frameCounter % 16) / 4;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            // Eyeball (NPC_400, 4 frames of 80x80 — origin 40,40 in
            // vanilla DrawNPC, i.e. the frame center).
            Texture2D eyeTex = TextureAssets.Projectile[Projectile.type].Value;
            const int frameCount = 4;
            int frameHeight = eyeTex.Height / frameCount;
            Rectangle frame = new Rectangle(0, Projectile.frame * frameHeight, eyeTex.Width, frameHeight);
            Vector2 drawPos = Projectile.Center - Main.screenPosition;
            Main.EntitySpriteDraw(eyeTex, drawPos, frame, lightColor, Projectile.rotation, frame.Size() / 2f, Projectile.scale, SpriteEffects.None, 0);

            // Pupil (Extra_19, vanilla DrawNPC type-400 branch): offset
            // from the center on the pupil ellipse, sized by the pupil
            // scale — both halved for the minion's 0.5 scale.
            Texture2D pupilTex = TextureAssets.Extra[19].Value;
            Rectangle pupilFrame = new Rectangle(0, 0, pupilTex.Width, pupilTex.Height);
            Main.EntitySpriteDraw(pupilTex, drawPos + PupilOffset(), pupilFrame, lightColor, Projectile.rotation, pupilTex.Size() / 2f, pupilScale * Projectile.scale, SpriteEffects.None, 0);
            return false;
        }
    }
}
