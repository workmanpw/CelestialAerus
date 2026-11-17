using Terraria;
using Terraria.ModLoader;

namespace CelestialAerus.QOL;

/// <summary>
/// Applies the Super Tools boosts to every pickaxe, axe, and hammer:
/// use time down to ToolUseTime, power up to ToolPowerValue.
/// (Ported from Daedulus's Super Tools Mod v2.0.)
/// </summary>
public class SuperToolsGlobalItem : GlobalItem
{
    public override void SetDefaults(Item item)
    {
        SuperToolsConfig config = ModContent.GetInstance<SuperToolsConfig>();
        if (config == null || !config.EnableSuperTools || (item.pick <= 0 && item.axe <= 0 && item.hammer <= 0))
        {
            return;
        }
        if (config.MaxToolSpeed)
        {
            item.useTime = config.ToolUseTime;
            item.useAnimation = config.ToolUseTime;
        }
        if (config.MaxToolPower)
        {
            if (item.pick > 0)
            {
                item.pick = config.ToolPowerValue;
            }
            if (item.axe > 0)
            {
                item.axe = config.ToolPowerValue;
            }
            if (item.hammer > 0)
            {
                item.hammer = config.ToolPowerValue;
            }
        }
    }
}
