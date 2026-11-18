using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using CelestialAerus.Projectiles;

namespace CelestialAerus.Items
{
    public class Firstlight : ModItem
    {
        // Firstlight's own texture: a cyan trial-blade with magenta
        // energy (Textures/Firstlight.png). No more vanilla placeholder.
        public override string Texture => "CelestialAerus/Textures/Firstlight";

        public override void SetStaticDefaults() { }

        public override void SetDefaults()
        {
            Item.width = 120;
            Item.height = 120;
            Item.damage = 200; // each projectile deals 25% of this = 50
            Item.DamageType = DamageClass.Melee;
            Item.useAnimation = 16;
            Item.useTime = 16;
            Item.useTurn = true;
            Item.useStyle = ItemUseStyleID.Swing; // swing like a broadsword
            Item.knockBack = 6f;
            Item.UseSound = SoundID.Item1; // basic swing sound; no sound on shoot
            Item.autoReuse = true;
            Item.rare = ItemRarityID.Purple;
            Item.value = Item.sellPrice(0, 30);
            Item.shoot = ModContent.ProjectileType<TrialBolt>();
            Item.shootSpeed = 16f;
        }

        public override bool Shoot(Player player, EntitySource_ItemUse_WithAmmo source, Vector2 position, Vector2 velocity, int type, int damage, float knockback)
        {
            // Firing pattern matches the Cosmosis: one main bolt plus a spread
            // of seekers. Only the TrialBolt is used here, at 25% weapon damage
            // each, with none of the Cosmosis's extra effects.
            type = ModContent.ProjectileType<TrialBolt>();
            int projDamage = (int)(damage * 0.25f);

            // Main bolt, fired along the swing.
            int projectile = Projectile.NewProjectile(source, position.X, position.Y, velocity.X, velocity.Y, type, projDamage, knockback, player.whoAmI);
            if (projectile >= 0 && projectile < Main.maxProjectiles)
            {
                Main.projectile[projectile].timeLeft = 160;
                Main.projectile[projectile].tileCollide = false;
            }

            // Spread seekers.
            float num72 = Main.rand.Next(22, 30);
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
                    type, projDamage, knockback, player.whoAmI);
                if (projectileFire >= 0 && projectileFire < Main.maxProjectiles)
                {
                    Main.projectile[projectileFire].timeLeft = 80;
                }
            }
            return false;
        }

        public override void AddRecipes()
        {
            // A real weapon gets a real recipe: the pinnacle of mortal
            // blades (Terra Blade) proven worthy of starlight, at the
            // Lunar Crafting Station. Mid-tier cost for a mid-tier blade.
            CreateRecipe()
                .AddIngredient(ItemID.TerraBlade)
                .AddIngredient(ItemID.FragmentSolar, 15)
                .AddIngredient(ItemID.FragmentVortex, 15)
                .AddIngredient(ItemID.FragmentNebula, 15)
                .AddIngredient(ItemID.FragmentStardust, 15)
                .AddTile(TileID.LunarCraftingStation)
                .Register();
        }
    }
}
