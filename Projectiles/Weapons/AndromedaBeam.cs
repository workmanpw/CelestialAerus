using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Audio;
using CelestialAerus.Effects;

namespace CelestialAerus.Projectiles
{
    public class AndromedaBeam : ModProjectile
    {
        private readonly TrailRecorder trail = new TrailRecorder(MaxTrail);
        private const int MaxTrail = 20;

        // Local spin accumulator; decrements so it spins opposite to CosmoBeam.
        private float spin;

        public override string Texture => "CelestialAerus/Assets/Textures/AndromedasWrath";

        public override void SetDefaults()
        {
            // Hitbox matches the drawn sprite: 180x180 texture at scale 1.
            Projectile.width = 180;
            Projectile.height = 180;
            Projectile.scale = 1f;
            Projectile.friendly = true;
            Projectile.hostile = false;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = 3;
            Projectile.timeLeft = 300;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 15;
            Projectile.aiStyle = -1;
            Projectile.tileCollide = false;
        }

        public override void AI()
        {
            trail.Record(Projectile.Center);

            // Red-orange light.
            Lighting.AddLight(Projectile.Center, 0.8f, 0.3f, 0.05f);

            // Face movement direction + steady spin (opposite direction to CosmoBeam).
            spin -= 0.08f;
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
                Vector2 toTarget = (target.Center - Projectile.Center).SafeNormalize(Vector2.Zero);
                if (toTarget != Vector2.Zero)
                    Projectile.velocity = (Projectile.velocity * 20f + toTarget * homingSpeed) / 21f;
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Color lightColorCopy = lightColor;
            Texture2D texture = TextureAssets.Projectile[Type].Value;

            // Soft halo around the blade instead of a ribbon through it.
            float haloRadius = Math.Max(texture.Width, texture.Height) * 0.72f * Projectile.scale;
            float haloPulse = 0.85f + 0.15f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 4f);

            HaloDraw.Draw(Projectile.Center, haloRadius, new Color(255, 110, 20), 0.45f * haloPulse);
            HaloDraw.Draw(Projectile.Center, haloRadius * 0.7f, new Color(255, 210, 110), 0.4f * haloPulse);

            // Shader ribbon streaming behind the blade.
            float trailStartOffset = texture.Height * 0.5f * Projectile.scale;
            // Fixed trail width, decoupled from the hitbox (2026-11-14):
            // the hitbox now matches the 180x180 sprite, but deriving the
            // ribbon width from it blew the trail up to a 180px band. 38
            // restores the pre-fix-pass ribbon. VertexStrip treats this as
            // a half-width.
            float trailHalfWidth = 38f;

            ShaderTrailDraw.Draw(
                trail.Positions,
                trail.Count,
                progress => ShaderTrailDraw.WithAlpha(
                    Color.Lerp(new Color(150, 35, 5), new Color(255, 175, 70), progress),
                    progress * progress * 0.85f),
                progress => trailHalfWidth * (float)Math.Pow(progress, 1.15f),
                "CelestialRibbon",
                effect =>
                {
                    SafeShaderDraw.Set(effect, "TrailStartColor", Color.White);
                    SafeShaderDraw.Set(effect, "TrailEndColor", Color.White);
                    SafeShaderDraw.Set(effect, "TrailPulseSpeed", 6f);
                    SafeShaderDraw.Set(effect, "TrailPulseIntensity", 0.2f);
                },
                trailStartOffset);

            return SafeShaderDraw.DrawWithShader("EonBeam",
                () =>
                {
                    Vector2 origin = texture.Size() / 2f;

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
                    SafeShaderDraw.Set(effect, "BeamWidth", 16f);
                    SafeShaderDraw.Set(effect, "BeamIntensity", 1.6f);
                    SafeShaderDraw.Set(effect, "BeamCoreColor", new Color(255, 128, 26));
                    SafeShaderDraw.Set(effect, "BeamEdgeColor", new Color(230, 51, 0));
                    SafeShaderDraw.Set(effect, "BeamPulseSpeed", 5f);
                    SafeShaderDraw.Set(effect, "BeamFlickers", 1f);
                    SafeShaderDraw.Set(effect, "BeamFlickerIntensity", 0.25f);
                    SafeShaderDraw.Set(effect, "BeamSpinSpeed", 3f);
                });
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            // Heal only: the Celestial hit bundle (buffs/debuffs) was
            // removed temporarily — Peyton has a better idea coming.
            // The 75 heal stays so the tooltip remains true.
            if (Projectile.owner >= 0 && Projectile.owner < Main.maxPlayers)
            {
                Player player = Main.player[Projectile.owner];
                if (player != null && player.active)
                {
                    int healAmount = 75;
                    if (player.statLife + healAmount > player.statLifeMax2)
                        healAmount = player.statLifeMax2 - player.statLife;
                    player.Heal(healAmount);
                    player.HealEffect(healAmount);
                }
            }

            SoundEngine.PlaySound(SoundID.Item14, Projectile.Center);

            for (int i = 0; i < 40; i++)
            {
                int dust = Dust.NewDust(
                    Projectile.position, Projectile.width, Projectile.height,
                    DustID.Torch, 0f, 0f, 100, default, 2f);
                Main.dust[dust].velocity *= 3f;
                Main.dust[dust].noGravity = true;
                Main.dust[dust].color = new Color(255, Main.rand.Next(50, 150), 0);
            }
            for (int j = 0; j < 25; j++)
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
