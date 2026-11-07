using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.GameContent;
using Terraria.Audio;
using Terraria.Localization;
using CelestialAerus.Effects;

namespace CelestialAerus.Projectiles
{
    public class ThrownCelestialAerus : ModProjectile
    {
        // --- Constants ---
        private const int HomingDelay = 15;
        private const float HomingSpeed = 22f;
        private const int MaxBounces = 3;
        private const float ReturnSpeed = 44f;
        private const int MaxStickTime = 300;
        private const int DotInterval = 30;
        private const float RotationSpeed = 0.4f;

        private const float ReturnKillDistance = ReturnSpeed * 2f;

        private const int OrbitDuration = 120;
        private const float OrbitRadius = 120f;
        private const float OrbitAngularSpeed = MathHelper.TwoPi * 2f / OrbitDuration;
        private const float OrbitSearchRange = 1000f;

        private const int StrikeTimeout = 240;

        // --- Fallback return triggers ---
        private const int NoTargetTimeout = 120;         // ~1 s
        private const int LowTimeReturnThreshold = 60;

        // --- Cosmic fire explosion (on stick) ---
        private const int ExplosionDuration = 45;        // ticks the burst lasts
        private const float ExplosionRadius = 170f;      // AoE damage radius
        private const float ExplosionDamageMult = 2f;    // burst damage x original
        private const float StickDotDamageMult = 1.5f;   // stuck DoT tick damage x original
        private const float StrikeDamageMult = 1.5f;     // diving strike hit damage x original

        // --- State ---
        private int homingTimer = 0;
        private int noTargetTimer = 0;
        private int bounceCount = 0;
        private bool isReturning = false;
        private bool isStuck = false;
        private bool isOrbiting = false;
        private bool isStriking = false;
        private bool hasOrbited = false;
        private int stuckNPCWho = -1;
        private int stickTimer = 0;
        private int dotCounter = 0;
        private int orbitTimer = 0;
        private float orbitAngle = 0f;
        private int orbitTargetWho = -1;
        private float originalDamage = 0f;
        private bool initialized = false;
        private NPC lastHitNPC = null;

        // --- Cosmic fire explosion state ---
        private bool explosionActive = false;
        private int explosionTimer = 0;
        private int explosionSeed = 0;

        private readonly List<Vector2> trailPositions = new List<Vector2>();
        private const int MaxTrailPoints = 28;

        public override string Texture => "CelestialAerus/Textures/CelestialAerus";
        public override LocalizedText DisplayName => Language.GetOrRegister("Celestial Aerus");

        public override void SetDefaults()
        {
            Projectile.width = 120;
            Projectile.height = 120;
            Projectile.scale = 1f;

            Projectile.friendly = true;
            Projectile.hostile = false;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = -1;

            // Fusion with the old version: a longer life (old 600, current 300)
            // so it has time to bounce, stick, orbit and strike.
            Projectile.timeLeft = 450;
            Projectile.extraUpdates = 1;

            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;

            Projectile.light = 1f;

            Projectile.usesLocalNPCImmunity = true;
            // Balanced immunity frames: 60 hit too often (hit spam / lag),
            // 160 (old) hit too rarely. 120 keeps it punchy without spamming.
            Projectile.localNPCHitCooldown = 120;

            Projectile.aiStyle = -1;
        }

        private Player Owner
        {
            get
            {
                if (Projectile.owner < 0 || Projectile.owner >= Main.maxPlayers)
                    return null;
                return Main.player[Projectile.owner];
            }
        }

        public override void AI()
        {
            Player player = Owner;
            if (player == null || !player.active || player.dead)
            {
                Projectile.Kill();
                return;
            }

            if (!initialized)
            {
                originalDamage = Projectile.damage;
                initialized = true;
            }

            Projectile.rotation += RotationSpeed;
            if (Projectile.rotation > MathHelper.TwoPi)
                Projectile.rotation -= MathHelper.TwoPi;

            // Rainbow light source — cycles through hue wheel to match the sprite.
            Lighting.AddLight(Projectile.Center, Rainbow().ToVector3() * 1.3f);

            if (!isStuck)
            {
                trailPositions.Add(Projectile.Center);
                if (trailPositions.Count > MaxTrailPoints)
                    trailPositions.RemoveAt(0);
                SpawnTrailDust();
            }

            // Cosmic fire explosion: runs its course even if the stuck enemy
            // dies mid-burst and the blade bounces to a new target.
            if (explosionActive)
            {
                explosionTimer++;
                SpawnExplosionEmbers(2);
                Lighting.AddLight(Projectile.Center, new Vector3(1f, 0.45f, 0.1f) * (1f - (float)explosionTimer / ExplosionDuration) * 1.5f);
                if (explosionTimer >= ExplosionDuration)
                    explosionActive = false;
            }

            if (isStuck)     { HandleStuck();       return; }
            if (isOrbiting)  { HandleOrbit(player); return; }
            if (isStriking)  { HandleStrike();      return; }
            if (isReturning) { HandleReturn(player);return; }

            HandleHoming();
        }

