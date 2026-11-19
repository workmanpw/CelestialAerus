using CelestialAerus.Projectiles.Weapons;
using CelestialAerus.Items.Weapons;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using CelestialAerus.Players;
using CelestialAerus.Projectiles;

namespace CelestialAerus.Items
{
    public class CelestialAerus : ModItem
    {
        public override string Texture => "CelestialAerus/Assets/Textures/CelestialAerus";

        public override void SetStaticDefaults()
        {
        }

        public override void SetDefaults()
        {
            Item.width = 120;
            Item.height = 120;
            Item.damage = 300;
            Item.useTime = 14;
            Item.useAnimation = 14;
            Item.useStyle = ItemUseStyleID.Swing;
            Item.knockBack = 6f;
            Item.value = Item.sellPrice(0, 50);
            Item.rare = ItemRarityID.Purple;
            Item.autoReuse = true;
            Item.DamageType = DamageClass.Melee;
            Item.shoot = ModContent.ProjectileType<CelestialFireball>();
            Item.shootSpeed = Item.useTime;
        }

        public override bool AltFunctionUse(Player player) => false;

        public override bool CanUseItem(Player player)
        {
            var modPlayer = player.GetModPlayer<AerusPlayer>();
            if (modPlayer.aerusMode == 1)
            {
                // Boomerang mode: configured exactly like the Ancient
                // Hatchet so the blade throws on every swing — Shoot
                // use-style, matching useTime/useAnimation, and the thrown
                // blade as the item's shoot type so the vanilla shoot path
                // spawns it (no Shoot override interference).
                Item.useStyle = ItemUseStyleID.Shoot;
                Item.useTime = 10;
                Item.useAnimation = 10;
                Item.noMelee = true;
                Item.noUseGraphic = true;
                Item.shoot = ModContent.ProjectileType<ThrownCelestialAerus>();
                Item.shootSpeed = 22f;
                Item.UseSound = SoundID.Item1;
            }
            else
            {
                // Fireball mode: the swung sword.
                Item.useStyle = ItemUseStyleID.Swing;
                Item.useTime = 14;
                Item.useAnimation = 14;
                Item.noMelee = false;
                Item.noUseGraphic = false;
                Item.shoot = ModContent.ProjectileType<CelestialFireball>();
                Item.shootSpeed = 20f;
                Item.UseSound = SoundID.Item60;
            }
            return true;
        }

        public override bool Shoot(
            Player player,
            EntitySource_ItemUse_WithAmmo source,
            Vector2 position,
            Vector2 velocity,
            int type,
            int damage,
            float knockBack
        )
        {
            var modPlayer = player.GetModPlayer<AerusPlayer>();

            if (modPlayer.aerusMode == 1)
            {
                // Boomerang mode: Item.shoot is already the thrown blade
                // (set in CanUseItem), so let the vanilla shoot path spawn
                // it — exactly like the Ancient Hatchet.
                return true;
            }
            else
            {
                for (int i = 0; i < 6; i++)
                {
                    Vector2 spawn = new(
                        Main.MouseWorld.X + Main.rand.Next(-Main.screenWidth / 2, Main.screenWidth / 2),
                        Main.MouseWorld.Y - 400 - i * 80
                    );
                    // Original spawn speed (14) and stagger value (i*12)
                    // restored 2026-11-14. NOTE: the fireball ignores ai[1]
                    // — the stagger was dead data in the original and stays
                    // dead. Do not implement stagger handling in the
                    // fireball; it made the volley crawl.
                    Vector2 toCursor = Vector2.Normalize(Main.MouseWorld - spawn)
                        .RotatedByRandom(MathHelper.ToRadians(10f)) * 14f;
                    Projectile.NewProjectile(
                        source, spawn, toCursor,
                        type, damage, knockBack,
                        player.whoAmI, 0f, i * 12f
                    );
                }
            }

            return false;
        }

        public override void AddRecipes()
        {
            CreateRecipe()
                .AddIngredient(ItemID.FragmentSolar, 20)
                .AddIngredient(ItemID.FragmentVortex, 20)
                .AddIngredient(ItemID.FragmentNebula, 20)
                .AddIngredient(ItemID.FragmentStardust, 20)
                .AddIngredient(ModContent.ItemType<AncientHatchet>())
                .AddIngredient(ItemID.Meowmere)
                .AddIngredient(ItemID.StarWrath)
                .AddTile(TileID.LunarCraftingStation)
                .Register();
        }

        public override void OnHitNPC(Player player, NPC target, NPC.HitInfo hit, int damageDone)
        {
            // Heal only — the mod-buff group (CelestialFlames /
            // CrushedDefense) was removed 2026-11-14, temporarily.
            player.Heal(125);
        }
    }
}
