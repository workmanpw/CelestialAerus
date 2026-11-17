using Terraria;
using Terraria.ModLoader;

namespace CelestialAerus.QOL;

/// <summary>
/// Grants effectively-infinite tile reach while InfiniteRange is on.
/// (Ported from Daedulus's Super Tools Mod v2.0.)
/// </summary>
public class SuperToolsPlayer : ModPlayer
{
    // LAG FIX (2026-11-15): the original mod set tileRangeX/tileRangeY to
    // 9999. The game's range-scanning code (smart cursor first among them)
    // iterates over a (2*range+1) x (2*range+1) tile area every frame — at
    // 9999 that's ~400 MILLION tiles per scan, which freezes the game solid.
    // 75 tiles is ~1.5 screens in every direction: effectively infinite for
    // gameplay (you can reach anything you can see and well beyond), while
    // keeping the scan to ~23K tiles worst case.
    private const int SafeInfiniteRange = 75;

    public override void ResetEffects()
    {
        SuperToolsConfig config = ModContent.GetInstance<SuperToolsConfig>();
        if (config != null && config.EnableSuperTools && config.InfiniteRange)
        {
            Player.tileRangeX = SafeInfiniteRange;
            Player.tileRangeY = SafeInfiniteRange;
        }
    }
}
