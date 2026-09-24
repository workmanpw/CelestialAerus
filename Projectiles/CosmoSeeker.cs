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

namespace CelestialAerus.Projectiles
{
    public class CosmoSeeker : ModProjectile
    {
        private readonly List<Vector2> trailPositions = new List<Vector2>();
        private const int MaxTrail = 15;

        public override void SetStaticDefaults()
        {
            ProjectileID.Sets.TrailCacheLength[Type] = MaxTrail;
            ProjectileID.Sets.TrailingMode[Type] = 0;
        }

        public override LocalizedText DisplayName => Language.GetOrRegister("Cosmo Seeker");
        public override string Texture => "CelestialAerus/Textures/Cosmosis";

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
                Vector2 toTarget = target.Center - Projectile.Center;
                toTarget.Normalize();
                Projectile.velocity = (Projectile.velocity * 20f + toTarget * homingSpeed) / 21f;
            }

            Projectile.rotation += 0.2f;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D texture = TextureAssets.Projectile[Type].Value;
            Vector2 origin = texture.Size() / 2f;

            int count = trailPositions.Count;

            for (int i = 0; i < count - 1; i++)
            {
                float progress = (count > 1) ? (float)i / (count - 1) : 0f;
                float alpha = progress * 0.6f;
                float scale = 0.3f + progress * 0.7f;

                float hue = (Main.GlobalTimeWrappedHourly * 0.4f + i * 0.03f) % 1f;
                Color color = Main.hslToRgb(hue, 1f, 0.7f) * alpha;
                color.A = (byte)(alpha * 255);

                Vector2 drawPos = trailPositions[i] - Main.screenPosition;

                Main.EntitySpriteDraw(
                    texture,
                    drawPos,
                    null,
                    color,
                    Projectile.rotation,
                    origin,
                    Projectile.scale * scale,
                    SpriteEffects.None,
                    0
                );
            }

            Vector2 headPos = Projectile.Center - Main.screenPosition;
            Main.EntitySpriteDraw(
                texture,
                headPos,
                null,
                Projectile.GetAlpha(lightColor),
                Projectile.rotation,
                origin,
                Projectile.scale,
                SpriteEffects.None,
                0
            );

            return false;
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
