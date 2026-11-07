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
    public class TempestBeam : ModProjectile
    {
        private readonly List<Vector2> trailPositions = new List<Vector2>();
        private const int MaxTrail = 20;

        // Local spin accumulator so the beam keeps spinning during flight.
        private float spin;

        public override void SetStaticDefaults()
        {
            ProjectileID.Sets.TrailCacheLength[Type] = MaxTrail;
            ProjectileID.Sets.TrailingMode[Type] = 0;
        }

        public override LocalizedText DisplayName => Language.GetOrRegister("Tempest Beam");
        public override string Texture => "CelestialAerus/Textures/CelestialTempest";

        public override void SetDefaults()
        {
            Projectile.width = 20;
            Projectile.height = 20;
            Projectile.scale = 1f;
            Projectile.friendly = true;
            Projectile.hostile = false;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = 3;
            Projectile.timeLeft = 300;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 12;
            Projectile.aiStyle = -1;
            Projectile.tileCollide = false;
        }

        public override void AI()
        {
            trailPositions.Add(Projectile.Center);
            if (trailPositions.Count > MaxTrail)
                trailPositions.RemoveAt(0);

            Lighting.AddLight(Projectile.Center, 0.2f, 0.5f, 0.9f);

            // Steady spin so the beam visibly rotates through its flight.
            spin += 0.10f;
            if (Projectile.velocity.LengthSquared() > 0.25f)
                Projectile.rotation = Projectile.velocity.ToRotation() + spin;

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
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Color lightColorCopy = lightColor;
            Texture2D texture = TextureAssets.Projectile[Type].Value;

            // Soft halo around the blade instead of a ribbon through it.
            float haloRadius = Math.Max(texture.Width, texture.Height) * 0.72f * Projectile.scale;
            float haloPulse = 0.85f + 0.15f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 5f);

            HaloDraw.Draw(Projectile.Center, haloRadius, new Color(30, 200, 150), 0.45f * haloPulse);
            HaloDraw.Draw(Projectile.Center, haloRadius * 0.7f, new Color(160, 255, 230), 0.4f * haloPulse);

            // Shader ribbon streaming behind the blade.
            float trailStartOffset = texture.Height * 0.5f * Projectile.scale;
            float trailHalfWidth = Math.Max(Projectile.width, Projectile.height) * 0.5f * Projectile.scale; // VertexStrip treats this as a half-width

            ShaderTrailDraw.Draw(
                trailPositions,
                trailPositions.Count,
                progress => ShaderTrailDraw.WithAlpha(
                    Color.Lerp(new Color(30, 190, 140), new Color(190, 255, 235), progress),
                    progress * progress * 0.85f),
                progress => trailHalfWidth * (float)Math.Pow(progress, 1.15f),
                "CelestialRibbon",
                effect =>
                {
                    SafeShaderDraw.Set(effect, "TrailStartColor", Color.White);
                    SafeShaderDraw.Set(effect, "TrailEndColor", Color.White);
                    SafeShaderDraw.Set(effect, "TrailPulseSpeed", 5f);
                    SafeShaderDraw.Set(effect, "TrailPulseIntensity", 0.22f);
                },
                trailStartOffset);

            return SafeShaderDraw.DrawWithShader("EonBeam",
                () =>
                {

                Vector2 origin = texture.Size() / 2f;

                // Head at full opacity.
                Vector2 headPos = Projectile.Center - Main.screenPosition;
                Main.EntitySpriteDraw(
                    texture,
                    headPos,
                    null,
                    Projectile.GetAlpha(lightColorCopy),
                    Projectile.rotation,
                    origin,
                    Projectile.scale,
                    SpriteEffects.None,
                    0
                );
                },
                (effect) =>
                {
                    SafeShaderDraw.Set(effect, "BeamWidth", 15f);
                    SafeShaderDraw.Set(effect, "BeamIntensity", 1.4f);
                    SafeShaderDraw.Set(effect, "BeamCoreColor", new Color(128, 255, 204));
                    SafeShaderDraw.Set(effect, "BeamEdgeColor", new Color(26, 179, 128));
                    SafeShaderDraw.Set(effect, "BeamPulseSpeed", 7f);
                    SafeShaderDraw.Set(effect, "BeamFlickers", 1f);
                    SafeShaderDraw.Set(effect, "BeamFlickerIntensity", 0.3f);
                    SafeShaderDraw.Set(effect, "BeamSpinSpeed", 4f);
                });
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            // Signature debuff.
            target.AddBuff(ModContent.BuffType<CelestialFlames>(), 300);

            if (Projectile.owner >= 0 && Projectile.owner < Main.maxPlayers)
            {
                Player player = Main.player[Projectile.owner];
                if (player != null && player.active)
                {
                    player.Heal(75);
                    player.HealEffect(75);
                    CelestialHitEffects.ApplyPlayerBiomeBuffs(player);
                    CelestialHitEffects.ApplyEnemyDebuffs(target);
                }
            }

            SoundEngine.PlaySound(SoundID.Item14, Projectile.Center);
        }
    }
}
