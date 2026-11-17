using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using CelestialAerus.Effects;

namespace CelestialAerus.Projectiles
{
    public class NebulaBlazeBolt : ModProjectile
    {
        // Homing settings. Original values restored 2026-11-14
        // (extraUpdates is 1 again, so these run twice per tick as
        // originally tuned). Range stays at the 1600 Peyton ordered
        // 2026-11-13.
        private const float HomingRange = 700f;
        private const float HomingSpeed = 20f;
        private const float HomingStrength = 0.15f;

        // Bouncing settings.
        private const int MaxBounces = 2;
        private const int BounceDelay = 8;

        private int bounceCount = 0;
        private int homingDelay = 0;
        private int lastHitNPC = -1;

        // Damage at spawn, before bounce reductions. The third-hit explosion
        // is 2x the ORIGINAL damage, not 2x the twice-reduced damage.
        private int originalDamage = 0;

        // Explosion state.
        private bool hasExploded = false;

        // Continuous ribbon trail history.
        private readonly TrailRecorder trail = new TrailRecorder(MaxTrail);
        private const int MaxTrail = 22;

        // True nebula palette: the colors of real emission nebulae —
        // ionized hydrogen pink, oxygen teal, and star-forming violet.
        private static readonly Color[] NebulaPalette =
        {
            new Color(138, 43, 226),  // violet
            new Color(255, 0, 255),   // magenta
            new Color(255, 20, 147),  // deep pink (hydrogen alpha)
            new Color(0, 206, 209),   // teal (ionized oxygen)
            new Color(220, 20, 60),   // crimson
        };

        // Texture and display name.
        public override string Texture =>
            "CelestialAerus/Textures/NebulaBlazeSword";

