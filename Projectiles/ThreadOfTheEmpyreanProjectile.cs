using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria.GameContent;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using CelestialAerus.Projectiles;
using CelestialAerus.Effects;

namespace CelestialAerus.Projectiles.ThreadOfTheEmpyrean
{
    public class ThreadOfTheEmpyreanProjectile : ModProjectile
    {
        private int fireTimer;
        // 300px jump threshold: the yoyo legitimately moves up to 80px per
        // tick in flight and 120px per tick on its return dash (vanilla
        // yoyo AI: return speed is 1.5x top speed), and points are recorded
        // every 2 ticks, so real gaps reach 240px. The default 150px
        // threshold kept clearing the trail mid-flight, which made the
        // rainbow ribbon flicker in and out of existence (2026-11-15).
        private readonly TrailRecorder trail = new TrailRecorder(15, 300f);
        private int trailTimer = 0;

        public override string Texture => "CelestialAerus/Textures/ThreadOfTheEmpyrean";

        public override void SetStaticDefaults()
        {
        }

        public override void SetDefaults()
        {
            // Hitbox already matches the drawn sprite: 28x28 texture at scale 1.
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
            // Buff: was 60 (only 1 hit per second per enemy). 20 lets the
            // yo-yo shred instead of tickling.
            Projectile.localNPCHitCooldown = 20;

            ProjectileID.Sets.YoyosLifeTimeMultiplier[Projectile.type] = 999f;
            ProjectileID.Sets.YoyosMaximumRange[Projectile.type] = 999f;
            ProjectileID.Sets.YoyosTopSpeed[Projectile.type] = 80f;
        }

        public override void AI()
        {
            // FIREBALL MODE (Aerus armor, 2026-11-15): when the armor spawns
            // this yoyo it sets aiStyle = 0, disabling the vanilla yoyo AI
            // (no tether, no return). The yoyo keeps everything else — its
            // spinning, its fireball-spawning, its rainbow trail, its disco
            // light — but it HOMES at enemies like a Celestial Fireball
            // (same Lerp 0.15 steering, same 750px detection range).
            // A player-thrown yoyo (aiStyle = Yoyo) is untouched.
            if (Projectile.aiStyle == 0)
            {
                float maxDetect = 750f;
                float homingSpeed = 16f;
                NPC target = null;
                float minDist = maxDetect;
                foreach (NPC npc in Main.npc)
                {
                    if (npc.CanBeChasedBy(Projectile, false))
                    {
                        float dist = Vector2.Distance(Projectile.Center, npc.Center);
                        if (dist < minDist)
                        {
                            minDist = dist;
                            target = npc;
                        }
                    }
                }
                if (target != null)
                {
                    Vector2 toTarget = (target.Center - Projectile.Center).SafeNormalize(Vector2.Zero);
                    if (toTarget != Vector2.Zero)
                        Projectile.velocity = Vector2.Lerp(Projectile.velocity, toTarget * homingSpeed, 0.15f);
                }
                Projectile.rotation += 0.3f;
            }

            fireTimer++;
            // Buff: fires every 10 ticks (was 15), and the fireballs now deal
            // full yo-yo damage (was half) and re-hit faster (cooldown 20).
            if (fireTimer >= 10)
            {
                fireTimer = 0;
                // Owner-only: AI runs on the server too, where Main.MouseWorld
                // is (0,0) — spawning there would double the fireballs and
                // send the server's copies flying toward the world origin.
                if (Projectile.owner == Main.myPlayer)
                {
                    Vector2 spawn = Projectile.Center;
                    Vector2 dir = (Main.MouseWorld - spawn).SafeNormalize(Vector2.UnitX) * 12f;
                    int p = Projectile.NewProjectile(
                        Projectile.GetSource_FromThis(),
                        spawn, dir,
                        ModContent.ProjectileType<CelestialFireball>(),
                        Projectile.damage, 2f, Projectile.owner
                    );
                    if (p >= 0 && p < Main.maxProjectiles)
                    {
                        Main.projectile[p].tileCollide = false;
                        Main.projectile[p].timeLeft = 300;
                        Main.projectile[p].usesLocalNPCImmunity = true;
                        Main.projectile[p].localNPCHitCooldown = 20;
                        Main.projectile[p].netUpdate = true;
                    }
                }
            }

            trailTimer++;
            if (trailTimer >= 2)
            {
                trail.Record(Projectile.Center);
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
            Player player = Projectile.owner >= 0 && Projectile.owner < Main.maxPlayers
                ? Main.player[Projectile.owner]
                : null;
            if (player == null || !player.active)
                return;
            int lifeStealAmount = 175;
            if (player.statLife + lifeStealAmount > player.statLifeMax2)
                lifeStealAmount = player.statLifeMax2 - player.statLife;
            player.Heal(lifeStealAmount);
            player.HealEffect(lifeStealAmount);
        }

        public override Color? GetAlpha(Color lightColor)
            => new Color(Main.DiscoR, Main.DiscoG, Main.DiscoB);

        public override bool PreDraw(ref Color lightColor)
        {
            Color lightColorCopy = lightColor;
            Texture2D texture = TextureAssets.Projectile[Type].Value;

            // Start the ribbon at the yo-yo's rear edge and give it the
            // projectile's full width at the head, tapering to a point.
            float trailStartOffset = texture.Height * 0.5f * Projectile.scale;
            float trailHalfWidth = Math.Max(Projectile.width, Projectile.height) * 0.5f * Projectile.scale; // VertexStrip treats this as a half-width

            ShaderTrailDraw.Draw(
                trail.Positions,
                trail.Count,
                progress => ShaderTrailDraw.WithAlpha(
                    new Color(Main.DiscoR, Main.DiscoG, Main.DiscoB), progress * progress * 0.8f),
                progress => trailHalfWidth * (float)Math.Pow(progress, 1.15f),
                "CelestialRibbon",
                effect =>
                {
                    SafeShaderDraw.Set(effect, "TrailStartColor", Color.White);
                    SafeShaderDraw.Set(effect, "TrailEndColor", Color.White);
                    SafeShaderDraw.Set(effect, "TrailPulseSpeed", 4f);
                    SafeShaderDraw.Set(effect, "TrailPulseIntensity", 0.22f);
                },
                trailStartOffset);

            SafeShaderDraw.DrawWithShader("CelestialTrail",
                () =>
                {
                    Vector2 origin = texture.Size() / 2f;

                    // Draw yo-yo head
                    Main.EntitySpriteDraw(
                        texture,
                        Projectile.Center - Main.screenPosition,
                        null,
                        Projectile.GetAlpha(lightColorCopy),
                        Projectile.rotation,
                        origin,
                        Projectile.scale,
                        SpriteEffects.None,
                        0);
                },
                (effect) =>
                {
                    SafeShaderDraw.Set(effect, "TrailWidth", 8f);
                    SafeShaderDraw.Set(effect, "TrailStartColor", new Color(204, 102, 255));
                    SafeShaderDraw.Set(effect, "TrailEndColor", new Color(102, 26, 204));
                    SafeShaderDraw.Set(effect, "TrailFades", 0f);
                    SafeShaderDraw.Set(effect, "TrailFadeSpeed", 1f);
                    SafeShaderDraw.Set(effect, "TrailPulseSpeed", 4f);
                    SafeShaderDraw.Set(effect, "TrailPulseIntensity", 0.3f);
                    SafeShaderDraw.Set(effect, "TrailGlows", 1f);
                    SafeShaderDraw.Set(effect, "TrailGlowColor", new Color(179, 77, 255));
                });
            return false;
        }
    }
}