        private void HandleHoming()
        {
            if (Projectile.timeLeft <= LowTimeReturnThreshold)
            {
                StartReturning();
                return;
            }

            homingTimer++;
            if (homingTimer < HomingDelay) return;

            NPC target = null;
            float minDist = 1000f;
            foreach (NPC npc in Main.npc)
            {
                if (npc.CanBeChasedBy(Projectile) && npc != lastHitNPC)
                {
                    float d = Vector2.Distance(Projectile.Center, npc.Center);
                    if (d < minDist) { minDist = d; target = npc; }
                }
            }

            if (target != null)
            {
                noTargetTimer = 0;
                Vector2 dir = target.Center - Projectile.Center;
                // Fusion with the old version: smooth steering (old lerped at
                // 0.15, current snapped the velocity). 0.35 tracks hard but
                // steers instead of snapping, so it reads as a thrown blade.
                Projectile.velocity = Vector2.Lerp(
                    Projectile.velocity,
                    dir.SafeNormalize(Vector2.Zero) * HomingSpeed,
                    0.35f);
            }
            else
            {
                noTargetTimer++;
                if (noTargetTimer >= NoTargetTimeout)
                    StartReturning();
            }
        }

        private void HandleStuck()
        {
            if (stuckNPCWho < 0 || stuckNPCWho >= Main.maxNPCs)
            {
                TryBounceToNewEnemy();
                return;
            }

            NPC target = Main.npc[stuckNPCWho];
            if (!target.active || target.life <= 0)
            {
                TryBounceToNewEnemy();
                return;
            }

            Projectile.Center = target.Center;

            dotCounter++;
            if (dotCounter % DotInterval == 0)
            {
                if (Projectile.owner == Main.myPlayer)
                {
                    // Extra damage while stuck: the DoT tick hits 1.5x.
                    target.SimpleStrikeNPC((int)(originalDamage * StickDotDamageMult), 0, false, 0f, DamageClass.Melee);
                }

                if (Main.netMode != NetmodeID.Server)
                {
                    // Alternating rainbow sparks and crystal shards so the
                    // DoT visual reads as cosmic, not fire.
                    for (int i = 0; i < 6; i++)
                    {
                        int id = (i % 2 == 0) ? DustID.RainbowTorch : DustID.PurpleCrystalShard;
                        int d = Dust.NewDust(target.position, target.width, target.height,
                            id, 0f, 0f, 0, default, 1.2f);
                        Main.dust[d].noGravity = true;
                        Main.dust[d].velocity *= 0.5f;
                        Main.dust[d].scale = 0.7f;
                        if (i % 2 != 0)
                            Main.dust[d].color = CosmicRainbow(i * 0.15f);
                    }
                }
            }

            stickTimer++;
            if (stickTimer >= MaxStickTime)
                TryBounceToNewEnemy();
        }

