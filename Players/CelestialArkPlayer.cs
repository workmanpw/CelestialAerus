using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;
using CelestialAerus.Items;
using CelestialAerus.Projectiles;

namespace CelestialAerus.Players
{
    public class CelestialArkPlayer : ModPlayer
    {
        public bool summonFireballs = false;
        private int fireballTimer = 0;
        private const int FireballDelay = 10;
        private readonly List<Projectile> activeFireballs = new List<Projectile>();
        public int arkCooldown;

        public override void ResetEffects()
        {
            if (Player.HeldItem.type != ModContent.ItemType<ArkOfTheCelestials>())
                summonFireballs = false;
        }

        public override void PostUpdate()
        {
            if (arkCooldown > 0)
                arkCooldown--;

            if (!summonFireballs || Player.dead || Player.HeldItem.type != ModContent.ItemType<ArkOfTheCelestials>())
            {
                activeFireballs.Clear();
                return;
            }

            activeFireballs.RemoveAll(p => !p.active || p.type != ModContent.ProjectileType<CelestialFireball>());

            if (activeFireballs.Count < 6 && Main.myPlayer == Player.whoAmI)
            {
                fireballTimer++;
                if (fireballTimer >= FireballDelay)
                {
                    fireballTimer = 0;
                    float radius = 100f;
                    float angle = Main.rand.NextFloat(MathHelper.TwoPi);
                    Vector2 spawnPos = Player.Center + new Vector2(
                        radius * (float)System.Math.Cos(angle),
                        radius * (float)System.Math.Sin(angle)
                    );

                    int p = Projectile.NewProjectile(
                        Player.GetSource_FromThis(),
                        spawnPos,
                        Vector2.Zero,
                        ModContent.ProjectileType<CelestialFireball>(),
                        Player.HeldItem.damage / 2,
                        Player.HeldItem.knockBack,
                        Player.whoAmI
                    );

                    if (p < Main.maxProjectiles)
                    {
                        Main.projectile[p].ai[0] = angle;
                        activeFireballs.Add(Main.projectile[p]);
                    }
                }
            }

            for (int i = 0; i < activeFireballs.Count; i++)
            {
                var proj = activeFireballs[i];
                if (proj.active && proj.type == ModContent.ProjectileType<CelestialFireball>())
                {
                    float radius = 100f;
                    float angle = proj.ai[0] + 0.02f;
                    proj.ai[0] = angle % MathHelper.TwoPi;

                    Vector2 targetPos = Player.Center + new Vector2(
                        radius * (float)System.Math.Cos(angle),
                        radius * (float)System.Math.Sin(angle)
                    );

                    proj.velocity = (targetPos - proj.Center) * 0.2f;
                    if (proj.velocity != Vector2.Zero)
                        proj.rotation = proj.velocity.ToRotation() + MathHelper.PiOver2;
                }
            }
        }
    }
}
