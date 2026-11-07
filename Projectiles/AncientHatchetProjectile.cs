using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Localization;
using CelestialAerus.AerusBuffs;
using CelestialAerus.Effects;

namespace CelestialAerus.Projectiles
{
    public class AncientHatchetProjectile : ModProjectile
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

        // --- Fallback return triggers ---
        private const int NoTargetTimeout = 60;          // ~1 s with no target
        private const int LowTimeReturnThreshold = 60;   // start returning with this much timeLeft

        // --- State ---
        private int homingTimer = 0;
        private int noTargetTimer = 0;
        private int bounceCount = 0;
        private bool isReturning = false;
        private bool isStuck = false;
        private int stuckNPCWho = -1;
        private int stickTimer = 0;
        private int dotCounter = 0;
        private float originalDamage = 0f;
        private bool initialized = false;
        private NPC lastHitNPC = null;

        private readonly List<Vector2> trailPositions = new List<Vector2>();
        private const int MaxTrail = 24;

        public override string Texture => "CelestialAerus/Textures/AncientHatchet";
        public override LocalizedText DisplayName => Language.GetOrRegister("Ancient Hatchet");

        public override void SetDefaults()
        {
            Projectile.CloneDefaults(ProjectileID.PossessedHatchet);
            Projectile.width = 28;
            Projectile.height = 28;
            Projectile.scale = 0.7f;

            Projectile.aiStyle = -1;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = -1;

            Projectile.timeLeft = 300;
            Projectile.extraUpdates = 1;

            Projectile.tileCollide = false;
            Projectile.light = 1f;

            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 60;
        }

