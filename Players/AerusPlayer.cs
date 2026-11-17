using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.GameInput;
using CelestialAerus.Items;
using CelestialAerus.Systems;
using CelestialAerus.AerusBuffs;

namespace CelestialAerus.Players
{
    public class AerusPlayer : ModPlayer
    {
        public int aerusMode = 0;
        public bool tempestMode = false;

        public override void ProcessTriggers(TriggersSet triggersSet)
        {
            if (KeybindSystem.SwitchModeKeybind != null && KeybindSystem.SwitchModeKeybind.JustPressed)
            {
                // Only toggle when the player is actually holding the
                // Celestial Aerus — otherwise every V press spams the
                // chat line while using other weapons.
                if (Player.HeldItem.type == ModContent.ItemType<global::CelestialAerus.Items.CelestialAerus>())
                {
                    aerusMode = (aerusMode + 1) % 2;
                    string mode = aerusMode == 1 ? "Boomerang" : "Fireball";
                    Main.NewText($"Aerus Mode: {mode}", Color.LightGreen);
                }
            }

            // NOTE (v2.0): the old custom reuseTimer hack that forced
            // controlUseItem every useTime ticks in Boomerang mode is gone.
            // It could reset its timer on a tick where the item was not
            // ready yet, silently skipping swings. The Celestial Aerus item
            // has autoReuse = true, so the game now fires a thrown blade
            // on every swing exactly like the Ancient Hatchet does.
        }

        public override void SaveData(TagCompound tag)
        {
            tag["aerusMode"] = aerusMode;
        }

        // New buff system (2026-11-14): hitting an enemy with a Celestial
        // Aerus weapon — melee swing or mod projectile — grants the player
        // Celestial Empowerment and inflicts Celestial Obliteration on the
        // enemy, both for 20 seconds (1200 ticks). Re-hitting refreshes
        // the timer. Vanilla and other mods' weapons do NOT trigger it:
        // each hook checks that the item/projectile belongs to this mod.
        // Both hooks are needed: direct swings fire OnHitNPCWithItem,
        // projectiles (including the yoyo) fire OnHitNPCWithProj.
        public override void OnHitNPCWithItem(Item item, NPC target, NPC.HitInfo hit, int damageDone)
        {
            if (item.ModItem == null || item.ModItem.Mod != Mod)
                return;
            ApplyCelestialBuffs(target);
        }

        public override void OnHitNPCWithProj(Projectile proj, NPC target, NPC.HitInfo hit, int damageDone)
        {
            if (proj.ModProjectile == null || proj.ModProjectile.Mod != Mod)
                return;
            ApplyCelestialBuffs(target);
        }

        private void ApplyCelestialBuffs(NPC target)
        {
            Player.AddBuff(ModContent.BuffType<CelestialEmpowerment>(), 1200);

            // No counter: clear the target's immunity to Obliteration
            // first, so even bosses that resist every other debuff take it.
            target.buffImmune[ModContent.BuffType<CelestialObliteration>()] = false;
            target.AddBuff(ModContent.BuffType<CelestialObliteration>(), 1200);
        }

        public override void LoadData(TagCompound tag)
        {
            // GetInt returns 0 when the key is missing (e.g. saves from
            // before this was added), which is Fireball mode — the default.
            aerusMode = tag.GetInt("aerusMode");
        }

        public override void ResetEffects()
        {
            // Do not decrement cooldowns here. ResetEffects is called in the same
            // update loop as PostUpdate, so decrementing here would halve the
            // effective cooldown durations and make timers expire twice as fast.
        }
    }
}
