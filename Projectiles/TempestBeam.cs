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
            spin += 0.03f;
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
            Texture2D texture = TextureAssets.Projectile[Type].Value;
            Vector2 origin = texture.Size() / 2f;

            int count = trailPositions.Count;

            // Trail only — skip the newest entry (that is the head).
            for (int i = 0; i < count - 1; i++)
            {
                float progress = (count > 1) ? (float)i / (count - 1) : 0f;
                float alpha = progress * 0.8f;
                float scale = 0.3f + progress * 0.7f;

                Color color = new Color(100, 200, 255) * alpha;
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

            // Head at full opacity.
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
        }
    }
}
