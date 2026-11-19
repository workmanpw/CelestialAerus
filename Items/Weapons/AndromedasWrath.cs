using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using CelestialAerus.Projectiles;

namespace CelestialAerus.Items
{
    public class AndromedasWrath : ModItem
    {
        public override string Texture => "CelestialAerus/Assets/Textures/AndromedasWrath";

        public override void SetStaticDefaults() { }

        public override void SetDefaults()
        {
            Item.width = 76;
            Item.height = 76;
            Item.damage = 2500;
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
            Item.shoot = ModContent.ProjectileType<AndromedaBeam>();
            Item.shootSpeed = 28f;
        }

        public override bool CanReforge() => true;

        public override void ModifyTooltips(List<TooltipLine> list)
        {
            list.Add(new TooltipLine(Mod, "Tooltip0", "Heals 75 life on hit"));
        }

        public override bool Shoot(Player player, EntitySource_ItemUse_WithAmmo source, Vector2 position, Vector2 velocity, int type, int damage, float knockback)
        {
            // Main shot – always AndromedaBeam
            type = ModContent.ProjectileType<AndromedaBeam>();
            int projectile = Projectile.NewProjectile(source, position.X, position.Y, velocity.X, velocity.Y, type, damage, knockback, player.whoAmI);
            if (projectile >= 0 && projectile < Main.maxProjectiles)
            {
                Main.projectile[projectile].timeLeft = 160;
                Main.projectile[projectile].tileCollide = false;
            }

            // Spread shots – AndromedaSeeker
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
                num80 = (float)Math.Sqrt((double)(num78 * num78 + num79 * num79));
                if (num80 == 0f) num80 = num72; // Cursor exactly on the spawn point: avoid dividing by zero.
                num80 = num72 / num80;
                num78 *= num80;
                num79 *= num80;
                float speedX4 = num78 + (float)Main.rand.Next(-360, 361) * 0.02f;
                float speedY5 = num79 + (float)Main.rand.Next(-360, 361) * 0.02f;

                int projectileFire = Projectile.NewProjectile(
                    source, vector2.X, vector2.Y, speedX4, speedY5,
                    ModContent.ProjectileType<AndromedaSeeker>(),
                    seekerDamage, knockback, player.whoAmI);
                if (projectileFire >= 0 && projectileFire < Main.maxProjectiles)
                {
                    Main.projectile[projectileFire].timeLeft = 80;
                    Main.projectile[projectileFire].scale = 0.5f;
                }
            }
            return false;
        }

        public override void AddRecipes()
        {
            Recipe recipe = CreateRecipe();
            recipe.AddIngredient(ModContent.ItemType<ThreadOfTheEmpyrean>());
            recipe.AddIngredient(ModContent.ItemType<ArkOfTheCelestials>());
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
