using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Microsoft.Xna.Framework;

namespace CelestialAerus.AerusBuffs
{
    /// <summary>
    /// Celestial Obliteration (2026-11-14): the enemy half of the new buff
    /// system. Applied for 20 seconds to enemies hit with attacks from any
    /// weapon (see AerusPlayer). Deals 2000 damage per tick and has no
    /// counter: the application hook clears the target's immunity to this
    /// buff first, so even bosses that shrug off every other debuff take it,
    /// and nothing in the game can remove an NPC's buffs before it expires.
    /// </summary>
    public class CelestialObliteration : ModBuff
    {
        // Dedicated 32x32 buff icon (2026-11-15): the old NebulaBlazeSword
        // texture is 120x120, far larger than vanilla's 32x32 buff icons.
        public override string Texture => "CelestialAerus/Textures/CelestialObliteration";

        public override void SetStaticDefaults()
        {
            Main.debuff[Type] = true;
            Main.buffNoSave[Type] = true;
            Main.buffNoTimeDisplay[Type] = false;
        }

        public override void Update(NPC npc, ref int buffIndex)
        {
            // 2000 damage per tick through vanilla's regeneration pipeline
            // (lifeRegen 120 = 1 HP per tick, so 240000 = 2000/tick).
            // Zero out positive regen first, like CelestialFlames does.
            if (npc.lifeRegen > 0)
                npc.lifeRegen = 0;
            npc.lifeRegen -= 240000;

            // Visual effect - shadowflame dust.
            if (Main.netMode != NetmodeID.Server && Main.rand.NextBool(8))
            {
                Dust dust = Dust.NewDustDirect(
                    npc.position,
                    npc.width,
                    npc.height,
                    DustID.Shadowflame,
                    Main.rand.NextFloat(-1.5f, 1.5f),
                    Main.rand.NextFloat(-1.5f, 1.5f),
                    100,
                    default,
                    1.5f
                );
                if (dust != null)
                    dust.noGravity = true;
            }
        }
    }
}
