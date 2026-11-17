using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace CelestialAerus.Config
{
    public class CelestialAerusConfig : ModConfig
    {
        public override ConfigScope Mode => ConfigScope.ClientSide;

        [Header("TempestWeaponBehavior")]
        [DefaultValue(true)]
        public bool EnableCelestialTempestRightClickModeToggle = true;
    }
}
