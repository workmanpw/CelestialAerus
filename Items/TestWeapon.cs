using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Localization;
using CelestialAerus.Projectiles;

namespace CelestialAerus.Items
{
    public class TestWeapon : ModItem
    {
        // Reuse the vanilla copper shortsword sprite.
        public override string Texture => "Terraria/Images/Item_" + ItemID.CopperShortsword;

        public override void SetStaticDefaults() { }

        public override LocalizedText DisplayName => Language.GetOrRegister("Test Weapon");
        public override LocalizedText Tooltip => Language.GetOrRegister(
            "Experimental test weapon\n" +
            "Fires homing rainbow bolts that pass through tiles"
        );

        public override void SetDefaults()
        {
            Item.width = 32;
            Item.height = 32;
            Item.damage = 200; // each projectile deals 25% of this = 50
            Item.DamageType = DamageClass.Melee;
            Item.useAnimation = 20;
            Item.useTime = 20;
            Item.useTurn = true;
            Item.useStyle = ItemUseStyleID.Swing; // swing like a broadsword
            Item.knockBack = 6f;
            Item.UseSound = SoundID.Item1; // basic swing sound; no sound on shoot
            Item.autoReuse = true;
            Item.rare = ItemRarityID.Blue;
            Item.value = 10000;
            Item.shoot = ModContent.ProjectileType<TestBolt>();
            Item.shootSpeed = 16f;
        }

        public override bool Shoot(Player player, EntitySource_ItemUse_WithAmmo source, Vector2 position, Vector2 velocity, int type, int damage, float knockback)
        {
            // Firing pattern matches the Cosmosis: one main bolt plus a spread
            // of seekers. Only the TestBolt is used here, at 25% weapon damage
            // each, with none of the Cosmosis's extra effects.
            type = ModContent.ProjectileType<TestBolt>();
            int projDamage = (int)(damage * 0.25f);

            // Main bolt, fired along the swing.
            int projectile = Projectile.NewProjectile(source, position.X, position.Y, velocity.X, velocity.Y, type, projDamage, knockback, Main.myPlayer);
            Main.projectile[projectile].timeLeft = 160;
            Main.projectile[projectile].tileCollide = false;

            // Spread seekers.
            float num72 = Main.rand.Next(22, 30);
            Vector2 vector2 = player.RotatedRelativePoint(player.MountedCenter, true);
            float num78 = (float)Main.mouseX + Main.screenPosition.X + vector2.X;
            float num79 = (float)Main.mouseY + Main.screenPosition.Y + vector2.Y;
            if (player.gravDir == -1f)
            {
                num79 = Main.screenPosition.Y + (float)Main.screenHeight + (float)Main.mouseY + vector2.Y;
            }
            float num80 = (float)Math.Sqrt((double)(num78 * num78 + num79 * num79));
            if ((float.IsNaN(num78) && float.IsNaN(num79)) || (num78 == 0f && num79 == 0f))
            {
                num78 = (float)player.direction;
                num79 = 0f;
                num80 = num72;
            }
            else
            {
                num80 = num72 / num80;
            }
            num78 *= num80;
            num79 *= num80;
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
                num80 = num72 / num80;
                num78 *= num80;
                num79 *= num80;
                float speedX4 = num78 + (float)Main.rand.Next(-360, 361) * 0.02f;
                float speedY5 = num79 + (float)Main.rand.Next(-360, 361) * 0.02f;

                int projectileFire = Projectile.NewProjectile(
                    source, vector2.X, vector2.Y, speedX4, speedY5,
                    type, projDamage, knockback, player.whoAmI);
                Main.projectile[projectileFire].timeLeft = 80;
            }
            return false;
        }

        public override void AddRecipes()
        {
            // One dirt, no crafting station required.
            CreateRecipe()
                .AddIngredient(ItemID.DirtBlock, 1)
                .Register();
        }
    }
}
