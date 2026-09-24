using Terraria.ModLoader;
using Terraria;

namespace CelestialAerus.Systems
{
    public class KeybindSystem : ModSystem
    {
        public static ModKeybind SwitchModeKeybind;

        public override void Load()
        {
            if (!Main.dedServ)
            {
                SwitchModeKeybind = KeybindLoader.RegisterKeybind(Mod, "Switch Aerus Mode", "V");
            }
        }

        public override void Unload()
        {
            SwitchModeKeybind = null;
        }
    }
}
