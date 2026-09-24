using System;
using Terraria.Localization;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Microsoft.Xna.Framework;

namespace CelestialAerus.AerusBuffs
{
    public class AndromedasRage : ModBuff
    {
        public override string Texture => "CelestialAerus/Textures/CelestialFireball";

        public override void SetStaticDefaults()
        {
            Main.buffNoSave[Type] = true;
            Main.buffNoTimeDisplay[Type] = false;
        }

        public override LocalizedText DisplayName => Language.GetOrRegister("Andromeda's Rage");
        public override LocalizedText Description => Language.GetOrRegister("The fury of a galaxy fuels your attacks");

        public override void Update(Player player, ref int buffIndex)
        {
            // Grant 50% damage increase for 5 seconds (300 ticks)
            player.GetDamage(DamageClass.Generic) += 0.5f;

            // Visual effect - golden dust particles
            if (Main.rand.NextBool(8)) // 12.5% chance per tick
            {
                int dustIndex = Dust.NewDust(
                    player.position,
                    player.width,
                    player.height,
                    DustID.GoldFlame,
                    Main.rand.NextFloat(-1f, 1f),
                    Main.rand.NextFloat(-1f, 1f),
                    100,
                    new Color(255, 215, 0), // Gold color
                    1.2f
                );
                Main.dust[dustIndex].noGravity = true;
            }
        }
    }
}
