using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Localization;
using CelestialAerus.AerusBuffs;
using CelestialAerus.Projectiles;

namespace CelestialAerus.Items
{
    public class NebulaBlazeSword : ModItem
    {
        public override string Texture =>
            "CelestialAerus/Textures/NebulaBlazeSword";

        public override void SetStaticDefaults()
        {
        }

        public override LocalizedText DisplayName =>
            Language.GetOrRegister("Nebula Blaze");

        public override LocalizedText Tooltip =>
            Language.GetOrRegister(
                "A stunning pink blade with a hatred for all creatures\n" +
                "It is very volatile and dangerous\n" +
                "Do not mistake it for candy\n" +
                "Rains cosmic energy onto enemies"
            );

        public override void SetDefaults()
        {
            Item.damage = 30;
            Item.DamageType = DamageClass.Melee;

            Item.width = 40;
            Item.height = 40;

            Item.useTime = 15;
            Item.useAnimation = 15;
            Item.useStyle = ItemUseStyleID.Swing;

            Item.knockBack = 6;

            Item.value = 100000;

            Item.rare = ItemRarityID.Pink;

            Item.UseSound = SoundID.Item60;
            Item.autoReuse = true;

            Item.shoot = ModContent.ProjectileType<NebulaBlazeBolt>();
            Item.shootSpeed = 4f;
        }

        public override void ModifyTooltips(List<TooltipLine> list)
        {
            list.Add(
                new TooltipLine(
                    Mod,
                    "HealTooltip",
                    "Heals 75 life on hit"
                )
            );
        }

        public override bool Shoot(
            Player player,
            EntitySource_ItemUse_WithAmmo source,
            Vector2 position,
            Vector2 velocity,
            int type,
            int damage,
            float knockback)
        {
            // Fire NebulaBlazeBolt in the same spread pattern
            // as the original Nebula Blaze Sword.

            float num72 = Main.rand.Next(5, 8);

            Vector2 vector2 =
                player.RotatedRelativePoint(
                    player.MountedCenter,
                    true
                );

            float num78 =
                (float)Main.mouseX +
                Main.screenPosition.X +
                vector2.X;

            float num79 =
                (float)Main.mouseY +
                Main.screenPosition.Y +
                vector2.Y;

            if (player.gravDir == -1f)
            {
                num79 =
                    Main.screenPosition.Y +
                    (float)Main.screenHeight +
                    (float)Main.mouseY +
                    vector2.Y;
            }

            float num80 =
                (float)Math.Sqrt(
                    (double)(
                        num78 * num78 +
                        num79 * num79
                    )
                );

            float num81 = num80;

            if (
                (float.IsNaN(num78) &&
                 float.IsNaN(num79)) ||
                (num78 == 0f && num79 == 0f)
            )
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
                    player.position.X +
                    (float)player.width * 0.5f +
                    (float)(-(float)player.direction) +
                    (
                        (float)Main.mouseX +
                        Main.screenPosition.X -
                        player.position.X
                    ),
                    player.MountedCenter.Y
                );

                vector2.X =
                    (vector2.X + player.Center.X) / 2f;

                vector2.Y -=
                    (float)(100 * num108);

                num78 =
                    (float)Main.mouseX +
                    Main.screenPosition.X -
                    vector2.X;

                num79 =
                    (float)Main.mouseY +
                    Main.screenPosition.Y -
                    vector2.Y;

                num80 =
                    (float)Math.Sqrt(
                        (double)(
                            num78 * num78 +
                            num79 * num79
                        )
                    );

                num80 = num72 / num80;

                num78 *= num80;
                num79 *= num80;

                float speedX4 =
                    num78 +
                    (float)Main.rand.Next(-360, 361) *
                    0.02f;

                float speedY5 =
                    num79 +
                    (float)Main.rand.Next(-360, 361) *
                    0.02f;

                int projectileFire =
                    Projectile.NewProjectile(
                        source,
                        vector2.X,
                        vector2.Y,
                        speedX4,
                        speedY5,
                        ModContent.ProjectileType<NebulaBlazeBolt>(),
                        15,
                        knockback,
                        player.whoAmI
                    );

                Main.projectile[projectileFire].timeLeft = 200;
                Main.projectile[projectileFire].tileCollide = false;
            }

            // Spawn a few projectiles from random positions
            // around the cursor, preserving the original behavior.

            Vector2 cursorWorldPos =
                Main.MouseWorld;

            for (int i = 0; i < 3; i++)
            {
                float angle =
                    Main.rand.NextFloat(
                        0,
                        MathHelper.TwoPi
                    );

                float distance =
                    Main.rand.Next(150, 250);

                Vector2 spawnPos =
                    cursorWorldPos +
                    new Vector2(
                        (float)Math.Cos(angle) *
                            distance,
                        (float)Math.Sin(angle) *
                            distance
                    );

                Vector2 toCursor =
                    cursorWorldPos -
                    spawnPos;

                toCursor.Normalize();

                toCursor *=
                    Main.rand.Next(4, 7);

                int rainProjectile =
                    Projectile.NewProjectile(
                        source,
                        spawnPos,
                        toCursor,
                        ModContent.ProjectileType<NebulaBlazeBolt>(),
                        15,
                        knockback,
                        player.whoAmI
                    );

                Main.projectile[rainProjectile].timeLeft = 300;
                Main.projectile[rainProjectile].tileCollide = false;
            }

            return false;
        }

        public override void OnHitNPC(
            Player player,
            NPC target,
            NPC.HitInfo hit,
            int damageDone)
        {
            // Apply CelestialFlames debuff.
            target.AddBuff(
                ModContent.BuffType<CelestialFlames>(),
                300
            );

            // Apply CrushedDefense debuff.
            target.AddBuff(
                ModContent.BuffType<CrushedDefense>(),
                600
            );
        }

        public override void AddRecipes()
        {
            Recipe recipe = CreateRecipe();

            recipe.AddIngredient(
                ItemID.HallowedBar,
                50
            );

            recipe.AddTile(TileID.MythrilAnvil);

            recipe.Register();
        }
    }
}
