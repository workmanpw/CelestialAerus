using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.GameContent;
using Terraria.Audio;
using Terraria.Localization;

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
        private const float MaxReturnDistance  = 3000f;

        private const int OrbitDuration = 120;
        private const float OrbitRadius = 120f;
        private const float OrbitAngularSpeed = MathHelper.TwoPi * 2f / OrbitDuration;
        private const float OrbitSearchRange = 1000f;

        private const int StrikeTimeout = 240;

        // --- Fallback return triggers ---
        private const int NoTargetTimeout = 120;         // ~1 s
        private const int LowTimeReturnThreshold = 60;

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

            Projectile.timeLeft = 300;
            Projectile.extraUpdates = 1;

            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;

            Projectile.light = 1f;

            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 60;

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

            SpawnTrailDust();

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
                Projectile.velocity = dir.SafeNormalize(Vector2.Zero) * HomingSpeed;
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
                StartReturning();
                return;
            }

            NPC target = Main.npc[stuckNPCWho];
            if (!target.active || target.life <= 0)
            {
                StartReturning();
                return;
            }

            Projectile.Center = target.Center;

            dotCounter++;
            if (dotCounter % DotInterval == 0)
            {
                if (Projectile.owner == Main.myPlayer)
                {
                    target.SimpleStrikeNPC((int)originalDamage, 0, false, 0f, DamageClass.Melee);
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
                        if (i % 2 != 0)
                            Main.dust[d].color = CosmicRainbow(i * 0.15f);
                    }
                }
            }

            stickTimer++;
            if (stickTimer >= MaxStickTime)
                StartReturning();
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

            if (dist > MaxReturnDistance)
            {
                Projectile.Kill();
                return;
            }

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
                Main.dust[d].velocity = Main.rand.NextVector2Circular(2f, 2f);
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
            Projectile.damage = (int)originalDamage;
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
                    stuckNPCWho = target.whoAmI;
                    stickTimer = 0;
                    dotCounter = 0;
                    Projectile.velocity = Vector2.Zero;
                    Projectile.timeLeft = 9999;
                    Projectile.damage = 0;
                    Projectile.tileCollide = false;
                    target.AddBuff(BuffID.Daybreak, 300);
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
            if (!Main.rand.NextBool(2)) return;

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
            Main.dust[dust].fadeIn = 1f;
            Main.dust[dust].noLight = false;
            Main.dust[dust].alpha = 80;
            Main.dust[dust].scale = 1.6f + Main.rand.NextFloat(0.6f);

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
                Main.dust[shard].color = CosmicRainbow(Main.rand.NextFloat() * 6f);
                Main.dust[shard].alpha = 60;
                Main.dust[shard].fadeIn = 0.6f;
                Main.dust[shard].scale = 1.1f + Main.rand.NextFloat(0.5f);
            }
        }

        private void SpawnExplosionDust()
        {
            // Firework burst — a swarm of rainbow flames.
            for (int i = 0; i < 30; i++)
            {
                int dust = Dust.NewDust(Projectile.position, Projectile.width, Projectile.height,
                    DustID.RainbowTorch, 0f, 0f, 0, default, 2f);
                Main.dust[dust].velocity *= 3f;
                if (Main.rand.NextBool())
                {
                    Main.dust[dust].scale = 0.6f;
                    Main.dust[dust].fadeIn = 1f + Main.rand.Next(10) * 0.1f;
                }
            }

            // Fast crystalline shards, each with a different hue.
            for (int j = 0; j < 40; j++)
            {
                int dust = Dust.NewDust(Projectile.position, Projectile.width, Projectile.height,
                    DustID.PurpleCrystalShard, 0f, 0f, 0, default, 2.2f);
                Main.dust[dust].velocity *= 3.5f;
                Main.dust[dust].noGravity = true;
                Main.dust[dust].color = CosmicRainbow(Main.rand.NextFloat() * 6f);
            }

            // Slow drifting rainbow motes for the aftermath glow.
            for (int k = 0; k < 20; k++)
            {
                int dust = Dust.NewDust(Projectile.position, Projectile.width, Projectile.height,
                    DustID.RainbowTorch, 0f, 0f, 0, default, 1.4f);
                Main.dust[dust].noGravity = true;
                Main.dust[dust].velocity *= 1.8f;
                Main.dust[dust].alpha = 120;
                Main.dust[dust].fadeIn = 0.5f;
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
            short lastHitWho = reader.ReadInt16();
            lastHitNPC = (lastHitWho >= 0 && lastHitWho < Main.maxNPCs) ? Main.npc[lastHitWho] : null;
        }

        public override bool OnTileCollide(Vector2 oldVelocity) => false;

        public override Color? GetAlpha(Color lightColor)
        {
            return Rainbow();
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D texture = TextureAssets.Projectile[Type].Value;
            Vector2 drawPos = Projectile.Center - Main.screenPosition;
            Main.EntitySpriteDraw(texture, drawPos, null,
                Projectile.GetAlpha(lightColor),
                Projectile.rotation,
                texture.Size() / 2f,
                Projectile.scale,
                SpriteEffects.None, 0);
            return false;
        }
    }
}
