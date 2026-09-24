using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Microsoft.Xna.Framework;
using Terraria.Localization;

namespace CelestialAerus.AerusBuffs
{
    public class CelestialFlames : ModBuff
    {
        public override string Texture => "CelestialAerus/Textures/CelestialFireball"; // Use existing projectile texture as buff icon

        public override void SetStaticDefaults()
        {
            Main.debuff[Type] = true;
            Main.buffNoSave[Type] = true;
            Main.buffNoTimeDisplay[Type] = false;
        }

        public override LocalizedText DisplayName => Language.GetOrRegister("Celestial Flames");
        public override LocalizedText Description => Language.GetOrRegister("Burning with cosmic fire");

        public override void Update(NPC npc, ref int buffIndex)
        {
            // Nerfed timer system - deal damage every 1 second (60 ticks at 60 FPS) instead of every 0.25 seconds
            if (npc.buffTime[buffIndex] % 60 == 0)
            {
                // Significantly reduced damage: 750 damage every 1 second instead of 5000 every 0.25 seconds
                npc.life -= 750;
            }

            // Reduced dust particles for better performance
            if (Main.rand.NextBool(12)) // Reduced from 8 to 12 for less frequent particles
            {
                int dustIndex = Dust.NewDust(
                    npc.position,
                    npc.width,
                    npc.height,
                    DustID.RainbowTorch, // Rainbow colored dust
                    Main.rand.NextFloat(-1.5f, 1.5f), // Reduced velocity range
                    Main.rand.NextFloat(-1.5f, 1.5f), // Reduced velocity range
                    100,
                    new Color(Main.DiscoR, Main.DiscoG, Main.DiscoB), // Rainbow color
                    1.2f // Reduced scale
                );
                Main.dust[dustIndex].noGravity = true;
            }

            // Kill NPC if life drops to 0 or below
            if (npc.life <= 0)
            {
                npc.life = 0;
                npc.checkDead();
            }
        }
    }
}