        // The blade's target died (or its stick ran out): instead of flying
        // home, bounce to a fresh enemy and keep fighting. Only returns
        // when there is nothing left to hit.
        private void TryBounceToNewEnemy()
        {
            NPC stuck = (stuckNPCWho >= 0 && stuckNPCWho < Main.maxNPCs) ? Main.npc[stuckNPCWho] : null;
            NPC target = null;
            float minDist = OrbitSearchRange;
            foreach (NPC npc in Main.npc)
            {
                if (npc == stuck) continue;
                if (!npc.CanBeChasedBy(Projectile)) continue;
                float d = Vector2.Distance(Projectile.Center, npc.Center);
                if (d < minDist) { minDist = d; target = npc; }
            }

            if (target == null)
            {
                StartReturning();
                return;
            }

            isStuck = false;
            stuckNPCWho = -1;
            stickTimer = 0;
            dotCounter = 0;
            bounceCount = 0;
            lastHitNPC = null;
            homingTimer = 0;
            noTargetTimer = 0;
            Projectile.damage = (int)originalDamage;
            // A clean 5-second window to reach the new enemy and stick. (Not
            // Math.Max: timeLeft is 9999 while stuck, and carrying that over
            // would let it chase an uncatchable enemy forever.)
            Projectile.timeLeft = 300;
            Projectile.tileCollide = false;
            Vector2 dir = target.Center - Projectile.Center;
            Projectile.velocity = dir.SafeNormalize(Vector2.Zero) * HomingSpeed;
            Projectile.netUpdate = true;
        }

        private void HandleReturn(Player player)
        {
            Vector2 toPlayer = player.Center - Projectile.Center;
            float dist = toPlayer.Length();

            if (dist <= ReturnKillDistance)
            {
                if (!hasOrbited)
                {
                    isReturning = false;
                    isOrbiting = true;
                    hasOrbited = true;
                    orbitTimer = 0;
                    orbitAngle = (Projectile.Center - player.Center).ToRotation();
                    Projectile.velocity = Vector2.Zero;
                    Projectile.netUpdate = true;
                    return;
                }

                Projectile.Kill();
                return;
            }

            // No far-distance kill: it always flies home, however far it
            // went (same fix as the Ancient Hatchet).
            Projectile.velocity = toPlayer.SafeNormalize(Vector2.Zero) * ReturnSpeed;
        }

        private void HandleOrbit(Player player)
        {
            orbitTimer++;
            orbitAngle += OrbitAngularSpeed;

            Projectile.Center = player.Center + orbitAngle.ToRotationVector2() * OrbitRadius;
            Projectile.velocity = Vector2.Zero;

            // Orbit sparkle — alternate rainbow flame and drifting crystal.
            if (Main.rand.NextBool(2))
            {
                int id = Main.rand.NextBool() ? DustID.RainbowTorch : DustID.PurpleCrystalShard;
                int d = Dust.NewDust(Projectile.position, Projectile.width, Projectile.height,
                    id, 0f, 0f, 0, default, 1.5f);
                Main.dust[d].noGravity = true;
                Main.dust[d].scale = 0.7f;
                Main.dust[d].velocity = Main.rand.NextVector2Circular(2f, 2f) * 0.5f;
                if (id == DustID.PurpleCrystalShard)
                    Main.dust[d].color = CosmicRainbow(Main.rand.NextFloat() * 6f);
            }

            if (orbitTimer < OrbitDuration) return;

            NPC target = FindNearestEnemy(OrbitSearchRange);
            if (target == null)
            {
                isOrbiting = false;
                StartReturning();
                return;
            }

            orbitTargetWho = target.whoAmI;
            isOrbiting = false;
            isStriking = true;
            homingTimer = 0;
            // The diving strike hits harder than a normal pass.
            Projectile.damage = (int)(originalDamage * StrikeDamageMult);
            Projectile.netUpdate = true;
        }

        private void HandleStrike()
        {
            homingTimer++;
            if (homingTimer >= StrikeTimeout)
            {
                isStriking = false;
                StartReturning();
                return;
            }

            if (orbitTargetWho < 0 || orbitTargetWho >= Main.maxNPCs)
            {
                isStriking = false;
                StartReturning();
                return;
            }

            NPC target = Main.npc[orbitTargetWho];
            if (!target.active || target.life <= 0)
            {
                isStriking = false;
                StartReturning();
                return;
            }

            Vector2 dir = target.Center - Projectile.Center;
            Projectile.velocity = dir.SafeNormalize(Vector2.Zero) * HomingSpeed;
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            if (isStuck) return;

            Player player = Owner;

            if (!isReturning && !isOrbiting && !isStriking)
            {
                int lifeStealAmount = 50;
                if (player.statLife + lifeStealAmount > player.statLifeMax2)
                    lifeStealAmount = player.statLifeMax2 - player.statLife;
                player.Heal(lifeStealAmount);
                player.HealEffect(lifeStealAmount);
            }

            SoundEngine.PlaySound(SoundID.Item14, Projectile.Center);
            SpawnExplosionDust();

            if (isStriking)
            {
                isStriking = false;
                orbitTargetWho = -1;
                StartReturning();
                Projectile.netUpdate = true;
                return;
            }

            if (!isReturning && !isOrbiting)
            {
                if (bounceCount >= MaxBounces - 1)
                {
                    isStuck = true;
                    trailPositions.Clear();
                    stuckNPCWho = target.whoAmI;
                    stickTimer = 0;
                    dotCounter = 0;
                    Projectile.velocity = Vector2.Zero;
                    Projectile.timeLeft = 9999;
                    Projectile.damage = 0;
                    Projectile.tileCollide = false;
                    target.AddBuff(BuffID.Daybreak, 300);

                    // Cosmic fire explosion: the blade erupts where it sticks.
                    TriggerExplosion();
                }
                else
                {
                    bounceCount++;
                    homingTimer = 0;
                    noTargetTimer = 0;
                    lastHitNPC = target;
                }
            }

            Projectile.netUpdate = true;
        }

