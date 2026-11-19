using System;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Microsoft.Xna.Framework;

namespace CelestialAerus.AerusBuffs
{
    /// <summary>
    /// Celestial Empowerment (2026-11-14): the player half of the new buff
    /// system. Granted for 20 seconds whenever the player hits an enemy with
    /// any weapon (see AerusPlayer). A strict +200% to damage, movement
    /// speed, mining speed, and critical chance, +200 defense, 2000 HP healed
    /// per tick, plus further bonuses for every weapon class.
    /// </summary>
    public class CelestialEmpowerment : ModBuff
    {
        // Dedicated 32x32 buff icon (2026-11-15): the old Cosmosis texture
        // is 76x76, far larger than vanilla's 32x32 buff icons.
        public override string Texture => "CelestialAerus/Assets/Textures/CelestialEmpowerment";

        public override void SetStaticDefaults()
        {
            Main.buffNoSave[Type] = true;
            Main.buffNoTimeDisplay[Type] = false;
        }

        public override void Update(Player player, ref int buffIndex)
        {
            // Strict +200% damage, every class (triple damage).
            player.GetDamage(DamageClass.Generic) += 2.0f;

            // Strict +200% movement speed (triple speed).
            player.moveSpeed += 2.0f;

            // Strict +200% mining speed: each block takes a third of the
            // time. pickSpeed is a time multiplier, so subtract two thirds.
            // Floored so stacking mining bonuses can never push it negative.
            player.pickSpeed -= 2f / 3f;
            if (player.pickSpeed < 0.1f)
                player.pickSpeed = 0.1f;

            // Strict +200 critical chance (percentage points): every hit crits.
            player.GetCritChance(DamageClass.Generic) += 200;

            // Flat +200 defense.
            player.statDefense += 200;

            // Further bonuses, every weapon class:
            player.GetAttackSpeed(DamageClass.Generic) += 0.2f;      // +20% attack speed
            player.GetKnockback(DamageClass.Generic) += 2.0f;        // +200% knockback
            player.GetArmorPenetration(DamageClass.Generic) += 200;  // +200 armor penetration
            player.statManaMax2 += 200;                              // +200 max mana
            player.manaCost -= 0.5f;                                 // -50% mana cost
            player.maxMinions += 5;                                  // +5 minion slots
            player.maxTurrets += 5;                                  // +5 sentry slots
            player.noKnockback = true;                               // immune to knockback

            // Guaranteed 2000 health per tick. Healed directly (not through
            // lifeRegen) so no regen-suppressing debuff can interfere.
            // Heal() clamps at max HP and is silent (no combat-text spam).
            player.Heal(2000);

            // Visual effect - golden dust particles.
            if (Main.rand.NextBool(8)) // 12.5% chance per tick
            {
                Dust dust = Dust.NewDustDirect(
                    player.position,
                    player.width,
                    player.height,
                    DustID.GoldFlame,
                    Main.rand.NextFloat(-1f, 1f),
                    Main.rand.NextFloat(-1f, 1f),
                    100,
                    new Color(255, 215, 0), // Gold color
                    1.2f
                );
                if (dust != null)
                    dust.noGravity = true;
            }
        }
    }
}
