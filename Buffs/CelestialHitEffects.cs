using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CelestialAerus.AerusBuffs
{
    /// <summary>
    /// Shared hit-effect helpers so items and projectiles apply identical
    /// biome-based player buffs and enemy debuffs on hit.
    /// </summary>
    public static class CelestialHitEffects
    {
        public static void ApplyPlayerBiomeBuffs(Player player)
        {
            bool holy = player.ZoneHallow;
            bool nebula = player.ZoneTowerNebula;
            bool stardust = player.ZoneTowerStardust;
            bool solar = player.ZoneTowerSolar;
            bool vortex = player.ZoneTowerVortex;
            bool hell = player.ZoneUnderworldHeight;
            bool corrupt = player.ZoneCorrupt;
            bool crimson = player.ZoneCrimson;
            bool forest = !corrupt && !crimson && !hell && !holy && !nebula && !stardust && !solar && !vortex
                          && !player.ZoneJungle && !player.ZoneSnow && !player.ZoneBeach && !player.ZoneDesert
                          && !player.ZoneDungeon && !player.ZoneGlowshroom && !player.ZoneSkyHeight;
            bool mushroom = player.ZoneGlowshroom;
            bool desert = player.ZoneDesert;
            bool pumpkinMoon = Main.pumpkinMoon;
            bool frostMoon = Main.snowMoon;
            bool bloodMoon = Main.bloodMoon;

            if (solar)
            {
                player.AddBuff(BuffID.Inferno, 300);
                player.AddBuff(BuffID.Swiftness, 600);
                player.AddBuff(BuffID.PaladinsShield, 600);
            }
            else if (nebula)
            {
                player.AddBuff(BuffID.NebulaUpDmg3, 600);
                player.AddBuff(BuffID.NebulaUpLife3, 600);
                player.AddBuff(BuffID.ManaRegeneration, 600);
            }
            else if (vortex)
            {
                player.AddBuff(BuffID.Titan, 900);
                player.AddBuff(BuffID.AmmoReservation, 600);
                player.AddBuff(BuffID.Invisibility, 300);
            }
            else if (stardust)
            {
                player.AddBuff(BuffID.Summoning, 600);
                player.AddBuff(BuffID.Bewitched, 600);
                player.AddBuff(BuffID.RapidHealing, 900);
            }
            else if (holy)
            {
                player.AddBuff(BuffID.Shine, 600);
                player.AddBuff(BuffID.Thorns, 600);
                player.AddBuff(BuffID.Endurance, 600);
            }
            else if (hell)
            {
                player.AddBuff(BuffID.ObsidianSkin, 600);
                player.AddBuff(BuffID.Inferno, 300);
                player.AddBuff(BuffID.Mining, 600);
            }
            else if (corrupt)
            {
                player.AddBuff(BuffID.Wrath, 900);
                player.AddBuff(BuffID.Dangersense, 600);
                player.AddBuff(BuffID.ShadowDodge, 600);
            }
            else if (crimson)
            {
                player.AddBuff(BuffID.Rage, 900);
                player.AddBuff(BuffID.Battle, 900);
                player.AddBuff(BuffID.Lifeforce, 600);
            }
            else if (desert)
            {
                player.AddBuff(BuffID.Swiftness, 600);
                player.AddBuff(BuffID.Mining, 600);
                player.AddBuff(BuffID.Spelunker, 600);
            }
            else if (player.ZoneSnow)
            {
                player.AddBuff(BuffID.IceBarrier, 600);
                player.AddBuff(BuffID.Featherfall, 600);
                player.AddBuff(BuffID.Warmth, 600);
            }
            else if (player.ZoneJungle)
            {
                player.AddBuff(BuffID.BoneJavelin, 900);
                player.AddBuff(BuffID.Spelunker, 600);
                player.AddBuff(BuffID.Regeneration, 600);
            }
            else if (player.ZoneBeach)
            {
                player.AddBuff(BuffID.Gills, 600);
                player.AddBuff(BuffID.Flipper, 600);
                player.AddBuff(BuffID.WaterWalking, 600);
            }
            else if (mushroom)
            {
                player.AddBuff(BuffID.ManaRegeneration, 900);
                player.AddBuff(BuffID.MagicPower, 900);
                player.AddBuff(BuffID.Lucky, 600);
            }
            else if (forest)
            {
                player.AddBuff(BuffID.WellFed3, 900);
                player.AddBuff(BuffID.PeaceCandle, 600);
                player.AddBuff(BuffID.Regeneration, 600);
            }
            else if (pumpkinMoon)
            {
                player.AddBuff(BuffID.Battle, 900);
                player.AddBuff(BuffID.NightOwl, 600);
                player.AddBuff(BuffID.Lucky, 600);
            }
            else if (frostMoon)
            {
                player.AddBuff(BuffID.Rage, 900);
                player.AddBuff(BuffID.IceBarrier, 600);
                player.AddBuff(BuffID.MagicPower, 600);
            }
            else if (bloodMoon)
            {
                player.AddBuff(BuffID.Tipsy, 600);
                player.AddBuff(BuffID.Battle, 600);
                player.AddBuff(BuffID.RapidHealing, 900);
            }
            else
            {
                player.AddBuff(BuffID.Swiftness, 300);
            }
        }

        public static void ApplyEnemyDebuffs(NPC target)
        {
            target.AddBuff(BuffID.Ichor, 600);
            target.AddBuff(BuffID.CursedInferno, 600);
            target.AddBuff(BuffID.Venom, 600);
            target.AddBuff(BuffID.Weak, 600);
            target.AddBuff(ModContent.BuffType<CrushedDefense>(), 600);
            target.AddBuff(BuffID.OnFire, 600);
            target.AddBuff(BuffID.Poisoned, 600);
            target.AddBuff(BuffID.Frostburn, 600);
            target.AddBuff(BuffID.Daybreak, 600);
            target.AddBuff(BuffID.StardustMinion, 600);
        }
    }
}
