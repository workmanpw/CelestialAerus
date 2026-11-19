using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.GameContent;
using Terraria.Audio;
using Terraria.DataStructures;
using CelestialAerus.Effects;

namespace CelestialAerus.Projectiles
{
    public class TempestStar : ModProjectile
    {
        public override string Texture => "CelestialAerus/Assets/Textures/TempestStar";

        private readonly TrailRecorder trail = new TrailRecorder(MaxTrail);
        private const int MaxTrail = 20;
        private const float SpinSpeed = 0.3f;

        public override void SetDefaults()
        {
            // Hitbox already matches the drawn sprite: 32x32 texture at scale 1.
            Projectile.width = 32;
            Projectile.height = 32;
            Projectile.friendly = true;
            Projectile.hostile = false;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.alpha = 255;
            Projectile.penetrate = 2;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 12;
            Projectile.aiStyle = -1;
            Projectile.timeLeft = 450;
        }

        public override void AI()
        {
            if (Projectile.alpha > 0)
            {
                Projectile.alpha -= 15;
                if (Projectile.alpha < 0)
                    Projectile.alpha = 0;
            }

            Projectile.rotation += SpinSpeed;

            trail.Record(Projectile.Center);

            float hue = (Main.GlobalTimeWrappedHourly * 0.6f) % 1f;
            Color rainbowColor = Main.hslToRgb(hue, 1f, 0.7f);
            Lighting.AddLight(Projectile.Center, rainbowColor.ToVector3() * 1.2f);

            // Homing toward nearest valid target.
            NPC bestTarget = null;
            float bestDist = 1600f;

            for (int i = 0; i < Main.maxNPCs; i++)
            {
                NPC npc = Main.npc[i];
                if (npc.CanBeChasedBy(Projectile, false))
                {
                    float d = Vector2.Distance(Projectile.Center, npc.Center);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestTarget = npc;
                    }
                }
            }

            if (bestTarget != null)
            {
                float homingSpeed = 35f;
                Vector2 toTarget = bestTarget.Center - Projectile.Center;
                float len = toTarget.Length();
                if (len > 0.001f)
                {
                    toTarget = toTarget / len * homingSpeed;
                    Projectile.velocity = (Projectile.velocity * 20f + toTarget) / 21f;
                }
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Color lightColorCopy = lightColor;
            Texture2D texture = TextureAssets.Projectile[Type].Value;

            // Start the ribbon at the star's rear edge and give it the
            // projectile's full width at the head, tapering to a point.
            float trailStartOffset = texture.Height * 0.5f * Projectile.scale;
            float trailHalfWidth = Math.Max(Projectile.width, Projectile.height) * 0.5f * Projectile.scale; // VertexStrip treats this as a half-width

            ShaderTrailDraw.Draw(
                trail.Positions,
                trail.Count,
                progress =>
                {
                    float hue = (Main.GlobalTimeWrappedHourly * 0.6f + progress * 0.14f) % 1f;
                    return ShaderTrailDraw.WithAlpha(Main.hslToRgb(hue, 1f, 0.72f), progress * progress * 0.8f);
                },
                progress => trailHalfWidth * (float)Math.Pow(progress, 1.15f),
                "CelestialRibbon",
                effect =>
                {
                    SafeShaderDraw.Set(effect, "TrailStartColor", Color.White);
                    SafeShaderDraw.Set(effect, "TrailEndColor", Color.White);
                    SafeShaderDraw.Set(effect, "TrailPulseSpeed", 6f);
                    SafeShaderDraw.Set(effect, "TrailPulseIntensity", 0.25f);
                },
                trailStartOffset);

            return SafeShaderDraw.DrawWithShader("AethericPulse",
                () =>
                {
                    Main.EntitySpriteDraw(texture, Projectile.Center - Main.screenPosition, null,
                        Projectile.GetAlpha(lightColorCopy), Projectile.rotation, texture.Size() / 2f,
                        Projectile.scale, SpriteEffects.None, 0);
                },
                (effect) =>
                {
                    SafeShaderDraw.Set(effect, "PulseIntensity", 1.2f);
                    SafeShaderDraw.Set(effect, "PulseCount", 3f);
                    SafeShaderDraw.Set(effect, "PulseColor", new Color(128, 255, 230));
                });
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            Player player = Projectile.owner >= 0 && Projectile.owner < Main.maxPlayers
                ? Main.player[Projectile.owner]
                : null;

            // Heal only: the Celestial hit bundle (buffs/debuffs) was
            // removed temporarily — Peyton has a better idea coming.
            // The 75 heal stays so the tooltip remains true.
            if (player != null && player.active)
            {
                int healAmount = 75;
                if (player.statLife + healAmount > player.statLifeMax2)
                    healAmount = player.statLifeMax2 - player.statLife;
                player.Heal(healAmount);
                player.HealEffect(healAmount);
            }

            // Owner only: OnHitNPC runs on every client, so an unguarded
            // spawn would create one biome projectile per client.
            // Pass the star's own damage so the biome projectile scales
            // with the weapon instead of a flat 250.
            if (player != null && player.active && Projectile.owner == Main.myPlayer)
                SpawnBiomeProjectiles(player, target.Center, target, Projectile.GetSource_OnHit(target), Projectile.damage);

            SoundEngine.PlaySound(SoundID.Item14, Projectile.Center);

            if (Main.netMode == NetmodeID.Server)
                return;

            for (int i = 0; i < 35; i++)
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

            for (int i = 0; i < 25; i++)
            {
                int spark = Dust.NewDust(
                    Projectile.position, Projectile.width, Projectile.height,
                    DustID.GoldFlame, 0f, 0f, 50, default, 2f);
                Main.dust[spark].velocity *= 2f;
                Main.dust[spark].noGravity = true;
                Main.dust[spark].color = new Color(200, 255, 230, 150);
            }
        }

