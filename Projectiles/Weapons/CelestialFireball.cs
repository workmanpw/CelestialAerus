using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;
using Terraria.ID;
using Terraria.DataStructures;
using Terraria.GameContent;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.Audio;
using CelestialAerus.Effects;

namespace CelestialAerus.Projectiles
{
    public class CelestialFireball : ModProjectile
    {
        // Trail storage
        private readonly TrailRecorder trail = new TrailRecorder(MaxTrail);
        private const int MaxTrail = 15;

        public override string Texture => "CelestialAerus/Assets/Textures/CelestialFireball";
        
        public override void SetStaticDefaults()
        {
        }

        public override void SetDefaults()
        {
            // Hitbox already matches the drawn sprite: 36x36 texture at scale 1.
            Projectile.width = 36;
            Projectile.height = 36;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = 2;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.aiStyle = -1;   // custom AI
            Projectile.extraUpdates = 1;
            Projectile.timeLeft = 360;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 120;
        }

        public override void AI()
        {
            // --- Trail recording ---
            trail.Record(Projectile.Center);

            // --- Rotation & lighting ---
            Projectile.rotation += 0.2f;
            Lighting.AddLight(Projectile.Center,
                Main.DiscoR / 70f, Main.DiscoG / 70f, Main.DiscoB / 70f);

            // --- Homing (after a brief delay) ---
            // NOTE: the fireball deliberately ignores ai[1]. The sword
            // passes a per-fireball stagger value there, but the original
            // code never read it — a fix-pass change once implemented it
            // and it made the volley crawl. Do not re-add stagger handling.
            // ORIGINAL AI restored 2026-11-15 (Peyton: the lock-on /
            // prediction / turn-rate rework "feels unnatural"): loose
            // Lerp steering, full scan every AI call, no lock-on, no
            // prediction, no fizzle — if there's no target it just flies
            // straight for its full 6-second life.
            Projectile.localAI[0]++;
            if (Projectile.localAI[0] > 10)
            {
                // Homing range buffed 2026-11-15 (Peyton): 500f -> 750f (+50%).
                float maxDetect = 750f, homingSpeed = 20f;
                NPC target = null;
                float minDist = maxDetect;
                foreach (NPC npc in Main.npc)
                    if (npc.CanBeChasedBy(Projectile))
                    {
                        float dist = Vector2.Distance(Projectile.Center, npc.Center);
                        if (dist < minDist) { minDist = dist; target = npc; }
                    }
                if (target != null)
                {
                    Vector2 to = target.Center - Projectile.Center;
                    // SafeNormalize instead of the original's
                    // Vector2.Normalize: identical except when the target
                    // is exactly at the fireball's center, where the
                    // original would produce NaN velocity.
                    Projectile.velocity = Vector2.Lerp(
                        Projectile.velocity,
                        to.SafeNormalize(Vector2.Zero) * homingSpeed,
                        0.15f);
                }
            }
        }

