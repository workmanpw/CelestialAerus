using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using CelestialAerus.Config;
using CelestialAerus.Players;
using CelestialAerus.Projectiles;

namespace CelestialAerus.Items
{
    public class CelestialTempest : ModItem
    {
        public override string Texture => "CelestialAerus/Textures/CelestialTempest";

        public override void SetStaticDefaults() { }

        public override void SetDefaults()
        {
            Item.width = 76;
            Item.height = 76;
            Item.damage = 3000;
            Item.DamageType = DamageClass.Melee;
            Item.useAnimation = 15;
            Item.useTime = 15;
            Item.useTurn = true;
            Item.useStyle = ItemUseStyleID.Swing;
            Item.crit += 25;
            Item.knockBack = 9.5f;
            Item.UseSound = SoundID.Item60;
            Item.autoReuse = true;
            Item.maxStack = 1;
            Item.value = 30000000;
            Item.rare = ItemRarityID.Purple;
            Item.shoot = ModContent.ProjectileType<TempestBeam>();
            Item.shootSpeed = 28f;
            Item.noUseGraphic = false;
            Item.channel = false;
        }

        public override bool CanReforge() => true;

        public override bool AltFunctionUse(Player player)
        {
            return ModContent.GetInstance<CelestialAerusConfig>().EnableCelestialTempestRightClickModeToggle;
        }

        public override bool CanUseItem(Player player)
        {
            // Right-click only toggles Tempest Star mode; suppress the
            // melee swing while toggling so it doesn't also deal a hit.
            bool toggling = player.altFunctionUse == 2
                && ModContent.GetInstance<CelestialAerusConfig>().EnableCelestialTempestRightClickModeToggle;
            Item.noMelee = toggling;
            return true;
        }

        public override bool? UseItem(Player player)
        {
            if (player.altFunctionUse == 2 && ModContent.GetInstance<CelestialAerusConfig>().EnableCelestialTempestRightClickModeToggle)
            {
                var modPlayer = player.GetModPlayer<AerusPlayer>();
                modPlayer.tempestMode = !modPlayer.tempestMode;

                string modeText = modPlayer.tempestMode ? "Tempest Star mode" : "Normal Tempest mode";
                Main.NewText($"Celestial Tempest: {modeText}", Color.Cyan);
                return true;
            }

            return null;
        }

        public override void ModifyTooltips(List<TooltipLine> list)
        {
            list.Add(new TooltipLine(Mod, "Tooltip0", $"Right click toggles Tempest Star mode ({(ModContent.GetInstance<CelestialAerusConfig>().EnableCelestialTempestRightClickModeToggle ? "enabled" : "disabled")})\nHeals 75 life on hit"));
        }

        public override bool Shoot(Player player, EntitySource_ItemUse_WithAmmo source, Vector2 position, Vector2 velocity, int type, int damage, float knockback)
        {
            var modPlayer = player.GetModPlayer<AerusPlayer>();
            if (player.altFunctionUse == 2 && ModContent.GetInstance<CelestialAerusConfig>().EnableCelestialTempestRightClickModeToggle)
                return false;

            if (modPlayer.tempestMode)
            {
                SpawnTempestStarFromScreenEdges(player, source, damage, knockback);
                return false;
            }

            // Main shot: TempestBeam
            type = ModContent.ProjectileType<TempestBeam>();
            int projectile = Projectile.NewProjectile(source, position.X, position.Y, velocity.X, velocity.Y, type, damage, knockback, player.whoAmI);
            if (projectile >= 0 && projectile < Main.maxProjectiles)
            {
                Main.projectile[projectile].timeLeft = 160;
                Main.projectile[projectile].tileCollide = false;
            }

            // Spread shots: TempestStar
            float num72 = Main.rand.Next(22, 30);
            int seekerDamage = (int)(damage * 0.25f);
            // vector2 / num78 / num79 / num80 are assigned fresh inside
            // the loop before any use; the old pre-loop computation of
            // them was dead and was removed 2026-11-14.
            Vector2 vector2;
            float num78, num79, num80;
            int num107 = 4;
            for (int num108 = 0; num108 < num107; num108++)
            {
                vector2 = new Vector2(
                    player.position.X + (float)player.width * 0.5f + (float)(-(float)player.direction) +
                    ((float)Main.mouseX + Main.screenPosition.X - player.position.X),
                    player.MountedCenter.Y);
                vector2.X = (vector2.X + player.Center.X) / 2f;
                vector2.Y -= (float)(100 * num108);
                num78 = (float)Main.mouseX + Main.screenPosition.X - vector2.X;
                num79 = (float)Main.mouseY + Main.screenPosition.Y - vector2.Y;
                num80 = (float)System.Math.Sqrt((double)(num78 * num78 + num79 * num79));
                if (num80 == 0f) num80 = num72; // Cursor exactly on the spawn point: avoid dividing by zero.
                num80 = num72 / num80;
                num78 *= num80;
                num79 *= num80;
                float speedX4 = num78 + (float)Main.rand.Next(-360, 361) * 0.02f;
                float speedY5 = num79 + (float)Main.rand.Next(-360, 361) * 0.02f;

                int projectileFire = Projectile.NewProjectile(
                    source, vector2.X, vector2.Y, speedX4, speedY5,
                    ModContent.ProjectileType<TempestStar>(),
                    seekerDamage, knockback, player.whoAmI,
                    0f, (float)Main.rand.Next(3));
                if (projectileFire >= 0 && projectileFire < Main.maxProjectiles)
                {
                    Main.projectile[projectileFire].timeLeft = 80;
                }
            }
            return false;
        }

