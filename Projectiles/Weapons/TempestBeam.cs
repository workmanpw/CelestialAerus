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
    public class TempestBeam : ModProjectile
    {
        private readonly TrailRecorder trail = new TrailRecorder(MaxTrail);
        private const int MaxTrail = 20;

        // Local spin accumulator so the beam keeps spinning during flight.
        private float spin;

        public override string Texture => "CelestialAerus/Assets/Textures/CelestialTempest";

        public override void SetDefaults()
        {
            // Hitbox matches the drawn sprite: 102x102 texture at scale 1.
            Projectile.width = 102;
            Projectile.height = 102;
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
            trail.Record(Projectile.Center);

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
            float haloPulse = 0.85f + 0.15f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 5f);

            HaloDraw.Draw(Projectile.Center, haloRadius, new Color(30, 200, 150), 0.45f * haloPulse);
            HaloDraw.Draw(Projectile.Center, haloRadius * 0.7f, new Color(160, 255, 230), 0.4f * haloPulse);

            // Shader ribbon streaming behind the blade.
            float trailStartOffset = texture.Height * 0.5f * Projectile.scale;
            // Fixed trail width, decoupled from the hitbox (2026-11-14):
            // the hitbox now matches the 102x102 sprite, but deriving the
            // ribbon width from it blew the trail up to a 102px band. 10
            // restores the pre-fix-pass ribbon. VertexStrip treats this as
            // a half-width.
            float trailHalfWidth = 10f;

            ShaderTrailDraw.Draw(
                trail.Positions,
                trail.Count,
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

            if (Main.netMode == NetmodeID.Server)
                return;

            for (int i = 0; i < 40; i++)
            {
                int dust = Dust.NewDust(
                    Projectile.position, Projectile.width, Projectile.height,
                    DustID.RainbowTorch, 0f, 0f, 100, default, 2f);
                Main.dust[dust].velocity *= 3f;
                Main.dust[dust].noGravity = true;
                Main.dust[dust].color = new Color(
                    Main.rand.Next(80, 150),
                    Main.rand.Next(200, 256),
                    Main.rand.Next(180, 256));
            }

            for (int i = 0; i < 30; i++)
            {
                int spark = Dust.NewDust(
                    Projectile.position, Projectile.width, Projectile.height,
                    DustID.GoldFlame, 0f, 0f, 50, default, 2f);
                Main.dust[spark].velocity *= 2f;
                Main.dust[spark].noGravity = true;
                Main.dust[spark].color = new Color(200, 255, 230, 150);
            }
        }
    }
}
