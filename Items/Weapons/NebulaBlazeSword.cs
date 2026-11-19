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
    public class NebulaBlazeSword : ModItem
    {
        public override string Texture =>
            "CelestialAerus/Assets/Textures/NebulaBlazeSword";

        public override void SetStaticDefaults()
        {
        }

        public override void SetDefaults()
        {
            Item.damage = 250;
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
            // as the original Nebula Blaze Sword: four bolts rain
            // down from above, then three more converge on the
            // cursor from random directions. Restored 2026-11-14
            // at Peyton's order (the Spiral Galaxy Volley didn't
            // feel right). Bolt damage scales with the weapon
            // (50%) instead of the old hardcoded 15, since the
            // sword now deals 250.

            float num72 = Main.rand.Next(5, 8);

            // vector2 / num78 / num79 / num80 are assigned fresh inside
            // the loop before any use; the old pre-loop computation of
            // them was dead and was removed 2026-11-14 (num81 was never
            // read anywhere).
            Vector2 vector2;
            float num78, num79, num80;

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

                // Cursor exactly on the spawn point: avoid dividing by zero.
                if (num80 < 1f) num80 = 1f;

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
                        (int)(damage * 0.5f),
                        knockback,
                        player.whoAmI
                    );

                if (projectileFire >= 0 && projectileFire < Main.maxProjectiles)
                {
                    Main.projectile[projectileFire].timeLeft = 200;
                    Main.projectile[projectileFire].tileCollide = false;
                }
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
                        (int)(damage * 0.5f),
                        knockback,
                        player.whoAmI
                    );

                if (rainProjectile >= 0 && rainProjectile < Main.maxProjectiles)
                {
                    Main.projectile[rainProjectile].timeLeft = 300;
                    Main.projectile[rainProjectile].tileCollide = false;
                }
            }

            return false;
        }

        public override void OnHitNPC(
            Player player,
            NPC target,
            NPC.HitInfo hit,
            int damageDone)
        {
            // The tooltip promises 75 life on hit. Heal only — the
            // mod-buff group (CelestialFlames / CrushedDefense) was
            // removed 2026-11-14, temporarily.
            player.Heal(75);
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