        public override void AI()
        {
            Player player = Main.player[Projectile.owner];
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

            // Item-side return trigger: the Ancient Hatchet item sets ai[0]=1
            // to call the projectile home. The projectile never read ai[0]
            // before, so that trigger did nothing.
            if (Projectile.ai[0] == 1f && !isReturning)
            {
                Projectile.ai[0] = 0f;
                StartReturning();
                return;
            }

            Projectile.rotation += RotationSpeed;
            if (Projectile.rotation > MathHelper.TwoPi)
                Projectile.rotation -= MathHelper.TwoPi;

            // --- Shader ribbon trail ---
            // Record a short position history so the ribbon follows the arc
            // of the throw instead of a single straight line. Add every tick:
            // the old distance check could never seed the first point (with
            // an empty list it compared the center to itself, distance 0),
            // so the trail stayed empty forever and only the dust showed.
            if (isStuck)
            {
                trailPositions.Clear();
            }
            else
            {
                trailPositions.Add(Projectile.Center);
                if (trailPositions.Count > MaxTrail)
                    trailPositions.RemoveAt(0);
            }

            // --- Dust Trail ---
            if (Main.rand.NextBool(2))
            {
                int dust = Dust.NewDust(
                    Projectile.position - new Vector2(10, 10),
                    Projectile.width + 20,
                    Projectile.height + 20,
                    DustID.Torch,
                    0f, 0f, 0, default, 1.5f
                );
                Main.dust[dust].noGravity = true;
                Main.dust[dust].velocity = Projectile.velocity * 0.2f + Main.rand.NextVector2Circular(2f, 2f);
                Main.dust[dust].fadeIn = 0f;
                Main.dust[dust].color = Color.Orange;
                Main.dust[dust].noLight = false;
                Main.dust[dust].alpha = 100;
                Main.dust[dust].scale = 1.8f + Main.rand.NextFloat(0.5f);

                if (Main.rand.NextBool(2))
                {
                    int spark = Dust.NewDust(
                        Projectile.position - new Vector2(8, 8),
                        Projectile.width + 16,
                        Projectile.height + 16,
                        DustID.Torch,
                        0f, 0f, 0, default, 1.2f
                    );
                    Main.dust[spark].noGravity = true;
                    Main.dust[spark].velocity = Projectile.velocity * 0.3f + Main.rand.NextVector2Circular(3f, 3f);
                    Main.dust[spark].color = Color.OrangeRed;
                    Main.dust[spark].alpha = 150;
                    Main.dust[spark].fadeIn = 0f;
                }
            }

            // --- Stuck state ---
            if (isStuck)
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
                        for (int i = 0; i < 6; i++)
                        {
                            Dust.NewDust(target.position, target.width, target.height,
                                DustID.Torch, 0f, 0f, 0, default, 1.2f);
                        }
                    }
                }

                stickTimer++;
                if (stickTimer >= MaxStickTime)
                {
                    StartReturning();
                }
                return;
            }

            // --- Returning state ---
            if (isReturning)
            {
                Vector2 toPlayer = player.Center - Projectile.Center;
                float dist = toPlayer.Length();

                if (dist <= ReturnKillDistance)
                {
                    Projectile.Kill();
                    return;
                }

                // No far-distance kill: a thrown hatchet always flies home,
                // however far it went. Killing it when far away is what made
                // it vanish instead of returning when it hit nothing.
                Projectile.velocity = toPlayer.SafeNormalize(Vector2.Zero) * ReturnSpeed;
                return;
            }

            // --- Homing state ---

            // Fallback #1: not enough timeLeft to home safely -> return.
            if (Projectile.timeLeft <= LowTimeReturnThreshold)
            {
                StartReturning();
                return;
            }

            homingTimer++;
            if (homingTimer >= HomingDelay)
            {
                NPC target = null;
                float minDist = 1000f;
                foreach (NPC npc in Main.npc)
                {
                    if (npc.CanBeChasedBy(Projectile) && npc != lastHitNPC)
                    {
                        float d = Vector2.Distance(Projectile.Center, npc.Center);
                        if (d < minDist)
                        {
                            minDist = d;
                            target = npc;
                        }
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
                    // Fallback #2: no valid target for too long -> return.
                    noTargetTimer++;
                    if (noTargetTimer >= NoTargetTimeout)
                    {
                        StartReturning();
                    }
                }
            }
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            if (isStuck)
                return;

            Player player = Main.player[Projectile.owner];

            // Apply the Celestial Flames debuff plus the shared Celestial
            // enemy debuffs, buffs and biome buffs. Healing is handled below
            // by the hatchet's own lifesteal, so no extra heal is added here.
            if (player != null && player.active)
                CelestialHitEffects.ApplyCelestialHitBundle(player, target, healAmount: 0);

            if (!isReturning)
            {
                int lifeStealAmount = 50;
                if (player.statLife + lifeStealAmount > player.statLifeMax2)
                    lifeStealAmount = player.statLifeMax2 - player.statLife;
                player.Heal(lifeStealAmount);
                player.HealEffect(lifeStealAmount);
            }

            SoundEngine.PlaySound(SoundID.Item14, Projectile.Center);
            for (int i = 0; i < 40; i++)
            {
                int dust = Dust.NewDust(Projectile.position, Projectile.width, Projectile.height,
                    DustID.OrangeTorch, 0f, 0f, 100, default, 2f);
                Main.dust[dust].velocity *= 3f;
                if (Main.rand.NextBool())
                {
                    Main.dust[dust].scale = 0.5f;
                    Main.dust[dust].fadeIn = 0f;
                }
            }
            for (int j = 0; j < 70; j++)
            {
                int dust = Dust.NewDust(Projectile.position, Projectile.width, Projectile.height,
                    DustID.OrangeTorch, 0f, 0f, 100, default, 3f);
                Main.dust[dust].noGravity = true;
                Main.dust[dust].velocity *= 5f;
                dust = Dust.NewDust(Projectile.position, Projectile.width, Projectile.height,
                    DustID.OrangeTorch, 0f, 0f, 100, default, 2f);
                Main.dust[dust].velocity *= 2f;
            }
            for (int k = 0; k < 20; k++)
            {
                int dust = Dust.NewDust(Projectile.position, Projectile.width, Projectile.height,
                    DustID.Flare, 0f, 0f, 150, default, 1.5f);
                Main.dust[dust].noGravity = true;
                Main.dust[dust].velocity *= 4f;
                Main.dust[dust].color = new Color(255, 150, 0, 100);
            }

            if (!isReturning)
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
            Projectile.damage = (int)(originalDamage * 0.25f);
            Projectile.timeLeft = 9999;
            Projectile.netUpdate = true;

            Player player = Main.player[Projectile.owner];
            if (player != null && player.active)
            {
                Vector2 toPlayer = player.Center - Projectile.Center;
                if (toPlayer != Vector2.Zero)
                    Projectile.velocity = toPlayer.SafeNormalize(Vector2.Zero) * ReturnSpeed;
            }
        }

        public override void SendExtraAI(BinaryWriter writer)
        {
            writer.Write(isStuck);
            writer.Write(isReturning);
            writer.Write((short)stuckNPCWho);
            writer.Write((short)stickTimer);
            writer.Write((short)dotCounter);
            writer.Write((byte)bounceCount);
            writer.Write((short)homingTimer);
            writer.Write((short)noTargetTimer);
            writer.Write(originalDamage);
            writer.Write(initialized);
        }

        public override void ReceiveExtraAI(BinaryReader reader)
        {
            isStuck        = reader.ReadBoolean();
            isReturning    = reader.ReadBoolean();
            stuckNPCWho    = reader.ReadInt16();
            stickTimer     = reader.ReadInt16();
            dotCounter     = reader.ReadInt16();
            bounceCount    = reader.ReadByte();
            homingTimer    = reader.ReadInt16();
            noTargetTimer  = reader.ReadInt16();
            originalDamage = reader.ReadSingle();
            initialized    = reader.ReadBoolean();
        }

        public override bool OnTileCollide(Vector2 oldVelocity) => false;

        public override bool PreDraw(ref Color lightColor)
        {
            Color lightColorCopy = lightColor;
            Texture2D texture = TextureAssets.Projectile[Type].Value;

            // Fire trail, Calamity-style: the shaders stay simple and the
            // drawing does the work — three layered ribbons (outer glow, fire
            // body, white-hot core) with independent flicker so the flame
            // tongues dance instead of pulsing as one smooth band.
            float trailStartOffset = texture.Height * 0.5f * Projectile.scale;
            float outerHalfWidth = Math.Max(Projectile.width, Projectile.height) * 0.5f * Projectile.scale; // VertexStrip treats this as a half-width
            float flameHalfWidth = outerHalfWidth * 0.62f;
            float coreHalfWidth = outerHalfWidth * 0.3f;
            float fireTime = Main.GlobalTimeWrappedHourly;

            float Flicker(float progress, float phase, float amount)
            {
                // Keep the head size stable and let the flame tongues dance
                // along the middle and tail of the ribbon.
                float envelope = (float)Math.Sin(MathHelper.Clamp(progress, 0f, 1f) * MathHelper.Pi);
                float wave = (float)Math.Sin(fireTime * 12f - progress * 28f + phase);
                float lick = (float)Math.Sin(fireTime * 7f + progress * 17f + phase * 1.7f);
                return 1f + envelope * amount * (wave * 0.7f + lick * 0.3f);
            }

            // Layer 1 — outer glow: broad, deep orange, dim. The heat haze.
            ShaderTrailDraw.Draw(
                trailPositions,
                trailPositions.Count,
                progress => ShaderTrailDraw.WithAlpha(
                    Color.Lerp(new Color(150, 24, 0), new Color(255, 112, 12), progress),
                    (float)Math.Pow(progress, 0.85f) * 0.55f * Flicker(progress, 0f, 0.16f)),
                progress => outerHalfWidth * (float)Math.Pow(progress, 1.1f) * Flicker(progress, 0f, 0.32f),
                "CelestialRibbon",
                effect =>
                {
                    SafeShaderDraw.Set(effect, "TrailStartColor", Color.White);
                    SafeShaderDraw.Set(effect, "TrailEndColor", Color.White);
                    SafeShaderDraw.Set(effect, "TrailPulseSpeed", 9f);
                    SafeShaderDraw.Set(effect, "TrailPulseIntensity", 0.32f);
                },
                trailStartOffset);

            // Layer 2 — flame body: the fire shader does the burning, the
            // drawing sets the shape. FireDecay stays 0 so the burn is even
            // along the ribbon and the vertex fade does the head-to-tail falloff.
            ShaderTrailDraw.Draw(
                trailPositions,
                trailPositions.Count,
                progress => ShaderTrailDraw.WithAlpha(
                    Color.Lerp(new Color(255, 90, 0), new Color(255, 190, 80), progress),
                    (float)Math.Pow(progress, 0.9f) * 0.9f * Flicker(progress, 2.1f, 0.1f)),
                progress => flameHalfWidth * (float)Math.Pow(progress, 1.2f) * Flicker(progress, 2.1f, 0.22f),
                "CelestialFireTrail",
                effect =>
                {
                    SafeShaderDraw.Set(effect, "TrailStartColor", Color.White);
                    SafeShaderDraw.Set(effect, "TrailEndColor", Color.White);
                    SafeShaderDraw.Set(effect, "TrailPulseSpeed", 9f);
                    SafeShaderDraw.Set(effect, "TrailPulseIntensity", 0.3f);
                    SafeShaderDraw.Set(effect, "TrailFades", 0f);
                    SafeShaderDraw.Set(effect, "TrailFadeSpeed", 1f);
                    SafeShaderDraw.Set(effect, "TrailGlows", 1f);
                    SafeShaderDraw.Set(effect, "TrailGlowColor", new Color(255, 153, 51));
                    SafeShaderDraw.Set(effect, "FireIntensity", 1.35f);
                    SafeShaderDraw.Set(effect, "FireColor", new Color(255, 115, 13));
                    SafeShaderDraw.Set(effect, "FireDecay", 0f);
                    SafeShaderDraw.Set(effect, "FireEmbers", 1f);
                },
                trailStartOffset);

            // Layer 3 — white-hot core: narrow, bright, the hottest part.
            ShaderTrailDraw.Draw(
                trailPositions,
                trailPositions.Count,
                progress => ShaderTrailDraw.WithAlpha(
                    Color.Lerp(new Color(255, 180, 60), new Color(255, 248, 200), progress),
                    (float)Math.Pow(progress, 0.95f) * 0.95f * Flicker(progress, 4.2f, 0.08f)),
                progress => coreHalfWidth * (float)Math.Pow(progress, 1.3f) * Flicker(progress, 4.2f, 0.15f),
                "CelestialRibbon",
                effect =>
                {
                    SafeShaderDraw.Set(effect, "TrailStartColor", Color.White);
                    SafeShaderDraw.Set(effect, "TrailEndColor", Color.White);
                    SafeShaderDraw.Set(effect, "TrailPulseSpeed", 13f);
                    SafeShaderDraw.Set(effect, "TrailPulseIntensity", 0.35f);
                },
                trailStartOffset);

            return SafeShaderDraw.DrawWithShader("AncientHatchetFlame",
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
                    SafeShaderDraw.Set(effect, "FlameIntensity", 1.55f);
                    SafeShaderDraw.Set(effect, "FlameDistortion", 1.45f);
                    SafeShaderDraw.Set(effect, "FlameCoreColor", new Color(255, 218, 92));
                    SafeShaderDraw.Set(effect, "FlameEdgeColor", new Color(224, 48, 0));
                    SafeShaderDraw.Set(effect, "AxeSpinSpeed", 5f);
                });
        }
    }
}