        private static void SpawnBiomeProjectiles(Player player, Vector2 targetPosition, NPC target, IEntitySource source, int starDamage)
        {
            bool holy = player.ZoneHallow;
            bool nebula = player.ZoneTowerNebula;
            bool stardust = player.ZoneTowerStardust;
            bool solar = player.ZoneTowerSolar;
            bool vortex = player.ZoneTowerVortex;
            bool hell = player.ZoneUnderworldHeight;
            bool corrupt = player.ZoneCorrupt;
            bool crimson = player.ZoneCrimson;
            bool dungeon = player.ZoneDungeon;
            bool space = player.ZoneSkyHeight;
            bool forest = !corrupt && !crimson && !hell && !holy && !nebula && !stardust && !solar && !vortex
                          && !player.ZoneJungle && !player.ZoneSnow && !player.ZoneBeach && !player.ZoneDesert
                          && !dungeon && !player.ZoneGlowshroom && !space;
            bool mushroom = player.ZoneGlowshroom;
            bool desert = player.ZoneDesert;
            bool pumpkinMoon = Main.pumpkinMoon;
            bool frostMoon = Main.snowMoon;
            bool bloodMoon = Main.bloodMoon;

            int projectileType = ProjectileID.Bullet;
            // Scales with the weapon: 25% of the star's damage, the same
            // fraction the mod's seekers use (was a flat 250).
            int damage = (int)(starDamage * 0.25f);

            if (solar) projectileType = ProjectileID.Daybreak;
            else if (nebula) projectileType = ProjectileID.NebulaBlaze1;
            else if (vortex) projectileType = ProjectileID.VortexBeaterRocket;
            else if (stardust) projectileType = ProjectileID.StardustCellMinionShot;
            else if (holy) projectileType = ProjectileID.RainbowRodBullet;
            else if (hell) projectileType = ProjectileID.Flamelash;
            else if (corrupt) projectileType = ProjectileID.TinyEater;
            else if (crimson) projectileType = ProjectileID.VampireKnife;
            else if (forest) projectileType = ProjectileID.TerrarianBeam;
            else if (mushroom) projectileType = ProjectileID.Mushroom;
            else if (desert) projectileType = ProjectileID.BlackBolt;
            else if (player.ZoneBeach) projectileType = ProjectileID.FlaironBubble;
            else if (player.ZoneSnow) projectileType = ProjectileID.FrostBoltSword;
            else if (pumpkinMoon) projectileType = ProjectileID.FlamingJack;
            else if (frostMoon) projectileType = ProjectileID.FrostBoltSword;
            else if (bloodMoon) projectileType = ProjectileID.BloodArrow;
            else if (dungeon) projectileType = ProjectileID.ShadowBeamFriendly;
            else if (space) projectileType = ProjectileID.FallingStar;

            Vector2 velocity = new Vector2(Main.rand.NextFloat(-6f, 6f), Main.rand.NextFloat(-6f, 6f));
            velocity = velocity.SafeNormalize(Vector2.UnitX);
            velocity *= Main.rand.NextFloat(8f, 14f);

            int newProjectile = Projectile.NewProjectile(
                source,
                targetPosition,
                velocity,
                projectileType,
                damage,
                2f,
                player.whoAmI
            );
            if (newProjectile >= 0 && newProjectile < Main.maxProjectiles)
            {
                Main.projectile[newProjectile].usesLocalNPCImmunity = false;
                Main.projectile[newProjectile].localNPCHitCooldown = 0;
                // Some biome picks are enemy projectiles by default
                // (e.g. TinyEater): force them friendly so they can never
                // hurt the player.
                Main.projectile[newProjectile].friendly = true;
                Main.projectile[newProjectile].hostile = false;
                Main.projectile[newProjectile].netUpdate = true;
            }
        }
    }
}