        private void StartReturning()
        {
            if (isReturning) return;

            isReturning = true;
            isStuck = false;
            isOrbiting = false;
            isStriking = false;
            Projectile.damage = (int)(originalDamage * 0.25f);
            Projectile.timeLeft = 9999;
            Projectile.netUpdate = true;

            Player player = Owner;
            if (player != null && player.active)
            {
                Vector2 toPlayer = player.Center - Projectile.Center;
                if (toPlayer != Vector2.Zero)
                    Projectile.velocity = toPlayer.SafeNormalize(Vector2.Zero) * ReturnSpeed;
            }
        }

        private NPC FindNearestEnemy(float range)
        {
            NPC best = null;
            float bestDist = range;
            foreach (NPC npc in Main.npc)
            {
                if (!npc.CanBeChasedBy(Projectile)) continue;
                float d = Vector2.Distance(Projectile.Center, npc.Center);
                if (d < bestDist) { bestDist = d; best = npc; }
            }
            return best;
        }

        // Smooth sine-based rainbow — used for the sprite tint and the light
        // source. Cycles in lockstep with the projectile's rotation feel.
        private static Color Rainbow(float offset = 0f)
        {
            float t = Main.GlobalTimeWrappedHourly * 2f + offset;
            return new Color(
                0.5f + 0.5f * (float)Math.Sin(t),
                0.5f + 0.5f * (float)Math.Sin(t + MathHelper.TwoPi / 3f),
                0.5f + 0.5f * (float)Math.Sin(t + MathHelper.TwoPi * 2f / 3f)
            );
        }

        // Per-particle HSL rainbow with a bit of saturation / brightness
        // variance so no two dust motes look the same.
        private static Color CosmicRainbow(float offset = 0f)
        {
            float hue = (Main.GlobalTimeWrappedHourly * 0.4f + offset) % 1f;
            if (hue < 0f) hue += 1f;
            float sat = 0.85f + Main.rand.NextFloat() * 0.15f;
            float lum = 0.55f + Main.rand.NextFloat() * 0.15f;
            return Main.hslToRgb(hue, sat, lum);
        }

        private void SpawnTrailDust()
        {
            if (Main.netMode == NetmodeID.Server || !Main.rand.NextBool(2)) return;

            // Primary trail: rainbow flame sprite — uses its own native
            // colour cycle, so no .color override here.
            int dust = Dust.NewDust(
                Projectile.position - new Vector2(10, 10),
                Projectile.width + 20,
                Projectile.height + 20,
                DustID.RainbowTorch,
                0f, 0f, 0, default, 1.7f
            );
            Main.dust[dust].noGravity = true;
            Main.dust[dust].velocity = Projectile.velocity * 0.2f + Main.rand.NextVector2Circular(2f, 2f);
            Main.dust[dust].velocity *= 0.6f;
            Main.dust[dust].noLight = false;
            Main.dust[dust].scale = 0.7f + Main.rand.NextFloat(0.2f);

            // Secondary trail: crystalline shard with its own hue.
            if (Main.rand.NextBool(2))
            {
                int shard = Dust.NewDust(
                    Projectile.position - new Vector2(8, 8),
                    Projectile.width + 16,
                    Projectile.height + 16,
                    DustID.PurpleCrystalShard,
                    0f, 0f, 0, default, 1.2f
                );
                Main.dust[shard].noGravity = true;
                Main.dust[shard].velocity = Projectile.velocity * 0.3f + Main.rand.NextVector2Circular(3f, 3f);
                Main.dust[shard].velocity *= 0.6f;
                Main.dust[shard].color = CosmicRainbow(Main.rand.NextFloat() * 6f);
                Main.dust[shard].scale = 0.6f + Main.rand.NextFloat(0.2f);
            }
        }

