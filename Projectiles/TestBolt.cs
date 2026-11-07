using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.GameContent;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Localization;
using CelestialAerus.Effects;

namespace CelestialAerus.Projectiles
{
    public class TestBolt : ModProjectile
    {
        // Keep the vanilla Rainbow Rod bolt sprite.
        public override string Texture => "Terraria/Images/Projectile_" + ProjectileID.RainbowRodBullet;

        private readonly List<Vector2> trailPositions = new List<Vector2>();
        private const int MaxTrail = 14;

        public override LocalizedText DisplayName => Language.GetOrRegister("Test Bolt");

        public override void SetDefaults()
        {
            // Start from the vanilla Rainbow Rod bolt, then modify it.
            Projectile.CloneDefaults(ProjectileID.RainbowRodBullet);
            Projectile.aiStyle = -1; // custom homing AI below
            Projectile.DamageType = DamageClass.Melee;
            Projectile.tileCollide = false; // travel through tiles
            Projectile.timeLeft = 300;
        }

        public override void AI()
        {
            // Glow, like the vanilla bolt.
            Lighting.AddLight(Projectile.Center, 0.6f, 0.6f, 0.6f);

            // Rainbow sparkle trail.
            if (Main.rand.NextBool(2))
            {
                int dust = Dust.NewDust(
                    Projectile.position, Projectile.width, Projectile.height,
                    DustID.RainbowTorch, 0f, 0f, 100,
                    new Color(Main.DiscoR, Main.DiscoG, Main.DiscoB), 1.2f);
                Main.dust[dust].noGravity = true;
                Main.dust[dust].velocity *= 0.3f;
            }

            // Homing, Cosmosis-style: steer toward the nearest chaseable NPC,
            // ignoring tiles so it homes through walls.
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

            Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.PiOver2;

            // Track the flight path for the continuous ribbon trail.
            trailPositions.Add(Projectile.Center);
            if (trailPositions.Count > MaxTrail)
                trailPositions.RemoveAt(0);
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Color lightColorCopy = lightColor;
            Texture2D texture = TextureAssets.Projectile[Type].Value;

            // One continuous rainbow ribbon behind the bolt, full width at
            // the head and tapering to a thin tail.
            float trailStartOffset = texture.Height * 0.5f * Projectile.scale;
            float trailHalfWidth = Math.Max(Projectile.width, Projectile.height) * 0.5f * Projectile.scale; // VertexStrip treats this as a half-width

            ShaderTrailDraw.Draw(
                trailPositions,
                trailPositions.Count,
                progress =>
                {
                    float hue = (Main.GlobalTimeWrappedHourly * 0.6f + progress * 0.25f) % 1f;
                    return ShaderTrailDraw.WithAlpha(Main.hslToRgb(hue, 1f, 0.72f), progress * progress * 0.8f);
                },
                progress => trailHalfWidth * (float)Math.Pow(progress, 1.15f),
                "CelestialRibbon",
                effect =>
                {
                    SafeShaderDraw.Set(effect, "TrailStartColor", Color.White);
                    SafeShaderDraw.Set(effect, "TrailEndColor", Color.White);
                    SafeShaderDraw.Set(effect, "TrailPulseSpeed", 6f);
                    SafeShaderDraw.Set(effect, "TrailPulseIntensity", 0.22f);
                },
                trailStartOffset);

            return SafeShaderDraw.DrawWithShader("EonBeam",
                () =>
                {
                    Main.EntitySpriteDraw(
                        texture,
                        Projectile.Center - Main.screenPosition,
                        null,
                        Projectile.GetAlpha(lightColorCopy),
                        Projectile.rotation,
                        texture.Size() / 2f,
                        Projectile.scale,
                        SpriteEffects.None,
                        0);
                },
                (effect) =>
                {
                    SafeShaderDraw.Set(effect, "BeamWidth", 10f);
                    SafeShaderDraw.Set(effect, "BeamIntensity", 1.2f);
                    SafeShaderDraw.Set(effect, "BeamCoreColor", new Color(255, 255, 255));
                    SafeShaderDraw.Set(effect, "BeamEdgeColor", new Color(128, 128, 255));
                    SafeShaderDraw.Set(effect, "BeamPulseSpeed", 3f);
                    SafeShaderDraw.Set(effect, "BeamFlickers", 0f);
                    SafeShaderDraw.Set(effect, "BeamSpinSpeed", 2f);
                });
        }
    }
}
