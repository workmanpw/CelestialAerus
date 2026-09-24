using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Localization;

namespace CelestialAerus.Projectiles
{
    public class TestBolt : ModProjectile
    {
        // Keep the vanilla Rainbow Rod bolt sprite.
        public override string Texture => "Terraria/Images/Projectile_" + ProjectileID.RainbowRodBullet;

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
        }
    }
}
