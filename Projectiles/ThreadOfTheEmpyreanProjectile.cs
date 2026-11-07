using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.GameContent;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using CelestialAerus.Projectiles;
using CelestialAerus.Effects;

namespace CelestialAerus.Projectiles.ThreadOfTheEmpyrean
{
    public class ThreadOfTheEmpyreanProjectile : ModProjectile
    {
        private int fireTimer;
        private List<Vector2> trailPositions = new List<Vector2>();
        private int trailTimer = 0;

        public override string Texture => "CelestialAerus/Textures/ThreadOfTheEmpyrean";

        public override void SetStaticDefaults()
        {
        }

        public override LocalizedText DisplayName => Language.GetOrRegister("Empyrean Yo-Yo");

        public override void SetDefaults()
        {
            Projectile.width = 28;
            Projectile.height = 28;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.aiStyle = ProjAIStyleID.Yoyo;
            Projectile.penetrate = -1;
            Projectile.timeLeft = 999999;
            Projectile.ignoreWater = true;
            Projectile.tileCollide = false;
            Projectile.usesLocalNPCImmunity = true;
            // Buff: was 60 (only 1 hit per second per enemy). 20 lets the
            // yo-yo shred instead of tickling.
            Projectile.localNPCHitCooldown = 20;

            ProjectileID.Sets.YoyosLifeTimeMultiplier[Projectile.type] = 999f;
            ProjectileID.Sets.YoyosMaximumRange[Projectile.type] = 999f;
            ProjectileID.Sets.YoyosTopSpeed[Projectile.type] = 80f;
        }

        public override void AI()
        {
            fireTimer++;
            // Buff: fires every 10 ticks (was 15), and the fireballs now deal
            // full yo-yo damage (was half) and re-hit faster (cooldown 20).
            if (fireTimer >= 10)
            {
                fireTimer = 0;
                Vector2 spawn = Projectile.Center;
                Vector2 dir = Vector2.Normalize(Main.MouseWorld - spawn) * 12f;
                int p = Projectile.NewProjectile(
                    Projectile.GetSource_FromThis(),
                    spawn, dir,
                    ModContent.ProjectileType<CelestialFireball>(),
                    Projectile.damage, 2f, Projectile.owner
                );
                Main.projectile[p].tileCollide = false;
                Main.projectile[p].timeLeft = 300;
                Main.projectile[p].usesLocalNPCImmunity = true;
                Main.projectile[p].localNPCHitCooldown = 20;
            }

            trailTimer++;
            if (trailTimer >= 2)
            {
                trailPositions.Add(Projectile.Center);
                if (trailPositions.Count > 15)
                    trailPositions.RemoveAt(0);
                trailTimer = 0;
            }

            float intensity = 0.8f + (float)Math.Sin(Main.time * 0.3f) * 0.4f;
            Lighting.AddLight(Projectile.Center,
                Main.DiscoR / 255f * intensity,
                Main.DiscoG / 255f * intensity,
                Main.DiscoB / 255f * intensity);
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            Player player = Main.player[Projectile.owner];
            player.Heal(175);
        }

        public override Color? GetAlpha(Color lightColor)
            => new Color(Main.DiscoR, Main.DiscoG, Main.DiscoB);

        public override bool PreDraw(ref Color lightColor)
        {
            Color lightColorCopy = lightColor;
            Texture2D texture = TextureAssets.Projectile[Type].Value;

            // Start the ribbon at the yo-yo's rear edge and give it the
            // projectile's full width at the head, tapering to a point.
            float trailStartOffset = texture.Height * 0.5f * Projectile.scale;
            float trailHalfWidth = Math.Max(Projectile.width, Projectile.height) * 0.5f * Projectile.scale; // VertexStrip treats this as a half-width

            ShaderTrailDraw.Draw(
                trailPositions,
                trailPositions.Count,
                progress => ShaderTrailDraw.WithAlpha(
                    new Color(Main.DiscoR, Main.DiscoG, Main.DiscoB), progress * progress * 0.8f),
                progress => trailHalfWidth * (float)Math.Pow(progress, 1.15f),
                "CelestialRibbon",
                effect =>
                {
                    SafeShaderDraw.Set(effect, "TrailStartColor", Color.White);
                    SafeShaderDraw.Set(effect, "TrailEndColor", Color.White);
                    SafeShaderDraw.Set(effect, "TrailPulseSpeed", 4f);
                    SafeShaderDraw.Set(effect, "TrailPulseIntensity", 0.22f);
                },
                trailStartOffset);

            SafeShaderDraw.DrawWithShader("CelestialTrail",
                () =>
                {
                    Vector2 origin = texture.Size() / 2f;

                    // Draw yo-yo head
                    Main.EntitySpriteDraw(
                        texture,
                        Projectile.Center - Main.screenPosition,
                        null,
                        Projectile.GetAlpha(lightColorCopy),
                        Projectile.rotation,
                        origin,
                        Projectile.scale,
                        SpriteEffects.None,
                        0);
                },
                (effect) =>
                {
                    SafeShaderDraw.Set(effect, "TrailWidth", 8f);
                    SafeShaderDraw.Set(effect, "TrailStartColor", new Color(204, 102, 255));
                    SafeShaderDraw.Set(effect, "TrailEndColor", new Color(102, 26, 204));
                    SafeShaderDraw.Set(effect, "TrailFades", 0f);
                    SafeShaderDraw.Set(effect, "TrailFadeSpeed", 1f);
                    SafeShaderDraw.Set(effect, "TrailPulseSpeed", 4f);
                    SafeShaderDraw.Set(effect, "TrailPulseIntensity", 0.3f);
                    SafeShaderDraw.Set(effect, "TrailGlows", 1f);
                    SafeShaderDraw.Set(effect, "TrailGlowColor", new Color(179, 77, 255));
                });
            return false;
        }
    }
}