        private static void SpawnTempestStarFromScreenEdges(Player player, EntitySource_ItemUse_WithAmmo source, int damage, float knockback)
        {
            Vector2 cursorWorldPos = Main.MouseWorld;
            float screenLeft = Main.screenPosition.X - 200;
            float screenRight = Main.screenPosition.X + Main.screenWidth + 200;
            float screenTop = Main.screenPosition.Y - 200;
            float screenBottom = Main.screenPosition.Y + Main.screenHeight + 200;
            int projectileCount = 12;

            for (int i = 0; i < projectileCount; i++)
            {
                float angle = (float)i / projectileCount * MathHelper.TwoPi;
                Vector2 spawnPos = Vector2.Zero;
                if (angle < MathHelper.PiOver2)
                {
                    float t = angle / MathHelper.PiOver2;
                    spawnPos = new Vector2(screenLeft + t * (screenRight - screenLeft), screenTop);
                }
                else if (angle < (float)System.Math.PI)
                {
                    float t = (angle - MathHelper.PiOver2) / MathHelper.PiOver2;
                    spawnPos = new Vector2(screenRight, screenTop + t * (screenBottom - screenTop));
                }
                else if (angle < 3f * MathHelper.PiOver2)
                {
                    float t = (angle - (float)System.Math.PI) / MathHelper.PiOver2;
                    spawnPos = new Vector2(screenRight - t * (screenRight - screenLeft), screenBottom);
                }
                else
                {
                    float t = (angle - 3f * MathHelper.PiOver2) / MathHelper.PiOver2;
                    spawnPos = new Vector2(screenLeft, screenBottom - t * (screenBottom - screenTop));
                }

                Vector2 direction = cursorWorldPos - spawnPos;
                direction.Normalize();
                Vector2 velocity = direction * 25f;
                velocity += new Vector2(Main.rand.NextFloat(-2f, 2f), Main.rand.NextFloat(-2f, 2f));

                int projectile = Projectile.NewProjectile(
                    source,
                    spawnPos,
                    velocity,
                    ModContent.ProjectileType<TempestStar>(),
                    damage,
                    knockback,
                    player.whoAmI,
                    0f,
                    0f
                );
                if (projectile >= 0 && projectile < Main.maxProjectiles)
                {
                    Main.projectile[projectile].timeLeft = 300;
                    Main.projectile[projectile].tileCollide = false;
                    Main.projectile[projectile].alpha = 255;
                }
            }
        }

        public override void AddRecipes()
        {
            Recipe recipe = CreateRecipe();
            recipe.AddIngredient(ModContent.ItemType<Cosmosis>());
            recipe.AddIngredient(ModContent.ItemType<ArkOfTheCelestials>());
            recipe.AddIngredient(ModContent.ItemType<AndromedasWrath>());
            recipe.AddTile(TileID.LunarCraftingStation);
            recipe.Register();
        }

        public override void MeleeEffects(Player player, Rectangle hitbox)
        {
            if (Main.rand.NextBool(5))
            {
                int num250 = Dust.NewDust(
                    new Vector2((float)hitbox.X, (float)hitbox.Y),
                    hitbox.Width, hitbox.Height,
                    DustID.RainbowTorch,
                    (float)(player.direction * 2), 0f, 150,
                    new Color(Main.DiscoR, Main.DiscoG, Main.DiscoB), 1.3f);
                Main.dust[num250].velocity *= 0.2f;
                Main.dust[num250].noGravity = true;
            }
        }

        public override void OnHitNPC(Player player, NPC target, NPC.HitInfo hit, int damageDone)
        {
            // Heal only — the mod-buff group (CelestialFlames /
            // CrushedDefense / Andromeda's Rage / biome buffs) was
            // removed 2026-11-14, temporarily.
            player.Heal(75);
        }
    }
}
