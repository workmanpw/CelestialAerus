using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using CelestialAerus.Players;

namespace CelestialAerus.Items
{
    // Brigitte's Charm (2026-11-15): the Soul of Eternity, as an accessory.
    // Named for Brigitte, the Goddess herself. Grants the effects of every
    // accessory (except the sniper scope's zoom), +600 defense, 600 HP/s and
    // 600 mana/s regeneration, summons one of every vanilla minion plus a
    // boss-minion army, and calls down a storm of projectiles. The heavy
    // lifting lives in CharmPlayer; this item is the switch that turns it on.
    public class BrigittesCharm : ModItem
    {
        public override string Texture => "CelestialAerus/Textures/BrigittesCharm";

        public override void SetStaticDefaults() { }

        public override void SetDefaults()
        {
            Item.width = 48;
            Item.height = 48;
            Item.accessory = true;
            Item.defense = 600;
            Item.rare = ItemRarityID.Red;
            Item.value = 100000000; // 10 platinum — the most expensive item in the mod.
        }

        public override void UpdateAccessory(Player player, bool hideVisual)
        {
            player.GetModPlayer<CharmPlayer>().charmEquipped = true;
        }

        public override void AddRecipes()
        {
            // As a final-tier accessory, the charm should not be a novelty
            // item that can be made from dirt. The real recipe ties it to the
            // modem's strongest gear and the lunar crafting bench.
            CreateRecipe()
                .AddIngredient(ItemID.FragmentSolar, 20)
                .AddIngredient(ItemID.FragmentVortex, 20)
                .AddIngredient(ItemID.FragmentNebula, 20)
                .AddIngredient(ItemID.FragmentStardust, 20)
                .AddIngredient(ModContent.ItemType<AncientHatchet>())
                .AddIngredient(ModContent.ItemType<AndromedasWrath>())
                .AddIngredient(ModContent.ItemType<ArkOfTheCelestials>())
                .AddIngredient(ModContent.ItemType<CelestialAerus>())
                .AddIngredient(ModContent.ItemType<CelestialTempest>())
                .AddIngredient(ModContent.ItemType<Cosmosis>())
                .AddIngredient(ModContent.ItemType<NebulaBlazeSword>())
                .AddIngredient(ModContent.ItemType<SpaceLaserRemote>())
                .AddIngredient(ModContent.ItemType<ThreadOfTheEmpyrean>())
                .AddTile(TileID.LunarCraftingStation)
                .Register();
        }
    }
}
