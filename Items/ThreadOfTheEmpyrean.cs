using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using CelestialAerus.Projectiles.ThreadOfTheEmpyrean;

namespace CelestialAerus.Items
{
    public class ThreadOfTheEmpyrean : ModItem
    {
        public override string Texture => "CelestialAerus/Textures/ThreadOfTheEmpyrean";

        public override void SetStaticDefaults()
        {
            ItemID.Sets.Yoyo[Item.type] = true;
            ItemID.Sets.GamepadExtraRange[Item.type] = 15;
            ItemID.Sets.GamepadSmartQuickReach[Item.type] = true;
        }

        public override LocalizedText DisplayName => Language.GetOrRegister("Thread Of The Empyrean");
        public override LocalizedText Tooltip => Language.GetOrRegister(
            "Greatness on a string\n" +
            "Tosses an extremely hasty yo-yo\n" +
            "Fires Celestial Fireballs that rapidly seek targets\n" +
            "Rumor has it that this yo-yo is very dangerous"
        );

        public override void SetDefaults()
        {
            Item.width = 24;
            Item.height = 24;
            Item.useStyle = ItemUseStyleID.Shoot;
            Item.useAnimation = 25;
            Item.useTime = 25;
            Item.shoot = ModContent.ProjectileType<ThreadOfTheEmpyreanProjectile>();
            Item.shootSpeed = 10f;
            Item.noUseGraphic = true;
            Item.noMelee = true;
            Item.channel = true;
            Item.damage = 1200;
            Item.knockBack = 1f;
            Item.DamageType = DamageClass.Melee;
            Item.value = Item.sellPrice(0, 35);
            Item.rare = ItemRarityID.Red;
            Item.autoReuse = true;
        }

        public override void AddRecipes()
        {
            CreateRecipe()
                .AddIngredient(ItemID.FragmentSolar, 12)
                .AddIngredient(ItemID.FragmentVortex, 12)
                .AddIngredient(ItemID.FragmentNebula, 12)
                .AddIngredient(ItemID.FragmentStardust, 12)
                .AddIngredient(ItemID.Terrarian)
                .AddIngredient(ItemID.LunarBar, 40)
                .AddIngredient(ModContent.ItemType<AncientHatchet>())
                .AddTile(TileID.LunarCraftingStation)
                .Register();
        }

        public override void ModifyTooltips(List<TooltipLine> list)
        {
            list.Add(new TooltipLine(Mod, "HealTooltip", "Heals 75 life on hit"));
        }
    }
}
