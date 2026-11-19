using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Microsoft.Xna.Framework;
using Terraria.Localization;

namespace CelestialAerus.AerusBuffs
{
    public class CelestialFlames : ModBuff
    {
        public override string Texture => "CelestialAerus/Assets/Textures/CelestialFireball"; // Use existing projectile texture as buff icon

        public override void SetStaticDefaults()
        {
            Main.debuff[Type] = true;
            Main.buffNoSave[Type] = true;
            Main.buffNoTimeDisplay[Type] = false;
        }

        public override void Update(NPC npc, ref int buffIndex)
        {
            // Apply the intended 750 damage per second through vanilla's
            // regeneration pipeline, independent of the refreshable timer.
            if (npc.lifeRegen > 0)
                npc.lifeRegen = 0;
            npc.lifeRegen -= 1500;

            // Reduced dust particles for better performance
            if (Main.netMode != NetmodeID.Server && Main.rand.NextBool(12))
            {
                Dust dust = Dust.NewDustDirect(
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
                if (dust != null)
                    dust.noGravity = true;
            }
        }
    }
}