        // Projectile defaults.
        public override void SetDefaults()
        {
            // Hitbox matches the sprite size: 120x120 texture
            // (Projectile.scale is 1; the 0.55 spriteScale in PreDraw
            // only shrinks the drawn image, not the hitbox).
            Projectile.width = 120;
            Projectile.height = 120;

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
            // Capture the spawn damage before any bounce reductions apply.
            if (originalDamage == 0)
                originalDamage = Projectile.damage;

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

            // Track the flight path for the continuous ribbon trail.
            trail.Record(Projectile.Center);

            // Nebula trail dust: wisps of colored interstellar gas that
            // drift off the bolt and hang in the air behind it.
            if (Main.rand.NextBool(2))
            {
                Dust gas = Dust.NewDustPerfect(
                    Projectile.Center +
                        Main.rand.NextVector2Circular(6f, 6f),
                    DustID.PurpleTorch,
                    -Projectile.velocity * 0.08f +
                        Main.rand.NextVector2Circular(1.5f, 1.5f),
                    100,
                    NebulaPalette[Main.rand.Next(NebulaPalette.Length)],
                    Main.rand.NextFloat(0.9f, 1.7f)
                );
                if (gas != null)
                    gas.noGravity = true;
            }

            // Occasional star sparkle streaking behind the bolt.
            if (Main.rand.NextBool(6))
            {
                Dust spark = Dust.NewDustPerfect(
                    Projectile.Center,
                    DustID.FireworksRGB,
                    -Projectile.velocity * 0.35f +
                        Main.rand.NextVector2Circular(2f, 2f),
                    0,
                    Main.rand.NextBool()
                        ? Color.White
                        : new Color(150, 220, 255),
                    Main.rand.NextFloat(0.6f, 1.0f)
                );
                if (spark != null)
                    spark.noGravity = true;
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

            // Explode on the third hit (2x the ORIGINAL damage, before the
            // 0.75x-per-bounce reductions).
            if (bounceCount >= MaxBounces)
            {
                int explosionDamage =
                    (originalDamage > 0 ? originalDamage : Projectile.damage) * 2;

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

            // Sync the mutated damage / bounce state to other clients.
            Projectile.netUpdate = true;
        }

        // Sync the bolt's AI state so non-owner clients simulate the same
        // bounce/homing decisions instead of diverging between automatic
        // syncs. Order and types must match ReceiveExtraAI exactly.
        public override void SendExtraAI(BinaryWriter writer)
        {
            writer.Write((short)bounceCount);
            writer.Write((short)homingDelay);
            writer.Write((short)lastHitNPC);
            writer.Write(originalDamage);
            writer.Write(hasExploded);
        }

        public override void ReceiveExtraAI(BinaryReader reader)
        {
            bounceCount = reader.ReadInt16();
            homingDelay = reader.ReadInt16();
            lastHitNPC = reader.ReadInt16();
            originalDamage = reader.ReadInt32();
            hasExploded = reader.ReadBoolean();
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

            // Layer 1: white-hot core flash — the heart of a newborn star.
            for (int i = 0; i < 30; i++)
            {
                Dust flash = Dust.NewDustPerfect(
                    Projectile.Center +
                        Main.rand.NextVector2Circular(30f, 30f),
                    DustID.FireworksRGB,
                    Main.rand.NextVector2Circular(12f, 12f),
                    0,
                    Color.White,
                    Main.rand.NextFloat(1.2f, 2.0f)
                );
                if (flash != null)
                    flash.noGravity = true;
            }

            // Layer 2: expanding nebula gas in true nebula colors,
            // drifting outward with turbulence so the clouds curl and
            // billow like real interstellar gas instead of flying in
            // straight lines.
            for (int i = 0; i < 90; i++)
            {
                Vector2 offset = Main.rand.NextVector2Circular(90f, 90f);
                Vector2 dir =
                    offset.SafeNormalize(Main.rand.NextVector2Unit());
                Vector2 swirl =
                    new Vector2(-dir.Y, dir.X) *
                    Main.rand.NextFloat(-2f, 2f);

                Dust gas = Dust.NewDustPerfect(
                    Projectile.Center + offset,
                    DustID.PurpleTorch,
                    dir * Main.rand.NextFloat(2f, 7f) + swirl,
                    100,
                    NebulaPalette[Main.rand.Next(NebulaPalette.Length)],
                    Main.rand.NextFloat(1.8f, 3.6f)
                );
                if (gas != null)
                    gas.noGravity = true;
            }

            // Layer 3: newborn star sparks streaking out of the cloud.
            for (int i = 0; i < 40; i++)
            {
                Vector2 dir =
                    Main.rand.NextVector2Circular(1f, 1f)
                        .SafeNormalize(Vector2.UnitX);
                Color starColor = Main.rand.Next(3) switch
                {
                    0 => Color.White,
                    1 => new Color(150, 220, 255),
                    _ => new Color(255, 220, 130),
                };

                Dust star = Dust.NewDustPerfect(
                    Projectile.Center,
                    DustID.FireworksRGB,
                    dir * Main.rand.NextFloat(8f, 16f),
                    0,
                    starColor,
                    Main.rand.NextFloat(0.7f, 1.2f)
                );
                if (star != null)
                    star.noGravity = true;
            }

            // Add strong pink and purple explosion light.
            Lighting.AddLight(
                Projectile.Center,
                1.5f,
                0.2f,
                1.2f
            );

            // Create the damaging explosion projectile. Owner only: OnKill runs
            // on every client, so an unguarded spawn would create one explosion
            // per client and multiply the damage in multiplayer. (Dust and
            // sound above stay unguarded — they are local effects.)
            if (damage > 0 && Projectile.owner == Main.myPlayer)
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

            const float spriteScale = 0.55f;

            // One continuous ribbon behind the bolt, full width at the head
            // and tapering to a thin tail.
            float trailStartOffset =
                texture.Height * 0.5f * spriteScale;
            // Fixed trail width, decoupled from the hitbox (2026-11-14):
            // the hitbox is now the full 120x120 sprite, but deriving the
            // ribbon width from it blew the trail up ~7x. 4.95 restores
            // the pre-fix-pass ribbon. VertexStrip treats this as a
            // half-width.
            float trailHalfWidth = 4.95f;

            ShaderTrailDraw.Draw(
                trail.Positions,
                trail.Count,
                progress => ShaderTrailDraw.WithAlpha(
                    NebulaTrailColor(progress),
                    progress * progress * 0.95f),
                progress => trailHalfWidth * 1.3f * (float)Math.Pow(progress, 1.15f),
                "CelestialRibbon",
                effect =>
                {
                    SafeShaderDraw.Set(effect, "TrailStartColor", Color.White);
                    SafeShaderDraw.Set(effect, "TrailEndColor", Color.White);
                    SafeShaderDraw.Set(effect, "TrailPulseSpeed", 6f);
                    SafeShaderDraw.Set(effect, "TrailPulseIntensity", 0.3f);
                },
                trailStartOffset);

            return SafeShaderDraw.DrawWithShader("CosmicGlow",
                () =>
                {
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
                        Projectile.scale * spriteScale,
                        SpriteEffects.None,
                        0
                    );
                },
                (effect) =>
                {
                    SafeShaderDraw.Set(effect, "GlowRadius", 35f);
                    SafeShaderDraw.Set(effect, "GlowInnerColor", new Color(255, 128, 51));
                    SafeShaderDraw.Set(effect, "GlowOuterColor", new Color(153, 26, 102));
                    SafeShaderDraw.Set(effect, "GlowPulseSpeed", 3f);
                    SafeShaderDraw.Set(effect, "GlowPulseIntensity", 0.4f);
                    SafeShaderDraw.Set(effect, "GlowRotates", 1f);
                    SafeShaderDraw.Set(effect, "GlowRotationSpeed", 0.8f);
                });
        }

        // Multi-stop nebula gradient for the ribbon trail: deep space
        // violet at the tail, through magenta and hydrogen pink, to
        // pale starlight at the head. Progress 0 = tail, 1 = head.
        private static Color NebulaTrailColor(float progress)
        {
            if (progress < 0.25f)
                return Color.Lerp(
                    new Color(48, 0, 96),
                    new Color(138, 43, 226),
                    progress / 0.25f);
            if (progress < 0.5f)
                return Color.Lerp(
                    new Color(138, 43, 226),
                    new Color(255, 0, 255),
                    (progress - 0.25f) / 0.25f);
            if (progress < 0.75f)
                return Color.Lerp(
                    new Color(255, 0, 255),
                    new Color(255, 105, 180),
                    (progress - 0.5f) / 0.25f);
            return Color.Lerp(
                new Color(255, 105, 180),
                new Color(220, 240, 255),
                (progress - 0.75f) / 0.25f);
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
