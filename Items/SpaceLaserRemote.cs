using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.DataStructures;
using Microsoft.Xna.Framework;
using CelestialAerus.Projectiles;
using Terraria.Audio;

namespace CelestialAerus.Items;

public class SpaceLaserRemote : ModItem
{
    public override string Texture => "CelestialAerus/Textures/SpaceLaserRemote";

    public override void SetStaticDefaults()
    {
    }

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
        // Low-life gate lives in CanUseItem now; reaching Shoot() means the
        // player has more than 50 life.
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
        // The remote costs 50 life per use; don't let the player start the
        // 120-tick use animation at low life only to have it do nothing.
        return player.statLife > 50;
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