        private void SpawnExplosionDust()
        {
            if (Main.netMode == NetmodeID.Server) return;

            // A compact, short-lived burst so repeated hits do not leave a cloud behind.
            for (int i = 0; i < 18; i++)
            {
                int dust = Dust.NewDust(Projectile.position, Projectile.width, Projectile.height,
                    DustID.RainbowTorch, 0f, 0f, 0, default, 2f);
                Main.dust[dust].noGravity = true;
                Main.dust[dust].velocity *= 2f;
                Main.dust[dust].scale = 0.7f;
                if (Main.rand.NextBool())
                {
                    Main.dust[dust].scale = 0.5f;
                }
            }

            // Fast crystalline shards, each with a different hue.
            for (int j = 0; j < 14; j++)
            {
                int dust = Dust.NewDust(Projectile.position, Projectile.width, Projectile.height,
                    DustID.PurpleCrystalShard, 0f, 0f, 0, default, 2.2f);
                Main.dust[dust].velocity *= 2.5f;
                Main.dust[dust].noGravity = true;
                Main.dust[dust].color = CosmicRainbow(Main.rand.NextFloat() * 6f);
                Main.dust[dust].scale = 0.7f;
            }

            // Slow drifting rainbow motes for the aftermath glow.
            for (int k = 0; k < 8; k++)
            {
                int dust = Dust.NewDust(Projectile.position, Projectile.width, Projectile.height,
                    DustID.RainbowTorch, 0f, 0f, 0, default, 1.4f);
                Main.dust[dust].noGravity = true;
                Main.dust[dust].velocity *= 1.2f;
                Main.dust[dust].scale = 0.6f;
            }
        }

        // Cosmic fire explosion: visual burst + AoE damage where the blade sticks.
        private void TriggerExplosion()
        {
            explosionActive = true;
            explosionTimer = 0;
            explosionSeed = Main.rand.Next();

            // Extra damage: one burst to every enemy in the blast radius.
            // Owner-only so it applies once in multiplayer (same pattern as
            // the stuck DoT).
            if (Projectile.owner == Main.myPlayer)
            {
                int explosionDamage = (int)(originalDamage * ExplosionDamageMult);
                foreach (NPC npc in Main.npc)
                {
                    if (!npc.active || !npc.CanBeChasedBy(Projectile))
                        continue;
                    if (Vector2.Distance(Projectile.Center, npc.Center) > ExplosionRadius)
                        continue;
                    int dir = npc.Center.X >= Projectile.Center.X ? 1 : -1;
                    npc.SimpleStrikeNPC(explosionDamage, dir, false, 4f, DamageClass.Melee);
                }
            }

            SoundEngine.PlaySound(SoundID.Item14, Projectile.Center);
            SpawnExplosionEmbers(16);
        }

        // Ember burst for the cosmic fire explosion: hot sparks and cosmic
        // shards thrown outward from the blast center.
        private void SpawnExplosionEmbers(int count)
        {
            if (Main.netMode == NetmodeID.Server)
                return;
            for (int i = 0; i < count; i++)
            {
                int id = Main.rand.NextBool() ? DustID.Torch : (Main.rand.NextBool() ? DustID.RainbowTorch : DustID.PurpleCrystalShard);
                Vector2 dir = Main.rand.NextVector2Circular(1f, 1f);
                if (dir == Vector2.Zero)
                    dir = Vector2.UnitX;
                dir = Vector2.Normalize(dir);
                float speed = Main.rand.NextFloat(3f, 8f);
                int d = Dust.NewDust(Projectile.Center - new Vector2(6, 6), 12, 12,
                    id, dir.X * speed, dir.Y * speed, 0, default, Main.rand.NextFloat(1.2f, 2f));
                Main.dust[d].noGravity = true;
                Main.dust[d].fadeIn = 0f;
                if (id == DustID.PurpleCrystalShard)
                    Main.dust[d].color = CosmicRainbow(Main.rand.NextFloat() * 6f);
            }
        }

