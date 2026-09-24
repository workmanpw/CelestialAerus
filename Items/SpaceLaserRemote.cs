using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.DataStructures;
using Microsoft.Xna.Framework;
using Terraria.Localization;
using CelestialAerus.AerusProjectiles;
using CelestialAerus.Projectiles;
using Terraria.Audio;

namespace CelestialAerus.Items;

public class SpaceLaserRemote : ModItem
{
    public override string Texture => "Terraria/Images/Item_3785";

    public override void SetStaticDefaults()
    {
    }

    public override LocalizedText DisplayName => Language.GetText("Space Laser Remote");
    public override LocalizedText Tooltip => Language.GetText("A device of untold, unmatched power\nIt is extremely dangerous\nDo not use in towns\n'Make sure you point it away from yourself, lest you become a pile of ash.'");

    public override void SetDefaults()
    {
        Item.width = 40;
        Item.height = 40;
        Item.damage = 80000;
        Item.DamageType = DamageClass.Generic;
        Item.rare = ItemRarityID.Red;
        Item.value = Item.sellPrice(0, 50);
        Item.useTime = 120;
        Item.useAnimation = 120;
        Item.useStyle = ItemUseStyleID.HoldUp;
        Item.noMelee = true;
        Item.shoot = ModContent.ProjectileType<SpaceLaserBeam>();
    }

    public override bool Shoot(Player player, EntitySource_ItemUse_WithAmmo source, Vector2 position, Vector2 velocity, int type, int damage, float knockback)
    {
        if (player.statLife <= 50)
            return false;
            
        player.statLife -= 50;
        player.HealEffect(-50);
        
        Vector2 targetPos = Main.MouseWorld;
        
        SoundEngine.PlaySound(new SoundStyle("CelestialAerus/Sounds/SpaceLaserRemote-Unused"), player.Center);
        
        Projectile.NewProjectile(
            source,
            new Vector2(targetPos.X, targetPos.Y - 600),
            Vector2.Zero,
            ModContent.ProjectileType<SpaceLaserBeam>(),
            damage,
            knockback,
            player.whoAmI
        );
        
        return false;
    }

    public override bool CanUseItem(Player player)
    {
        return true;
    }

    public override void AddRecipes()
    {
        CreateRecipe()
            .AddIngredient(ItemID.LunarBar, 50)
            .AddIngredient(ItemID.FragmentSolar, 30)
            .AddIngredient(ItemID.FragmentNebula, 30)
            .AddIngredient(ItemID.ChainGun)
            .AddTile(TileID.LunarCraftingStation)
            .Register();
    }
}
