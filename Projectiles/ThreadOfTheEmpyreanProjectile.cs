using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using CelestialAerus.Projectiles;

namespace CelestialAerus.Projectiles.ThreadOfTheEmpyrean
{
    public class ThreadOfTheEmpyreanProjectile : ModProjectile
    {
        private int fireTimer;
        private List<Vector2> trailPositions = new List<Vector2>();
        private int trailTimer = 0;

        public override string Texture => "CelestialAerus/Textures/ThreadOfTheEmpyrean";

        public override void SetStaticDefaults()
        {
        }

        public override LocalizedText DisplayName => Language.GetOrRegister("Empyrean Yo-Yo");

        public override void SetDefaults()
        {
            Projectile.width = 28;
            Projectile.height = 28;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.aiStyle = ProjAIStyleID.Yoyo;
            Projectile.penetrate = -1;
            Projectile.timeLeft = 999999;
            Projectile.ignoreWater = true;
            Projectile.tileCollide = false;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 60;

            ProjectileID.Sets.YoyosLifeTimeMultiplier[Projectile.type] = 999f;
            ProjectileID.Sets.YoyosMaximumRange[Projectile.type] = 999f;
            ProjectileID.Sets.YoyosTopSpeed[Projectile.type] = 80f;
        }

        public override void AI()
        {
            fireTimer++;
            if (fireTimer >= 15)
            {
                fireTimer = 0;
                Vector2 spawn = Projectile.Center;
                Vector2 dir = Vector2.Normalize(Main.MouseWorld - spawn) * 12f;
                int p = Projectile.NewProjectile(
                    Projectile.GetSource_FromThis(),
                    spawn, dir,
                    ModContent.ProjectileType<CelestialFireball>(),
                    Projectile.damage / 2, 2f, Projectile.owner
                );
                Main.projectile[p].tileCollide = false;
                Main.projectile[p].timeLeft = 300;
                Main.projectile[p].usesLocalNPCImmunity = true;
                Main.projectile[p].localNPCHitCooldown = 40;
            }

            trailTimer++;
            if (trailTimer >= 2)
            {
                trailPositions.Add(Projectile.Center);
                if (trailPositions.Count > 15)
                    trailPositions.RemoveAt(0);
                trailTimer = 0;
            }

            float intensity = 0.8f + (float)Math.Sin(Main.time * 0.3f) * 0.4f;
            Lighting.AddLight(Projectile.Center,
                Main.DiscoR / 255f * intensity,
                Main.DiscoG / 255f * intensity,
                Main.DiscoB / 255f * intensity);
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            Player player = Main.player[Projectile.owner];
            player.Heal(175);
        }

        public override Color? GetAlpha(Color lightColor)
            => new Color(Main.DiscoR, Main.DiscoG, Main.DiscoB);

    }
}