        public override void SendExtraAI(BinaryWriter writer)
        {
            writer.Write(isStuck);
            writer.Write(isReturning);
            writer.Write(isOrbiting);
            writer.Write(isStriking);
            writer.Write(hasOrbited);
            writer.Write((short)stuckNPCWho);
            writer.Write((short)orbitTargetWho);
            writer.Write((short)stickTimer);
            writer.Write((short)dotCounter);
            writer.Write((byte)bounceCount);
            writer.Write((short)homingTimer);
            writer.Write((short)noTargetTimer);
            writer.Write((short)orbitTimer);
            writer.Write(orbitAngle);
            writer.Write(originalDamage);
            writer.Write(initialized);
            writer.Write(explosionActive);
            writer.Write((short)explosionTimer);
            writer.Write(explosionSeed);
            writer.Write((short)(lastHitNPC != null ? lastHitNPC.whoAmI : -1));
        }

        public override void ReceiveExtraAI(BinaryReader reader)
        {
            isStuck = reader.ReadBoolean();
            isReturning = reader.ReadBoolean();
            isOrbiting = reader.ReadBoolean();
            isStriking = reader.ReadBoolean();
            hasOrbited = reader.ReadBoolean();
            stuckNPCWho = reader.ReadInt16();
            orbitTargetWho = reader.ReadInt16();
            stickTimer = reader.ReadInt16();
            dotCounter = reader.ReadInt16();
            bounceCount = reader.ReadByte();
            homingTimer = reader.ReadInt16();
            noTargetTimer = reader.ReadInt16();
            orbitTimer = reader.ReadInt16();
            orbitAngle = reader.ReadSingle();
            originalDamage = reader.ReadSingle();
            initialized = reader.ReadBoolean();
            explosionActive = reader.ReadBoolean();
            explosionTimer = reader.ReadInt16();
            explosionSeed = reader.ReadInt32();
            short lastHitWho = reader.ReadInt16();
            lastHitNPC = (lastHitWho >= 0 && lastHitWho < Main.maxNPCs) ? Main.npc[lastHitWho] : null;
        }

        public override bool OnTileCollide(Vector2 oldVelocity) => false;

        public override Color? GetAlpha(Color lightColor)
        {
            return Rainbow();
        }

        // Cosmic fire explosion, drawn with the existing fire/glow shaders:
        // a cosmic halo, a cluster of fire blobs, and a white-hot core, all
        // expanding and fading over the burst. Uses the fireball texture so
        // the shaders have a soft round shape to work on.
        private void DrawExplosion()
        {
            Texture2D fireTex = ModContent.Request<Texture2D>("CelestialAerus/Textures/CelestialFireball").Value;
            float progress = (float)explosionTimer / ExplosionDuration;
            // Ease-out expansion: fast at first, then settles.
            float expand = 1f - (1f - progress) * (1f - progress);
            float fade = 1f - progress;
            // Flicker so the fire never looks static.
            float flick = 0.85f + 0.15f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 31f + explosionSeed);
            Vector2 center = Projectile.Center - Main.screenPosition;

            // Layer 1: cosmic halo (CosmicGlow), the widest and softest.
            float haloScale = (70f + 190f * expand) / (fireTex.Width * 0.5f);
            SafeShaderDraw.DrawWithShader("CosmicGlow",
                () => Main.spriteBatch.Draw(fireTex, center, null, new Color(1f, 1f, 1f, fade * 0.75f * flick), Main.GlobalTimeWrappedHourly * 0.4f, fireTex.Size() / 2f, haloScale, SpriteEffects.None, 0f),
                effect =>
                {
                    SafeShaderDraw.Set(effect, "GlowRadius", 90f);
                    SafeShaderDraw.Set(effect, "GlowInnerColor", new Color(120, 240, 255));
                    SafeShaderDraw.Set(effect, "GlowOuterColor", new Color(150, 60, 255));
                    SafeShaderDraw.Set(effect, "GlowPulseSpeed", 6f);
                    SafeShaderDraw.Set(effect, "GlowPulseIntensity", 0.35f);
                    SafeShaderDraw.Set(effect, "GlowRotates", 1f);
                    SafeShaderDraw.Set(effect, "GlowRotationSpeed", 0.8f);
                });

