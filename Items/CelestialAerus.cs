using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using CelestialAerus.AerusBuffs;
using CelestialAerus.Players;
using CelestialAerus.Projectiles;

namespace CelestialAerus.Items
{
    public class CelestialAerus : ModItem
    {
        public override string Texture => "CelestialAerus/Textures/CelestialAerus";

        public override void SetStaticDefaults()
        {
        }

        public override LocalizedText DisplayName => Language.GetOrRegister("Celestial Aerus");
        public override LocalizedText Tooltip => Language.GetOrRegister(
            "The legendary blade forged from the heart of a dying universe\n" +
            "'When the last star falls, a new light is born'\n" +
            "Shoots homing Celestial Fireballs and cosmic blades\n" +
            "Switch between different attacks (must set keybind)\n" +
            "Heals 125 life on hit"
        );

        public override void SetDefaults()
        {
            Item.width = 120;
            Item.height = 120;
            Item.damage = 300;
            Item.useTime = 20;
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
                Item.noMelee = true;
                Item.noUseGraphic = true;
                Item.UseSound = SoundID.Item1;
            }
            else
            {
                Item.noMelee = false;
                Item.noUseGraphic = false;
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
                Projectile.NewProjectile(
                    source, position, velocity,
                    ModContent.ProjectileType<ThrownCelestialAerus>(),
                    damage, knockBack, player.whoAmI
                );
            }
            else
            {
                if (modPlayer.aerusFireballCooldown > 0)
                    return false;

                for (int i = 0; i < 6; i++)
                {
                    Vector2 spawn = new(
                        Main.MouseWorld.X + Main.rand.Next(-Main.screenWidth / 2, Main.screenWidth / 2),
                        Main.MouseWorld.Y - 400 - i * 80
                    );
                    Vector2 toCursor = Vector2.Normalize(Main.MouseWorld - spawn)
                        .RotatedByRandom(MathHelper.ToRadians(10f)) * 14f;
                    Projectile.NewProjectile(
                        source, spawn, toCursor,
                        type, damage, knockBack,
                        player.whoAmI, 0f, i * 12f
                    );
                }

                modPlayer.aerusFireballCooldown = 12;
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
            player.Heal(125);
            target.AddBuff(ModContent.BuffType<CelestialFlames>(), 300);
            target.AddBuff(ModContent.BuffType<CrushedDefense>(), 600);
        }
    }
}
