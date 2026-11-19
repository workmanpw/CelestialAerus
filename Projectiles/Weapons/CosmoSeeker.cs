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
    public class CosmoSeeker : ModProjectile
    {
        private readonly TrailRecorder trail = new TrailRecorder(MaxTrail);
        private const int MaxTrail = 15;

        public override string Texture => "CelestialAerus/Assets/Textures/Cosmosis";

        public override void SetDefaults()
        {
            // Hitbox matches the drawn sprite: 76x76 texture at 0.5 scale.
            Projectile.width = 38;
            Projectile.height = 38;
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
            trail.Record(Projectile.Center);

            // Cosmic purple/blue light.
            Lighting.AddLight(Projectile.Center, 0.4f, 0.2f, 0.8f);

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

            Projectile.rotation += 0.2f;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Color lightColorCopy = lightColor;
            Texture2D texture = TextureAssets.Projectile[Type].Value;

            // Start the ribbon at the projectile's rear edge and give it the
            // projectile's full width at the head, tapering to a point.
            float trailStartOffset = texture.Height * 0.5f * Projectile.scale;
            // Fixed trail width, decoupled from the hitbox (2026-11-14):
            // the hitbox now matches the drawn sprite, but the ribbon keeps
            // its tuned pre-fix-pass width. VertexStrip treats this as a
            // half-width.
            float trailHalfWidth = 19f;

            ShaderTrailDraw.Draw(
                trail.Positions,
                trail.Count,
                progress =>
                {
                    float hue = (Main.GlobalTimeWrappedHourly * 0.4f + progress * 0.2f) % 1f;
                    return ShaderTrailDraw.WithAlpha(Main.hslToRgb(hue, 1f, 0.7f), progress * progress * 0.8f);
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

            return SafeShaderDraw.DrawWithShader("GalaxySwirl",
                () =>
                {
                    Main.EntitySpriteDraw(texture, Projectile.Center - Main.screenPosition, null,
                        Projectile.GetAlpha(lightColorCopy), Projectile.rotation, texture.Size() / 2f,
                        Projectile.scale, SpriteEffects.None, 0);
                },
                (effect) =>
                {
                    SafeShaderDraw.Set(effect, "SwirlIntensity", 0.9f);
                    SafeShaderDraw.Set(effect, "SwirlColor1", new Color(179, 77, 255));
                    SafeShaderDraw.Set(effect, "SwirlColor2", new Color(77, 153, 255));
                    SafeShaderDraw.Set(effect, "SwirlColor3", new Color(255, 102, 204));
                    SafeShaderDraw.Set(effect, "SwirlSpeed", 2f);
                    SafeShaderDraw.Set(effect, "SwirlArms", 3f);
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

            for (int i = 0; i < 35; i++)
            {
                int dust = Dust.NewDust(
                    Projectile.position, Projectile.width, Projectile.height,
                    DustID.RainbowTorch, 0f, 0f, 100, default, 2f);
                Main.dust[dust].velocity *= 3f;
                Main.dust[dust].noGravity = true;
                Main.dust[dust].color = new Color(Main.DiscoR, Main.DiscoG, Main.DiscoB);
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
