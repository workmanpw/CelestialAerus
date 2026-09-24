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
    public class AndromedasWrath : ModItem
    {
        public override string Texture => "CelestialAerus/Textures/AndromedasWrath";

        public override void SetStaticDefaults() { }

        public override LocalizedText DisplayName => Language.GetOrRegister("Andromeda's Wrath");
        public override LocalizedText Tooltip => Language.GetOrRegister(
            "The manifested fury of Andromeda Galaxy itself\n" +
            "\"Order!\"\n" +
            "Fires a devastating Andromeda Beam and seeking Andromeda Seekers\n" +
            "Grants powerful buffs based on what biome you are standing in, and heals a large amount of life on hit\n" +
            "Inflicts debilitating debuffs on enemies"
        );

        public override void SetDefaults()
        {
            Item.width = 76;
            Item.height = 76;
            Item.damage = 2500;
            Item.DamageType = DamageClass.Melee;
            Item.useAnimation = 15;
            Item.useTime = 15;
            Item.useTurn = true;
            Item.useStyle = ItemUseStyleID.Swing;
            Item.crit += 25;
            Item.knockBack = 9.5f;
            Item.UseSound = SoundID.Item60;
            Item.autoReuse = true;
            Item.maxStack = 1;
            Item.value = 30000000;
            Item.rare = ItemRarityID.Purple;
            Item.shoot = ModContent.ProjectileType<AndromedaBeam>();
            Item.shootSpeed = 28f;
            Item.prefix = -1;
        }

        public override bool CanReforge() => true;

        public override void ModifyTooltips(List<TooltipLine> list)
        {
            list.Add(new TooltipLine(Mod, "Tooltip0", "Heals 75 life on hit"));
        }

        public override bool Shoot(Player player, EntitySource_ItemUse_WithAmmo source, Vector2 position, Vector2 velocity, int type, int damage, float knockback)
        {
            // Main shot – always AndromedaBeam
            type = ModContent.ProjectileType<AndromedaBeam>();
            int projectile = Projectile.NewProjectile(source, position.X, position.Y, velocity.X, velocity.Y, type, damage, knockback, Main.myPlayer);
            Main.projectile[projectile].timeLeft = 160;
            Main.projectile[projectile].tileCollide = false;

            // Spread shots – AndromedaSeeker
            float num72 = Main.rand.Next(22, 30);
            damage = Main.rand.Next(500, 621);
            Vector2 vector2 = player.RotatedRelativePoint(player.MountedCenter, true);
            float num78 = (float)Main.mouseX + Main.screenPosition.X + vector2.X;
            float num79 = (float)Main.mouseY + Main.screenPosition.Y + vector2.Y;
            if (player.gravDir == -1f)
            {
                num79 = Main.screenPosition.Y + (float)Main.screenHeight + (float)Main.mouseY + vector2.Y;
            }
            float num80 = (float)Math.Sqrt((double)(num78 * num78 + num79 * num79));
            if ((float.IsNaN(num78) && float.IsNaN(num79)) || (num78 == 0f && num79 == 0f))
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
                    player.position.X + (float)player.width * 0.5f + (float)(-(float)player.direction) +
                    ((float)Main.mouseX + Main.screenPosition.X - player.position.X),
                    player.MountedCenter.Y);
                vector2.X = (vector2.X + player.Center.X) / 2f;
                vector2.Y -= (float)(100 * num108);
                num78 = (float)Main.mouseX + Main.screenPosition.X - vector2.X;
                num79 = (float)Main.mouseY + Main.screenPosition.Y - vector2.Y;
                num80 = (float)Math.Sqrt((double)(num78 * num78 + num79 * num79));
                num80 = num72 / num80;
                num78 *= num80;
                num79 *= num80;
                float speedX4 = num78 + (float)Main.rand.Next(-360, 361) * 0.02f;
                float speedY5 = num79 + (float)Main.rand.Next(-360, 361) * 0.02f;

                int projectileFire = Projectile.NewProjectile(
                    source, vector2.X, vector2.Y, speedX4, speedY5,
                    ModContent.ProjectileType<AndromedaSeeker>(),
                    damage, knockback, player.whoAmI,
                    1f, (float)Main.rand.Next(3));
                Main.projectile[projectileFire].timeLeft = 80;
                Main.projectile[projectileFire].scale = 0.5f;
            }
            return false;
        }

        public override void AddRecipes()
        {
            Recipe recipe = CreateRecipe();
            recipe.AddIngredient(ModContent.ItemType<ThreadOfTheEmpyrean>());
            recipe.AddIngredient(ModContent.ItemType<ArkOfTheCelestials>());
            recipe.AddTile(TileID.WorkBenches);
            recipe.Register();
        }

        public override void MeleeEffects(Player player, Rectangle hitbox)
        {
            if (Main.rand.NextBool(5))
            {
                int num250 = Dust.NewDust(
                    new Vector2((float)hitbox.X, (float)hitbox.Y),
                    hitbox.Width, hitbox.Height,
                    DustID.RainbowTorch,
                    (float)(player.direction * 2), 0f, 150,
                    new Color(Main.DiscoR, Main.DiscoG, Main.DiscoB), 1.3f);
                Main.dust[num250].velocity *= 0.2f;
                Main.dust[num250].noGravity = true;
            }
        }

        public override void OnHitNPC(Player player, NPC target, NPC.HitInfo hit, int damageDone)
        {
            player.Heal(75);
            player.AddBuff(ModContent.BuffType<AndromedasRage>(), 300);
            target.AddBuff(ModContent.BuffType<CelestialFlames>(), 300);

            // Shared helper — same effects the projectiles apply.
            CelestialHitEffects.ApplyPlayerBiomeBuffs(player);
            CelestialHitEffects.ApplyEnemyDebuffs(target);
        }
    }
}
