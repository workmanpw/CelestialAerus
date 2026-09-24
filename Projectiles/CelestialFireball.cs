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
using Terraria.Localization;

namespace CelestialAerus.Projectiles
{
    public class CelestialFireball : ModProjectile
    {
        // Trail storage
        private List<Vector2> trailPositions = new List<Vector2>();
        private const int MaxTrail = 15;

        public override string Texture => "CelestialAerus/Textures/CelestialFireball";
        
        public override void SetStaticDefaults()
        {
        }

        public override LocalizedText DisplayName => Language.GetOrRegister("Celestial Fireball");

        public override void SetDefaults()
        {
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
            trailPositions.Add(Projectile.Center);
            if (trailPositions.Count > MaxTrail)
                trailPositions.RemoveAt(0);

            // --- Rotation & lighting ---
            Projectile.rotation += 0.2f;
            Lighting.AddLight(Projectile.Center,
                Main.DiscoR / 70f, Main.DiscoG / 70f, Main.DiscoB / 70f);

            // --- Homing (after a brief delay) ---
            Projectile.localAI[0]++;
            if (Projectile.localAI[0] > 10)
            {
                float maxDetect = 500f, homingSpeed = 20f;
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
                    Projectile.velocity = Vector2.Lerp(
                        Projectile.velocity,
                        Vector2.Normalize(to) * homingSpeed,
                        0.15f);
                }
            }
        }

        // --- Draw the trail ---
        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D texture = TextureAssets.Projectile[Type].Value;
            Vector2 origin = new Vector2(texture.Width / 2f, texture.Height / 2f);

            for (int i = 0; i < trailPositions.Count; i++)
            {
                float progress = (float)i / trailPositions.Count; // 0 = oldest, 1 = newest
                float alpha = progress * 0.7f;
                float scale = 0.4f + progress * 0.6f;

                Color color = new Color(Main.DiscoR, Main.DiscoG, Main.DiscoB) * alpha;
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
            return true;
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
            => Explode();

        public override void OnHitPlayer(Player target, Player.HurtInfo info)
            => Explode();

        private void Explode()
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