            // Layer 2: fire cluster (CelestialFireTrail) — several overlapping
            // fire blobs that spread apart as the blast expands, so it reads
            // as turbulent real fire instead of one smooth ball.
            int blobs = 4;
            for (int i = 0; i < blobs; i++)
            {
                float angle = explosionSeed * 0.7f + i * (MathHelper.TwoPi / blobs);
                float spread = (18f + 95f * expand) * (0.6f + 0.4f * (float)Math.Sin(explosionSeed + i * 2.3f));
                Vector2 offset = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * spread;
                float blobScale = (26f + 46f * expand) / (fireTex.Width * 0.5f);
                float blobFade = fade * (0.75f + 0.25f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 23f + i * 1.7f));
                SafeShaderDraw.DrawWithShader("CelestialFireTrail",
                    () => Main.spriteBatch.Draw(fireTex, center + offset, null, new Color(1f, 1f, 1f, MathHelper.Clamp(blobFade, 0f, 1f)), angle * 0.5f, fireTex.Size() / 2f, blobScale, SpriteEffects.None, 0f),
                    effect =>
                    {
                        SafeShaderDraw.Set(effect, "TrailStartColor", new Color(255, 179, 51));
                        SafeShaderDraw.Set(effect, "TrailEndColor", new Color(230, 77, 0));
                        SafeShaderDraw.Set(effect, "TrailFades", 0f);
                        SafeShaderDraw.Set(effect, "TrailFadeSpeed", 1f);
                        SafeShaderDraw.Set(effect, "TrailPulseSpeed", 9f);
                        SafeShaderDraw.Set(effect, "TrailPulseIntensity", 0.35f);
                        SafeShaderDraw.Set(effect, "TrailGlows", 1f);
                        SafeShaderDraw.Set(effect, "TrailGlowColor", new Color(255, 153, 51));
                        SafeShaderDraw.Set(effect, "FireIntensity", 1.5f);
                        SafeShaderDraw.Set(effect, "FireColor", new Color(255, 115, 13));
                        SafeShaderDraw.Set(effect, "FireDecay", 0.4f);
                        SafeShaderDraw.Set(effect, "FireEmbers", 1f);
                    });
            }

