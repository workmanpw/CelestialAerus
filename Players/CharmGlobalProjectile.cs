using System;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CelestialAerus.Players
{
    // Charm-wide local immunity frames + storm homing (2026-11-15).
    //
    // LOCAL FRAMES (Peyton's rule: "Charm only."): the charm's own spawns
    // (storm, cascade, weapon minions and their attacks, summoned vanilla
    // minions) all set usesLocalNPCImmunity directly. The ONE thing that
    // needs this global is the shots fired by the charm's summoned vanilla
    // minions (Hornet stingers, Imp fireballs, UFO lasers, etc.) — vanilla
    // AI spawns them with global immunity frames, so one hit would block
    // the other 39 allies for 10 ticks. This global gives local frames
    // ONLY to those 9 minion-shot types, and ONLY while the charm is
    // equipped. The player's own weapon shots, other mods' projectiles,
    // and everything else keep vanilla iframe rules.
    //
    // STORM HOMING (Peyton's order: "The projectiles should also auto-aim
    // and home in on enemies."): the storm and cascade mark their
    // projectiles with stormHoming = true when they spawn them. This global
    // then steers those projectiles toward the nearest enemy — a gentle
    // turn (0.15 rad per AI call, speed preserved), so the steering is
    // smooth with no sudden sharp turns. Only storm/cascade projectiles
    // home; the player's own weapon shots and the minions' shots are
    // untouched.
    public class CharmGlobalProjectile : GlobalProjectile
    {
        public override bool InstancePerEntity => true;

        // The 9 projectile types the charm's 29 summoned vanilla minions
        // shoot (verified from the decompiled minion AI, 2026-11-15).
        // 20 of the 29 minions are contact-only and fire nothing.
        private static readonly int[] MinionShotTypes = new int[]
        {
            ProjectileID.PygmySpear,              // 195 Pygmy x4
            ProjectileID.HornetStinger,           // 374 Hornet
            ProjectileID.ImpFireball,             // 376 Flying Imp
            ProjectileID.MiniRetinaLaser,         // 389 Retanimini
            ProjectileID.MiniSharkron,            // 408 Tempest
            ProjectileID.UFOLaser,                // 433 UFO
            ProjectileID.StardustCellMinionShot,  // 614 Stardust Cell
            ProjectileID.StardustGuardianExplosion, // 624 Stardust Guardian punch
            ProjectileID.AbigailCounter,          // 970 Abigail reactive counter
        };

        // Set by CharmPlayer when it spawns a storm or cascade projectile.
        public bool stormHoming;

        // Cached homing target (re-scanned every 15 AI calls).
        private int homingTarget = -1;
        private int homingScanTimer;

        public override bool AppliesToEntity(Projectile entity, bool lateInstantiation)
        {
            return entity.friendly && entity.owner >= 0 && entity.owner < Main.maxPlayers;
        }

        public override void SetDefaults(Projectile projectile)
        {
            // Defensive reset: the flag starts false for every projectile.
            // Only the charm's own spawn sites (storm volley, cascade) ever
            // set it true, so nothing else in the game can home.
            stormHoming = false;

            // Local frames: ONLY the 9 minion-shot types, ONLY while the
            // charm is equipped. Everything else keeps vanilla rules.
            if (System.Array.IndexOf(MinionShotTypes, projectile.type) < 0)
                return;

            Player owner = Main.player[projectile.owner];
            if (owner == null || !owner.active)
                return;
            if (!owner.GetModPlayer<CharmPlayer>().charmEquipped)
                return;

            projectile.usesLocalNPCImmunity = true;
            projectile.localNPCHitCooldown = CharmPlayer.CharmLocalCooldown;
        }

        public override void AI(Projectile projectile)
        {
            if (!stormHoming)
                return;

            // Re-scan for a target every 15 AI calls, and whenever the
            // cached target is gone.
            if (--homingScanTimer <= 0 || !IsTargetValid(homingTarget))
            {
                homingScanTimer = 15;
                homingTarget = FindNearestTarget(projectile);
            }

            if (homingTarget < 0)
                return;

            NPC target = Main.npc[homingTarget];
            Vector2 toTarget = target.Center - projectile.Center;
            float speed = projectile.velocity.Length();
            if (speed <= 0f || toTarget == Vector2.Zero)
                return;

            // Gentle turn toward the target: rotate the velocity direction
            // by at most 0.15 rad per AI call, speed preserved. No sudden
            // sharp turns.
            float currentAngle = projectile.velocity.ToRotation();
            float desiredAngle = toTarget.ToRotation();
            float delta = MathHelper.WrapAngle(desiredAngle - currentAngle);
            float turn = MathHelper.Clamp(delta, -0.15f, 0.15f);
            projectile.velocity = projectile.velocity.RotatedBy(turn);
        }

        private static bool IsTargetValid(int whoAmI)
        {
            if (whoAmI < 0 || whoAmI >= Main.maxNPCs)
                return false;
            NPC npc = Main.npc[whoAmI];
            return npc.active && !npc.friendly && npc.lifeMax > 5 && !npc.dontTakeDamage && npc.type != NPCID.TargetDummy;
        }

        private static int FindNearestTarget(Projectile projectile)
        {
            int best = -1;
            float bestDistSq = 1000f * 1000f;
            foreach (NPC npc in Main.npc)
            {
                if (!npc.active || npc.friendly || npc.lifeMax <= 5 || npc.dontTakeDamage || npc.type == NPCID.TargetDummy)
                    continue;
                float distSq = Vector2.DistanceSquared(projectile.Center, npc.Center);
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = npc.whoAmI;
                }
            }
            return best;
        }
    }
}
