using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace CelestialAerus.QOL;

/// <summary>
/// Super Tools config (ported from Daedulus's Super Tools Mod v2.0).
/// Client-side: each player configures their own tool boosts.
/// </summary>
public class SuperToolsConfig : ModConfig
{
    [DefaultValue(true)]
    public bool EnableSuperTools;

    [DefaultValue(true)]
    public bool MaxToolSpeed;

    [DefaultValue(true)]
    public bool MaxToolPower;

    [DefaultValue(true)]
    public bool InfiniteRange;

    [Range(1, 1000)]
    [DefaultValue(1000)]
    public int ToolPowerValue;

    [Range(1, 20)]
    [DefaultValue(1)]
    public int ToolUseTime;

    public override ConfigScope Mode => ConfigScope.ClientSide;
}
