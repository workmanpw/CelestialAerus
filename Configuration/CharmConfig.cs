using System.ComponentModel;
using Terraria.ModLoader.Config;

namespace CelestialAerus.Configs
{
    // Brigitte's Charm config (Peyton, 2026-11-18): storm fire rate
    // and size, toggles for each minion group, and the charm's flat
    // damage. EVERY default preserves the current behavior — the
    // storm, summons, bosses, and weapons are all active unless the
    // player goes in and changes something. Client-side: it only
    // controls the player's own charm.
    public class CharmConfig : ModConfig
    {
        public override ConfigScope Mode => ConfigScope.ClientSide;

        [Header("Storm")]

        [DefaultValue(true)]
        public bool StormEnabled;

        // Ticks between storm rings (60 ticks = 1 second). Lower is
        // faster; 1 fires a ring every tick.
        [Range(1, 60)]
        [DefaultValue(4)]
        public int StormFireRate;

        // Projectiles in each storm ring. Bigger rings cover more
        // angles but burn through the active-projectile cap faster.
        [Range(1, 40)]
        [DefaultValue(10)]
        public int StormProjectilesPerRing;

        // Maximum storm projectiles alive at once. The ring stops
        // growing past this cap; lower it if the storm lags the game.
        [Range(10, 500)]
        [DefaultValue(200)]
        public int StormMaxProjectiles;

        [Header("Minions")]

        // One of every vanilla minion and sentry.
        [DefaultValue(true)]
        public bool VanillaMinionsEnabled;

        // The ten Aerus weapon minions.
        [DefaultValue(true)]
        public bool WeaponMinionsEnabled;

        // The 21 boss minions (and the three True Eyes of Cthulhu).
        [DefaultValue(true)]
        public bool BossMinionsEnabled;

        [Header("Damage")]

        // Flat typeless damage for every charm minion and storm
        // projectile. Applies to newly spawned minions and rings.
        [Range(100, 100000)]
        [DefaultValue(6000)]
        public int CharmDamage;
    }
}
