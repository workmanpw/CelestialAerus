using Terraria;
using Terraria.ModLoader;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.Graphics.Shaders;
using Terraria.Graphics.Effects;
using Terraria.ID;
using Terraria.DataStructures;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.Linq;
using System;
using CelestialAerus.Players;
using CelestialAerus.Projectiles;

namespace CelestialAerus.Items
{
    public class ArkOfTheCelestials : ModItem
    {
        public override string Texture => "CelestialAerus/Assets/Textures/ArkOfTheCelestials";
        public override void SetStaticDefaults()
        {
        }

        public override void SetDefaults()
        {
            Item.damage = 1800;
            Item.DamageType = DamageClass.Melee;
            Item.width = 50;
            Item.height = 50;
            Item.useTime = 20;
            Item.useAnimation = 20;
            Item.useStyle = ItemUseStyleID.Swing;
            Item.knockBack = 6f;
            Item.value = Item.sellPrice(0, 75);
            Item.rare = ItemRarityID.Red;
            Item.UseSound = SoundID.Item60;
            Item.autoReuse = true;
            Item.shoot = ModContent.ProjectileType<CelestialFireball>();
            Item.shootSpeed = 12f;
            // Item.glowMask = (short)CelestialAerusMod.ArkGlowMask; // Commented out due to API limitations
        }

        public override void AddRecipes()
        {
            CreateRecipe()
                .AddIngredient(ItemID.LunarBar, 50)
                .AddIngredient(ItemID.FragmentSolar, 20)
                .AddIngredient(ItemID.FragmentVortex, 20)
                .AddIngredient(ItemID.FragmentNebula, 20)
                .AddIngredient(ItemID.FragmentStardust, 20)
                .AddTile(TileID.LunarCraftingStation)
                .Register();
        }

        public override bool Shoot(Player player, EntitySource_ItemUse_WithAmmo source, Vector2 position, Vector2 velocity, int type, int damage, float knockBack)
        {
            // Only shoot one group per click
            
            // Get cursor position
            Vector2 cursorPos = Main.MouseWorld;
            
            // Spawn 4 to 8 fireballs in a random spread around cursor
            int fireballCount = Main.rand.Next(4, 9);
            
            for (int i = 0; i < fireballCount; i++)
            {
                // Random position within 30 tiles (480 pixels)
                float distance = Main.rand.NextFloat(100f, 480f);
                Vector2 offset = Main.rand.NextVector2Unit() * distance;
                Vector2 spawnPos = cursorPos + offset;
                
                // Calculate velocity towards cursor center
                Vector2 fireVelocity = (cursorPos - spawnPos).SafeNormalize(Vector2.UnitX) * 10f;
                
                // The fireball ignores ai[1] — the whole volley homes at
                // once by design (stagger removed 2026-11-14).
                Projectile.NewProjectile(
                    source,
                    spawnPos,
                    fireVelocity,
                    ModContent.ProjectileType<CelestialFireball>(),
                    damage,
                    knockBack,
                    player.whoAmI
                );
            }

            return false;
        }

        public override void OnHitNPC(Player player, NPC target, NPC.HitInfo hit, int damageDone)
        {
            // Heal only — the mod-buff group (CelestialFlames /
            // CrushedDefense) was removed 2026-11-14, temporarily.
            player.Heal(250);
        }
    }
}
