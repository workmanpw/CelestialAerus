using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Localization;

namespace CelestialAerus.Projectiles
{
    public class NebulaBlazeBolt : ModProjectile
    {
        // Homing settings.
        private const float HomingRange = 700f;
        private const float HomingSpeed = 20f;
        private const float HomingStrength = 0.15f;

        // Bouncing settings.
        private const int MaxBounces = 2;
        private const int BounceDelay = 8;

        private int bounceCount = 0;
        private int homingDelay = 0;
        private int lastHitNPC = -1;

        // Explosion state.
        private bool hasExploded = false;

        // Texture and display name.
        public override string Texture =>
            "CelestialAerus/Textures/NebulaBlazeSword";

        public override LocalizedText DisplayName =>
            Language.GetOrRegister("Nebula Blaze Bolt");

        // Projectile defaults.
        public override void SetDefaults()
        {
            Projectile.width = 18;
            Projectile.height = 18;

            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Melee;

            Projectile.penetrate = -1;

            // Ignore blocks.
            Projectile.tileCollide = false;

            // Ignore liquids.
            Projectile.ignoreWater = true;

            Projectile.timeLeft = 300;

            Projectile.extraUpdates = 1;

            Projectile.aiStyle = -1;

            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 10;

            Projectile.light = 0.8f;
        }

        // Projectile AI.
        public override void AI()
        {
            // Set the projectile rotation to match its velocity.
            Projectile.rotation =
                Projectile.velocity.ToRotation();

            // Add pink and purple light.
            Lighting.AddLight(
                Projectile.Center,
                1.0f,
                0.15f,
                0.8f
            );

            // Create the pink and purple trail.
            if (Main.rand.NextBool(2))
            {
                int dustType =
                    Main.rand.NextBool()
                        ? DustID.PinkTorch
                        : DustID.PurpleTorch;

                int dust = Dust.NewDust(
                    Projectile.position,
                    Projectile.width,
                    Projectile.height,
                    dustType,
                    0f,
                    0f,
                    100,
                    default,
                    1.4f
                );

                Main.dust[dust].noGravity = true;

                Main.dust[dust].velocity =
                    -Projectile.velocity * 0.08f;

                Main.dust[dust].scale =
                    Main.rand.NextFloat(0.8f, 1.5f);
            }

            // Apply the bounce delay.
            if (homingDelay > 0)
            {
                homingDelay--;
            }

            // Find a nearby target.
            NPC target = null;
            float closestDistance = HomingRange;

            if (homingDelay <= 0)
            {
                foreach (NPC npc in Main.npc)
                {
                    if (!npc.CanBeChasedBy(Projectile))
                        continue;

                    // Do not immediately hit the NPC just bounced from.
                    if (npc.whoAmI == lastHitNPC)
                        continue;

                    float distance =
                        Vector2.Distance(
                            Projectile.Center,
                            npc.Center
                        );

                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        target = npc;
                    }
                }
            }

            // Home toward the selected target.
            if (target != null)
            {
                Vector2 direction =
                    target.Center -
                    Projectile.Center;

                Projectile.velocity =
                    Vector2.Lerp(
                        Projectile.velocity,
                        direction.SafeNormalize(
                            Vector2.Zero
                        ) * HomingSpeed,
                        HomingStrength
                    );
            }
            else
            {
                // Maintain a consistent flight speed without a target.
                if (Projectile.velocity != Vector2.Zero)
                {
                    Projectile.velocity =
                        Projectile.velocity.SafeNormalize(
                            Vector2.Zero
                        ) * HomingSpeed;
                }
            }
        }

        // Handle NPC hits.
        public override void OnHitNPC(
            NPC target,
            NPC.HitInfo hit,
            int damageDone)
        {
            lastHitNPC = target.whoAmI;

            // Explode on the third hit.
            if (bounceCount >= MaxBounces)
            {
                int explosionDamage =
                    Projectile.damage * 2;

                Explode(explosionDamage);

                Projectile.Kill();

                return;
            }

            // Increase the bounce count.
            bounceCount++;

            // Reduce damage by 25 percent after each bounce.
            Projectile.damage =
                (int)(Projectile.damage * 0.75f);

            if (Projectile.damage < 1)
                Projectile.damage = 1;

            // Apply a short delay before homing again.
            homingDelay = BounceDelay;

            // Find the next target.
            NPC nextTarget =
                FindNearestTarget(target.whoAmI);

            if (nextTarget != null)
            {
                Vector2 direction =
                    nextTarget.Center -
                    Projectile.Center;

                Projectile.velocity =
                    direction.SafeNormalize(
                        Vector2.Zero
                    ) * HomingSpeed;
            }
            else
            {
                // Continue flying in the current direction without another target.
                Projectile.velocity =
                    Projectile.velocity.SafeNormalize(
                        Vector2.Zero
                    ) * HomingSpeed;
            }
        }

        // Find the nearest valid target.
        private NPC FindNearestTarget(
            int excludedNPC)
        {
            NPC closestNPC = null;

            float closestDistance =
                HomingRange;

            foreach (NPC npc in Main.npc)
            {
                if (!npc.CanBeChasedBy(Projectile))
                    continue;

                if (npc.whoAmI == excludedNPC)
                    continue;

                float distance =
                    Vector2.Distance(
                        Projectile.Center,
                        npc.Center
                    );

                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestNPC = npc;
                }
            }