        // --- Draw the trail ---
        public override bool PreDraw(ref Color lightColor)
        {
            Color lightColorCopy = lightColor;
            Texture2D texture = TextureAssets.Projectile[Type].Value;

            // Start the ribbon at the projectile's rear edge and give it the
            // projectile's full width at the head, tapering to a point.
            float trailStartOffset = texture.Height * 0.5f * Projectile.scale;
            float trailHalfWidth = Math.Max(Projectile.width, Projectile.height) * 0.5f * Projectile.scale; // VertexStrip treats this as a half-width

            // Rainbow recolor 2026-11-15 (Peyton): the trail uses the same
            // disco rainbow as the Thread of the Empyrean's trail. Same
            // mechanics, same length and shape — only the color changed.
            ShaderTrailDraw.Draw(
                trail.Positions,
                trail.Count,
                progress => ShaderTrailDraw.WithAlpha(
                    new Color(Main.DiscoR, Main.DiscoG, Main.DiscoB),
                    progress * progress * 0.78f),
                progress => trailHalfWidth * (float)Math.Pow(progress, 1.15f),
                "CelestialRibbon",
                effect =>
                {
                    SafeShaderDraw.Set(effect, "TrailStartColor", Color.White);
                    SafeShaderDraw.Set(effect, "TrailEndColor", Color.White);
                    SafeShaderDraw.Set(effect, "TrailPulseSpeed", 7f);
                    SafeShaderDraw.Set(effect, "TrailPulseIntensity", 0.24f);
                },
                trailStartOffset);

            return SafeShaderDraw.DrawWithShader("CelestialFireTrail",
                () =>
                {
                    Main.EntitySpriteDraw(texture, Projectile.Center - Main.screenPosition, null,
                        Projectile.GetAlpha(lightColorCopy), Projectile.rotation, texture.Size() / 2f,
                        Projectile.scale, SpriteEffects.None, 0);
                },
                // Rainbow recolor 2026-11-15 (Peyton): the shader colors go
                // white so the disco tint from GetAlpha is the fireball's
                // only color — the same technique as the yoyo trail (white
                // shader colors + disco vertex color). The fire's shape,
                // flicker, and glow are untouched; only the color changed.
                (effect) =>
                {
                    SafeShaderDraw.Set(effect, "TrailWidth", 12f);
                    SafeShaderDraw.Set(effect, "TrailStartColor", Color.White);
                    SafeShaderDraw.Set(effect, "TrailEndColor", Color.White);
                    SafeShaderDraw.Set(effect, "TrailFades", 0f);
                    SafeShaderDraw.Set(effect, "TrailFadeSpeed", 1f);
                    SafeShaderDraw.Set(effect, "TrailPulseSpeed", 7f);
                    SafeShaderDraw.Set(effect, "TrailPulseIntensity", 0.4f);
                    SafeShaderDraw.Set(effect, "TrailGlows", 1f);
                    SafeShaderDraw.Set(effect, "TrailGlowColor", Color.White);
                    SafeShaderDraw.Set(effect, "FireIntensity", 1.2f);
                    SafeShaderDraw.Set(effect, "FireColor", Color.White);
                    SafeShaderDraw.Set(effect, "FireDecay", 0.7f);
                    SafeShaderDraw.Set(effect, "FireEmbers", 1f);
                });
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
            => SpawnExplosionEffects();

        public override void OnHitPlayer(Player target, Player.HurtInfo info)
            => SpawnExplosionEffects();

        private void SpawnExplosionEffects()
        {
            SoundEngine.PlaySound(SoundID.Item14, Projectile.Center);

            // --- Explosion dust burst (rainbow/glowing) ---
            for (int i = 0; i < 40; i++)
            {
                int dust = Dust.NewDust(
                    Projectile.position,
                    Projectile.width,
                    Projectile.height,
                    DustID.FireworkFountain_Blue,
                    0f, 0f,
                    100,
                    default,
                    2.5f
                );
                Main.dust[dust].velocity *= 4f;
                Main.dust[dust].noGravity = true;
                Color randomColor = new Color(
                    Main.rand.Next(100, 256),
                    Main.rand.Next(100, 256),
                    Main.rand.Next(100, 256)
                );
                Main.dust[dust].color = randomColor;
                Main.dust[dust].scale = 1.5f + Main.rand.NextFloat(1.5f);
            }

            // Golden sparks
            for (int j = 0; j < 25; j++)
            {
                int spark = Dust.NewDust(
                    Projectile.position,
                    Projectile.width,
                    Projectile.height,
                    DustID.GoldFlame,
                    0f, 0f,
                    50,
                    default,
                    2f
                );
                Main.dust[spark].velocity *= 3f;
                Main.dust[spark].noGravity = true;
                Main.dust[spark].color = new Color(255, 215, 0, 150);
            }
        }

        public override Color? GetAlpha(Color lightColor)
            => new Color(Main.DiscoR, Main.DiscoG, Main.DiscoB);
    }
}
