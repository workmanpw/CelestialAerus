using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Media;
using CelestialAerus.Projectiles;
using CelestialAerus;
using System.Collections.Generic;

namespace CelestialAerus.Items.Weapons;

public class AncientHatchet : ModItem
{
    public override string Texture => "CelestialAerus/Assets/Textures/AncientHatchet";

    public override void SetStaticDefaults()
    {
    }

    public override void SetDefaults()
    {
        Item.CloneDefaults(ItemID.PossessedHatchet);
        Item.width = 32;
        Item.height = 32;
        Item.damage = 140;
        Item.rare = ItemRarityID.Pink;
        Item.value = Item.sellPrice(0, 5);
        Item.shoot = ModContent.ProjectileType<AncientHatchetProjectile>();
        Item.shootSpeed = 22f; // Increased speed for better feel with infinite throw
        Item.DamageType = DamageClass.Melee;
        Item.noMelee = true;
        Item.noUseGraphic = true;
        Item.autoReuse = true;
        Item.useTime = 15; // Faster use time for rapid throwing
        Item.useAnimation = 15;
        Item.consumable = false; // Makes it infinite
        Item.maxStack = 1; // Can't stack infinite items
    }
    public override bool CanUseItem(Player player)
    {
        // No limit on number of hatchets
        return true;
    }
    

    public override void AddRecipes()
    {
        CreateRecipe()
            .AddIngredient(ItemID.PossessedHatchet)
            .AddIngredient(ItemID.FragmentSolar, 5)
            .AddTile(TileID.LunarCraftingStation)
            .Register();
    }

    public override void ModifyTooltips(List<TooltipLine> list)
    {
        list.Add(new TooltipLine(Mod, "HealTooltip", "Heals 50 life on hit"));
    }
}