            return closestNPC;
        }

        // Handle projectile death.
        public override void OnKill(
            int timeLeft)
        {
            // Prevent another explosion after a third-hit explosion.
            if (hasExploded)
                return;

            hasExploded = true;

            // Lifetime expiration causes a normal-damage explosion.
            Explode(Projectile.damage);
        }

        // Create the explosion.
        private void Explode(int damage)
        {
            // Prevent duplicate explosions.
            if (hasExploded && Projectile.timeLeft > 0)
                return;

            hasExploded = true;

            // Play the explosion sound.
            SoundEngine.PlaySound(
                SoundID.Item14,
                Projectile.Center
            );

            // Create the large pink and purple dust cloud.
            for (int i = 0; i < 120; i++)
            {
                int dustType =
                    Main.rand.NextBool()
                        ? DustID.PinkTorch
                        : DustID.PurpleTorch;

                int dust = Dust.NewDust(
                    Projectile.Center -
                        new Vector2(90f, 90f),
                    180,
                    180,
                    dustType,
                    0f,
                    0f,
                    100,
                    default,
                    Main.rand.NextFloat(1.5f, 3.5f)
                );

                Main.dust[dust].noGravity = true;

                Vector2 direction =
                    Main.dust[dust].position -
                    Projectile.Center;

                direction =
                    direction.SafeNormalize(
                        Main.rand.NextVector2Unit()
                    );

                Main.dust[dust].velocity =
                    direction *
                    Main.rand.NextFloat(2f, 8f);

                Main.dust[dust].scale =
                    Main.rand.NextFloat(1.5f, 3.5f);
            }

            // Create the bright central particles.
            for (int i = 0; i < 45; i++)
            {
                int dustType =
                    Main.rand.NextBool()
                        ? DustID.PinkTorch
                        : DustID.PurpleTorch;

                int dust = Dust.NewDust(
                    Projectile.Center -
                        new Vector2(35f, 35f),
                    70,
                    70,
                    dustType,
                    0f,
                    0f,
                    50,
                    default,
                    Main.rand.NextFloat(2f, 4f)
                );

                Main.dust[dust].noGravity = true;

                Main.dust[dust].velocity =
                    Main.rand.NextVector2Circular(
                        9f,
                        9f
                    );
            }

            // Add strong pink and purple explosion light.
            Lighting.AddLight(
                Projectile.Center,
                1.5f,
                0.2f,
                1.2f
            );

            // Create the damaging explosion projectile.
            if (damage > 0)
            {
                int explosion =
                    Projectile.NewProjectile(
                        Projectile.GetSource_FromThis(),
                        Projectile.Center,
                        Vector2.Zero,
                        ModContent.ProjectileType<NebulaBlazeExplosion>(),
                        damage,
                        0f,
                        Projectile.owner
                    );

                if (explosion >= 0 &&
                    explosion < Main.maxProjectiles)
                {
                    Main.projectile[explosion].Center =
                        Projectile.Center;

                    Main.projectile[explosion].netUpdate = true;
                }
            }
        }

        // Draw the bolt.
        public override bool PreDraw(
            ref Color lightColor)
        {
            Texture2D texture =
                TextureAssets.Projectile[Type].Value;

            Vector2 origin =
                texture.Size() / 2f;

            Color color =
                new Color(
                    255,
                    80,
                    255,
                    255
                );

            Main.EntitySpriteDraw(
                texture,
                Projectile.Center -
                    Main.screenPosition,
                null,
                color,
                Projectile.rotation,
                origin,
                Projectile.scale * 0.55f,
                SpriteEffects.None,
                0
            );

            return false;
        }

        // Set the projectile color.
        public override Color? GetAlpha(
            Color lightColor)
        {
            return new Color(
                255,
                80,
                255,
                255
            );
        }
    }

    // Create the damaging explosion area.
    public class NebulaBlazeExplosion : ModProjectile
    {
        public override string Texture =>
            "CelestialAerus/Textures/NebulaBlazeSword";

        // Explosion projectile defaults.
        public override void SetDefaults()
        {
            Projectile.width = 180;
            Projectile.height = 180;

            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Melee;

            Projectile.penetrate = -1;

            // Ignore blocks/tiles.
            Projectile.tileCollide = false;

            // Ignore liquids.
            Projectile.ignoreWater = true;

            // Give the explosion several frames to register hits.
            Projectile.timeLeft = 4;

            Projectile.extraUpdates = 0;

            Projectile.aiStyle = -1;

            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 10;

            // Keep the damaging projectile invisible.
            Projectile.alpha = 255;
        }

        // Keep the explosion stationary and illuminated.
        public override void AI()
        {
            Projectile.velocity = Vector2.Zero;

            Projectile.Center = Projectile.Center;

            Lighting.AddLight(
                Projectile.Center,
                1.5f,
                0.2f,
                1.2f
            );
        }

        // Do not draw the invisible explosion projectile.
        public override bool PreDraw(
            ref Color lightColor)
        {
            return false;
        }
    }
}
