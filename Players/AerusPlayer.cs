using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Terraria.GameInput;
using CelestialAerus.Items;
using CelestialAerus.Systems;

namespace CelestialAerus.Players
{
    public class AerusPlayer : ModPlayer
    {
        public int aerusMode = 0;
        private int reuseTimer;
        public int aerusFireballCooldown;
        public int celestialCooldown;
        public int arkCooldown;

        public override void ProcessTriggers(TriggersSet triggersSet)
        {
            if (KeybindSystem.SwitchModeKeybind.JustPressed)
            {
                aerusMode = (aerusMode + 1) % 2;
                string mode = aerusMode == 1 ? "Boomerang" : "Fireball";
                Main.NewText($"Aerus Mode: {mode}", Color.LightGreen);
            }

            var item = Player.HeldItem;
            if (item.type == ModContent.ItemType<global::CelestialAerus.Items.CelestialAerus>() &&
                aerusMode == 1 &&
                triggersSet.MouseLeft)
            {
                if (reuseTimer <= 0)
                {
                    Player.controlUseItem = true;
                    Player.releaseUseItem = false;
                    reuseTimer = item.useTime;
                }
            }
        }

        public override void PostUpdate()
        {
            if (reuseTimer > 0) reuseTimer--;
            if (aerusFireballCooldown > 0) aerusFireballCooldown--;
            if (celestialCooldown > 0) celestialCooldown--;
            if (arkCooldown > 0) arkCooldown--;
        }

        public override void ResetEffects()
        {
            if (celestialCooldown > 0) celestialCooldown--;
            if (arkCooldown > 0) arkCooldown--;
        }
    }
}
