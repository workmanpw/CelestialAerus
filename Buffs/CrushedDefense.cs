using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Microsoft.Xna.Framework;
using Terraria.Localization;

namespace CelestialAerus.AerusBuffs
{
    public class CrushedDefense : ModBuff
    {
        public override string Texture => "CelestialAerus/Textures/CelestialFireball"; // Use Celestial Fireball texture as icon

        public override void SetStaticDefaults()
        {
            Main.debuff[Type] = true;
            Main.buffNoSave[Type] = true;
            Main.buffNoTimeDisplay[Type] = false;
        }

        public override LocalizedText DisplayName => Language.GetOrRegister("Crushed Defense");
        public override LocalizedText Description => Language.GetOrRegister("Your defenses have been utterly shattered by cosmic power");

        public override void Update(NPC npc, ref int buffIndex)
        {
            // Reduce defense by 100% (no defense left)
            npc.defense = 0;

            // Visual effect - purple dust particles
            if (Main.rand.NextBool(8)) // 12.5% chance per tick
            {
                int dustIndex = Dust.NewDust(
                    npc.position,
                    npc.width,
                    npc.height,
                    DustID.Shadowflame, // Purple shadow flame dust for dark effect
                    Main.rand.NextFloat(-1f, 1f),
                    Main.rand.NextFloat(-1f, 1f),
                    100,
                    new Color(128, 0, 128), // Purple color
                    1.2f
                );
                Main.dust[dustIndex].noGravity = true;
            }
        }
    }
}