            // Layer 3: white-hot core (CelestialFireTrail), small and brightest.
            float coreScale = (20f + 55f * expand) / (fireTex.Width * 0.5f);
            SafeShaderDraw.DrawWithShader("CelestialFireTrail",
                () => Main.spriteBatch.Draw(fireTex, center, null, new Color(1f, 1f, 1f, fade * flick), -Main.GlobalTimeWrappedHourly * 0.6f, fireTex.Size() / 2f, coreScale, SpriteEffects.None, 0f),
                effect =>
                {
                    SafeShaderDraw.Set(effect, "TrailStartColor", new Color(255, 248, 220));
                    SafeShaderDraw.Set(effect, "TrailEndColor", new Color(255, 190, 80));
                    SafeShaderDraw.Set(effect, "TrailFades", 0f);
                    SafeShaderDraw.Set(effect, "TrailFadeSpeed", 1f);
                    SafeShaderDraw.Set(effect, "TrailPulseSpeed", 13f);
                    SafeShaderDraw.Set(effect, "TrailPulseIntensity", 0.3f);
                    SafeShaderDraw.Set(effect, "TrailGlows", 1f);
                    SafeShaderDraw.Set(effect, "TrailGlowColor", new Color(255, 220, 120));
                    SafeShaderDraw.Set(effect, "FireIntensity", 1.8f);
                    SafeShaderDraw.Set(effect, "FireColor", new Color(255, 200, 90));
                    SafeShaderDraw.Set(effect, "FireDecay", 0.2f);
                    SafeShaderDraw.Set(effect, "FireEmbers", 1f);
                });
        }

        // While the blade is stuck in an enemy, the enemy keeps burning in
        // cosmic fire: a small pulsing flame wrapped around the blade.
        private void DrawStuckFlame()
        {
            Texture2D fireTex = ModContent.Request<Texture2D>("CelestialAerus/Textures/CelestialFireball").Value;
            Vector2 center = Projectile.Center - Main.screenPosition;
            float pulse = 0.7f + 0.3f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 9f);
            float scale = (34f * pulse) / (fireTex.Width * 0.5f);
            SafeShaderDraw.DrawWithShader("CelestialFireTrail",
                () => Main.spriteBatch.Draw(fireTex, center, null, new Color(1f, 1f, 1f, 0.55f * pulse), Main.GlobalTimeWrappedHourly * 0.8f, fireTex.Size() / 2f, scale, SpriteEffects.None, 0f),
                effect =>
                {
                    SafeShaderDraw.Set(effect, "TrailStartColor", new Color(255, 179, 51));
                    SafeShaderDraw.Set(effect, "TrailEndColor", new Color(230, 77, 0));
                    SafeShaderDraw.Set(effect, "TrailFades", 0f);
                    SafeShaderDraw.Set(effect, "TrailFadeSpeed", 1f);
                    SafeShaderDraw.Set(effect, "TrailPulseSpeed", 9f);
                    SafeShaderDraw.Set(effect, "TrailPulseIntensity", 0.4f);
                    SafeShaderDraw.Set(effect, "TrailGlows", 1f);
                    SafeShaderDraw.Set(effect, "TrailGlowColor", new Color(255, 153, 51));
                    SafeShaderDraw.Set(effect, "FireIntensity", 1.3f);
                    SafeShaderDraw.Set(effect, "FireColor", new Color(255, 115, 13));
                    SafeShaderDraw.Set(effect, "FireDecay", 0.5f);
                    SafeShaderDraw.Set(effect, "FireEmbers", 1f);
                });
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Color lightColorCopy = lightColor;
            Texture2D texture = TextureAssets.Projectile[Type].Value;

            // Start the ribbon at the sword's rear edge and give it the
            // projectile's full width at the head, tapering to a point.
            float trailStartOffset = texture.Height * 0.5f * Projectile.scale;
            float trailHalfWidth = Math.Max(Projectile.width, Projectile.height) * 0.5f * Projectile.scale; // VertexStrip treats this as a half-width

            ShaderTrailDraw.Draw(
                trailPositions,
                trailPositions.Count,
                progress => ShaderTrailDraw.WithAlpha(Color.White, progress * progress * 0.9f),
                progress => trailHalfWidth * (float)Math.Pow(progress, 1.15f),
                "CelestialRibbon",
                effect =>
                {
                    SafeShaderDraw.Set(effect, "TrailStartColor", new Color(45, 220, 255));
                    SafeShaderDraw.Set(effect, "TrailEndColor", new Color(210, 65, 255));
                    SafeShaderDraw.Set(effect, "TrailPulseSpeed", 4f);
                    SafeShaderDraw.Set(effect, "TrailPulseIntensity", 0.22f);
                },
                trailStartOffset);

            // Cosmic fire: the explosion burst while it erupts, or the
            // smaller burning flame while the blade is stuck in an enemy.
            if (explosionActive)
                DrawExplosion();
            else if (isStuck)
                DrawStuckFlame();

            return SafeShaderDraw.DrawWithShader("CelestialTrail",
                () =>
                {

                Vector2 drawPos = Projectile.Center - Main.screenPosition;
                Main.EntitySpriteDraw(texture, drawPos, null,
                    Projectile.GetAlpha(lightColorCopy),
                    Projectile.rotation,
                    texture.Size() / 2f,
                    Projectile.scale,
                    SpriteEffects.None, 0);
                },
                (effect) =>
                {
                    SafeShaderDraw.Set(effect, "TrailWidth", 12f);
                    SafeShaderDraw.Set(effect, "TrailStartColor", new Color(102, 204, 255));
                    SafeShaderDraw.Set(effect, "TrailEndColor", new Color(26, 102, 230));
                    SafeShaderDraw.Set(effect, "TrailFades", 0f);
                    SafeShaderDraw.Set(effect, "TrailFadeSpeed", 1.3f);
                    SafeShaderDraw.Set(effect, "TrailPulseSpeed", 5f);
                    SafeShaderDraw.Set(effect, "TrailPulseIntensity", 0.4f);
                    SafeShaderDraw.Set(effect, "TrailGlows", 1f);
                    SafeShaderDraw.Set(effect, "TrailGlowColor", new Color(77, 179, 255));
                });
        }
    }
}
