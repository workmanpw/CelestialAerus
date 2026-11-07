using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Localization;
using Terraria.Audio;
using CelestialAerus.AerusBuffs;
using CelestialAerus.Effects;

namespace CelestialAerus.Projectiles
{
    public class AndromedaSeeker : ModProjectile
    {
        private readonly List<Vector2> trailPositions = new List<Vector2>();
        private const int MaxTrail = 15;

        public override void SetStaticDefaults()
        {
            ProjectileID.Sets.TrailCacheLength[Type] = MaxTrail;
            ProjectileID.Sets.TrailingMode[Type] = 0;
        }

        public override LocalizedText DisplayName => Language.GetOrRegister("Andromeda Seeker");
        public override string Texture => "CelestialAerus/Textures/AndromedasWrath";

        public override void SetDefaults()
        {
            Projectile.width = 76;
            Projectile.height = 76;
            Projectile.scale = 0.5f;
            Projectile.friendly = true;
            Projectile.hostile = false;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = 2;
            Projectile.timeLeft = 600;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 10;
            Projectile.aiStyle = -1;
            Projectile.tileCollide = false;
        }

        public override void AI()
        {
            trailPositions.Add(Projectile.Center);
            if (trailPositions.Count > MaxTrail)
                trailPositions.RemoveAt(0);

            // Purple-pink light.
            Lighting.AddLight(Projectile.Center, 0.7f, 0.1f, 0.7f);

            // Homing.
            float maxDetect = 1600f;
            float homingSpeed = 35f;
            NPC target = null;
            float minDist = maxDetect;

            foreach (NPC npc in Main.npc)
            {
                if (npc.CanBeChasedBy(Projectile, false))
                {
                    float dist = Vector2.Distance(Projectile.Center, npc.Center);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        target = npc;
                    }
                }
            }

            if (target != null)
            {
                Vector2 toTarget = target.Center - Projectile.Center;
                toTarget.Normalize();
                Projectile.velocity = (Projectile.velocity * 20f + toTarget * homingSpeed) / 21f;
            }

            Projectile.rotation += 0.2f;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D texture = TextureAssets.Projectile[Type].Value;

            // Start the ribbon at the sword's rear edge and give it the
            // projectile's full width at the head, tapering to a point.
            float trailStartOffset = texture.Height * 0.5f * Projectile.scale;
            float trailHalfWidth = Math.Max(Projectile.width, Projectile.height) * 0.5f * Projectile.scale; // VertexStrip treats this as a half-width

            ShaderTrailDraw.Draw(
                trailPositions,
                trailPositions.Count,
                progress =>
                {
                    float hue = (Main.GlobalTimeWrappedHourly * 0.3f + progress * 0.18f) % 1f;
                    return ShaderTrailDraw.WithAlpha(Main.hslToRgb(hue, 1f, 0.7f), progress * progress * 0.85f);
                },
                progress => trailHalfWidth * (float)Math.Pow(progress, 1.15f),
                "CelestialRibbon",
                effect =>
                {
                    SafeShaderDraw.Set(effect, "TrailStartColor", Color.White);
                    SafeShaderDraw.Set(effect, "TrailEndColor", Color.White);
                    SafeShaderDraw.Set(effect, "TrailPulseSpeed", 7f);
                    SafeShaderDraw.Set(effect, "TrailPulseIntensity", 0.22f);
                },
                trailStartOffset);

            return SafeShaderDraw.DrawWithShader("CelestialTrail",
                () =>
                {
                    Vector2 origin = texture.Size() / 2f;
                Vector2 headPos = Projectile.Center - Main.screenPosition;
                Main.EntitySpriteDraw(
                    texture,
                    headPos,
                    null,
                    Color.White * ((255 - Projectile.alpha) / 255f),
                    Projectile.rotation,
                    origin,
                    Projectile.scale,
                    SpriteEffects.None,
                    0
                );
                },
                (effect) =>
                {
                    SafeShaderDraw.Set(effect, "TrailWidth", 10f);
                    SafeShaderDraw.Set(effect, "TrailStartColor", new Color(255, 150, 50));
                    SafeShaderDraw.Set(effect, "TrailEndColor", new Color(255, 50, 150));
                    SafeShaderDraw.Set(effect, "TrailFades", 0f);
                    SafeShaderDraw.Set(effect, "TrailFadeSpeed", 1.2f);
                    SafeShaderDraw.Set(effect, "TrailPulseSpeed", 6f);
                    SafeShaderDraw.Set(effect, "TrailPulseIntensity", 0.5f);
                    SafeShaderDraw.Set(effect, "TrailGlows", 1f);
                    SafeShaderDraw.Set(effect, "TrailGlowColor", new Color(255, 100, 50));
                });
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            // Full Celestial hit package; Andromeda's Wrath also grants its rage buff.
            if (Projectile.owner >= 0 && Projectile.owner < Main.maxPlayers)
            {
                Player player = Main.player[Projectile.owner];
                if (player != null && player.active)
                    CelestialHitEffects.ApplyCelestialHitBundle(player, target, grantAndromedasRage: true);
            }

            SoundEngine.PlaySound(SoundID.Item14, Projectile.Center);

            for (int i = 0; i < 35; i++)
            {
                int dust = Dust.NewDust(
                    Projectile.position, Projectile.width, Projectile.height,
                    DustID.PurpleCrystalShard, 0f, 0f, 100, default, 2f);
                Main.dust[dust].velocity *= 3f;
                Main.dust[dust].noGravity = true;
                Main.dust[dust].color = new Color(
                    Main.rand.Next(150, 255),
                    Main.rand.Next(0, 100),
                    Main.rand.Next(150, 255));
            }
            for (int j = 0; j < 20; j++)
            {
                int spark = Dust.NewDust(
                    Projectile.position, Projectile.width, Projectile.height,
                    DustID.GoldFlame, 0f, 0f, 50, default, 2f);
                Main.dust[spark].velocity *= 2f;
                Main.dust[spark].noGravity = true;
                Main.dust[spark].color = new Color(255, 215, 0, 150);
            }
        }
    }
}
