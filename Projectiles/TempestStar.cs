using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Localization;
using Terraria.GameContent;
using Terraria.Audio;
using Terraria.DataStructures;
using CelestialAerus.AerusBuffs;

namespace CelestialAerus.AerusProjectiles
{
    public class TempestStar : ModProjectile
    {
        public override string Texture => "CelestialAerus/Textures/TempestStar";

        private readonly List<Vector2> trailPositions = new List<Vector2>();
        private const int MaxTrail = 20;
        private const float SpinSpeed = 0.3f;

        public override void SetStaticDefaults()
        {
            ProjectileID.Sets.TrailCacheLength[Type] = 8;
            ProjectileID.Sets.TrailingMode[Type] = 2;
        }

        public override LocalizedText DisplayName => Language.GetOrRegister("Tempest Star");

        public override void SetDefaults()
        {
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

            trailPositions.Add(Projectile.Center);
            if (trailPositions.Count > MaxTrail)
                trailPositions.RemoveAt(0);

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
            Texture2D texture = TextureAssets.Projectile[Type].Value;
            Vector2 origin = texture.Size() / 2f;

            int count = trailPositions.Count;

            // Trail only — skip newest entry.
            for (int i = 0; i < count - 1; i++)
            {
                float progress = (count > 1) ? (float)i / (count - 1) : 0f;
                float alpha = progress * 0.6f;
                float scale = 0.3f + progress * 0.7f;

                float hue = (Main.GlobalTimeWrappedHourly * 0.6f + i * 0.02f) % 1f;
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
            // Signature debuffs.
            target.AddBuff(ModContent.BuffType<CelestialFlames>(), 600);
            target.AddBuff(ModContent.BuffType<CrushedDefense>(), 600);

            if (Projectile.owner >= 0 && Projectile.owner < Main.maxPlayers)
            {
                Player player = Main.player[Projectile.owner];
                if (player != null && player.active)
                {
                    player.Heal(75);
                    player.HealEffect(75);

                    // Flat buff bundle.
                    player.AddBuff(BuffID.Battle, 600);
                    player.AddBuff(BuffID.WellFed, 600);
                    player.AddBuff(BuffID.Wrath, 600);
                    player.AddBuff(BuffID.Rage, 600);
                    player.AddBuff(BuffID.MagicPower, 600);
                    player.AddBuff(BuffID.Ironskin, 600);
                    player.AddBuff(BuffID.RapidHealing, 600);

                    // Biome + full debuff list from the shared helper.
                    CelestialHitEffects.ApplyPlayerBiomeBuffs(player);
                    CelestialHitEffects.ApplyEnemyDebuffs(target);

                    // Pass the source through so the static method doesn't need
                    // to reach back into Projectile.
                    SpawnBiomeProjectiles(player, target.Center, target, Projectile.GetSource_OnHit(target));
                }
            }

            SoundEngine.PlaySound(SoundID.Item14, Projectile.Center);
        }

        private static void SpawnBiomeProjectiles(Player player, Vector2 targetPosition, NPC target, IEntitySource source)
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
            int damage = 250;

            if (solar) projectileType = ProjectileID.Daybreak;
            else if (nebula) projectileType = ProjectileID.NebulaBlaze1;
            else if (vortex) projectileType = ProjectileID.VortexBeaterRocket;
            else if (stardust) projectileType = ProjectileID.StardustCellMinionShot;
            else if (holy) projectileType = ProjectileID.RainbowRodBullet;
            else if (hell) projectileType = ProjectileID.Flamelash;
            else if (corrupt) projectileType = ProjectileID.TinyEater;
            else if (crimson) projectileType = ProjectileID.VampireKnife;
            else if (forest) projectileType = ProjectileID.TerrarianBeam;
            else if (mushroom) projectileType = 131;
            else if (desert) projectileType = ProjectileID.BlackBolt;
            else if (player.ZoneBeach) projectileType = ProjectileID.FlaironBubble;
            else if (player.ZoneSnow) projectileType = ProjectileID.FrostBoltSword;
            else if (pumpkinMoon) projectileType = ProjectileID.FlamingJack;
            else if (frostMoon) projectileType = ProjectileID.FrostBoltSword;
            else if (bloodMoon) projectileType = ProjectileID.BloodArrow;
            else if (dungeon) projectileType = ProjectileID.ShadowBeamFriendly;
            else if (space) projectileType = ProjectileID.FallingStar;

            Vector2 velocity = new Vector2(Main.rand.NextFloat(-6f, 6f), Main.rand.NextFloat(-6f, 6f));
            velocity.Normalize();
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
            Main.projectile[newProjectile].usesLocalNPCImmunity = false;
            Main.projectile[newProjectile].localNPCHitCooldown = 0;
        }
    }
}
